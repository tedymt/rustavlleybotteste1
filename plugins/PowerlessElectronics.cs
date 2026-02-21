using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static IOEntity;

namespace Oxide.Plugins
{
    [Info("Powerless Electronics", "WhiteThunder", "1.4.0")]
    [Description("Allows electrical entities to generate their own power when not plugged in.")]
    internal class PowerlessElectronics : CovalencePlugin
    {
        #region Fields

        private const string PermissionAll = "powerlesselectronics.all";
        private const string PermissionEntityFormat = "powerlesselectronics.{0}";
        private const string ElectricSwitchPrefab = "assets/prefabs/deployable/playerioents/simpleswitch/switch.prefab";
        private const string CodeLockDeniedEffectPrefab = "assets/prefabs/locks/keypad/effects/lock.code.denied.prefab";

        private readonly object False = false;

        private static readonly Vector3 TurretSwitchPosition = new(0, 0.36f, -0.32f);
        private static readonly Quaternion TurretSwitchRotation = Quaternion.Euler(0, 180, 0);
        private static readonly Vector3 SamSiteSwitchPosition = new(0, 0.35f, -0.95f);
        private static readonly Quaternion SamSiteSwitchRotation = Quaternion.Euler(0, 180, 0);

        private const int AutoTurretInputSlot = 0;
        private const int SamSiteInputSlot = 0;

        private Configuration _config;
        private HashSet<IOEntity> _modifiedEntities = new();
        private DynamicHookSubscriber<ElectricSwitch> _attachedSwitches;
        private ProtectionProperties _immortalProtection;
        private StoredData _data;

        public PowerlessElectronics()
        {
            _attachedSwitches = new DynamicHookSubscriber<ElectricSwitch>(this,
                nameof(OnSwitchToggle),
                nameof(OnSwitchToggled),
                nameof(OnWireConnect),
                nameof(OnServerSave)
            );
        }

        #endregion

        #region Hooks

        private void Init()
        {
            _data = StoredData.Load();
            Unsubscribe(nameof(OnEntitySpawned));
            _attachedSwitches.UnsubscribeAll();
        }

        private void OnServerInitialized()
        {
            _immortalProtection = ScriptableObject.CreateInstance<ProtectionProperties>();
            _immortalProtection.name = $"{Name}Protection";
            _immortalProtection.Add(1);

            // Don't overwrite the config if invalid since the user will lose their config!
            if (!_config.UsingDefaults)
            {
                var didMigratePrefabs = _config.MigratePrefabs();
                var addedPrefabs = _config.AddMissingPrefabs();
                if (addedPrefabs != null)
                {
                    LogWarning($"Discovered and added {addedPrefabs.Count} electrical entity types to Configuration.\n - {string.Join("\n - ", addedPrefabs)}");
                }

                if (didMigratePrefabs || addedPrefabs != null)
                {
                    SaveConfig();
                }
            }

            _config.GeneratePermissionNames();

            // Register permissions only after discovering prefabs.
            permission.RegisterPermission(PermissionAll, this);
            foreach (var entry in _config.Entities)
            {
                permission.RegisterPermission(entry.Value.PermissionName, this);
            }

            foreach (var entity in BaseNetworkable.serverEntities)
            {
                var ioEntity = entity as IOEntity;
                if (ioEntity == null)
                    continue;

                ProcessIOEntity(ioEntity, delay: false);

                // Fix switch-able industrial entities that were stuck in the Busy state due to their power being
                // removed during server shutdown.
                if (ioEntity is IndustrialConveyor or IndustrialCrafter)
                {
                    ioEntity.SetFlag(BaseEntity.Flags.Busy, false);
                }
            }

            Subscribe(nameof(OnEntitySpawned));

            // Periodically clean up the list of modified entities to avoid memory leaks. Presumably this is less
            // expensive than hooking OnEntityKill or attaching an object to every modified entity.
            timer.Once(300, ForgetDestroyedEntities);
        }

        private void Unload()
        {
            UnityEngine.Object.Destroy(_immortalProtection);

            foreach (var ioEntity in _modifiedEntities)
            {
                if (ioEntity == null)
                    continue;

                // Skip resetting power for IndustrialConveyor and IndustrialCrafter entities during server reboot, so
                // their On state isn't forgotten (not saved by Rust) and so they don't get stuck Busy.
                if (Interface.Oxide.IsShuttingDown && ioEntity is IndustrialConveyor or IndustrialCrafter)
                    continue;

                ResetEntityPower(ioEntity);
            }

            _data.SaveEntitiesSwitchedOn(_attachedSwitches);

            foreach (var electricSwitch in _attachedSwitches)
            {
                if (electricSwitch == null)
                    continue;

                electricSwitch.Kill();
            }
        }

        private void OnServerSave()
        {
            _data.SaveEntitiesSwitchedOn(_attachedSwitches);
        }

        private void OnNewSave()
        {
            _data = StoredData.Reset();
        }

        private void OnEntitySpawned(IOEntity ioEntity)
        {
            ProcessIOEntity(ioEntity, delay: true);
        }

        private void OnIORefCleared(IORef ioRef, IOEntity ioEntity)
        {
            ProcessIOEntity(ioEntity, delay: true);
        }

        // Only subscribed while there are attached switches.
        // Require players to have building permission to toggle attached switches.
        private object OnSwitchToggle(ElectricSwitch electricSwitch, BasePlayer player)
        {
            if (!_attachedSwitches.Contains(electricSwitch))
                return null;

            if (player.CanBuild())
                return null;

            Effect.server.Run(CodeLockDeniedEffectPrefab, electricSwitch, 0, Vector3.zero, Vector3.forward);
            return False;
        }

        // Only subscribed while there are attached switches.
        private void OnSwitchToggled(ElectricSwitch electricSwitch)
        {
            if (!_attachedSwitches.Contains(electricSwitch))
                return;

            var parentIoEntity = electricSwitch.GetParentEntity() as IOEntity;
            if (parentIoEntity == null)
                return;

            if (electricSwitch.IsOn())
            {
                if (parentIoEntity is AutoTurret turret)
                {
                    var powerAmount = GetEntityConfig(turret)?.GetPowerForSlot(AutoTurretInputSlot) ?? 0;
                    if (powerAmount >= 0)
                    {
                        TryProvidePower(turret, AutoTurretInputSlot, powerAmount);
                    }
                }
                else if (parentIoEntity is SamSite samSite)
                {
                    var powerAmount = GetEntityConfig(samSite)?.GetPowerForSlot(SamSiteInputSlot) ?? 0;
                    if (powerAmount >= 0)
                    {
                        TryProvidePower(samSite, SamSiteInputSlot, powerAmount);
                    }
                }
            }
            else
            {
                if (parentIoEntity is AutoTurret turret)
                {
                    TryProvidePower(turret, AutoTurretInputSlot, 0);
                }
                else if (parentIoEntity is SamSite samSite)
                {
                    TryProvidePower(samSite, SamSiteInputSlot, 0);
                }
            }
        }

        // Only subscribed while there are attached switches.
        // Note: The first entity seems to always be the receiver.
        private void OnWireConnect(BasePlayer player, IOEntity ioEntity, int slot, IOEntity otherIoEntity, int otherSlot)
        {
            if (_config.AddSwitchToPowerlessAutoTurrets)
            {
                if (ioEntity is AutoTurret turret && slot == AutoTurretInputSlot)
                {
                    ProcessIOEntity(turret, delay: true);
                    return;
                }
            }

            if (_config.AddSwitchToPowerlessSamSites)
            {
                if (ioEntity is SamSite samSite && slot == SamSiteInputSlot)
                {
                    ProcessIOEntity(samSite, delay: true);
                    return;
                }
            }
        }

        #endregion

        #region Helper Methods

        private static bool InputUpdateWasBlocked(IOEntity ioEntity, int inputSlot, int amount)
        {
            return Interface.CallHook("OnPowerlessInputUpdate", inputSlot, ioEntity, amount) is false;
        }

        private static bool IsHybridIOEntity(IOEntity ioEntity)
        {
            return ioEntity is ElectricFurnaceIO or MicrophoneStandIOEntity or Hopper
                || (ioEntity is SimpleLight && ioEntity.GetParentEntity() is WeaponRack or FlagTogglePhotoFrame);
        }

        private static bool IsEntityNormallyParented(IOEntity ioEntity)
        {
            return IsHybridIOEntity(ioEntity)
                || ioEntity is IndustrialStorageAdaptor or IndustrialCrafter or StorageMonitor or DoorManipulator
                || ioEntity is SimpleLight && ioEntity.ShortPrefabName.Contains("neonsigntr");
        }

        private static BaseEntity GetOwnerEntity(IOEntity ioEntity)
        {
            var parent = ioEntity.GetParentEntity();
            if (parent is null)
                return ioEntity;

            return IsHybridIOEntity(ioEntity) ? parent : ioEntity;
        }

        private static T GetChildEntity<T>(BaseEntity entity) where T : BaseEntity
        {
            foreach (var child in entity.children)
            {
                var childOfType = child as T;
                if (childOfType != null)
                    return childOfType;
            }

            return null;
        }

        private static bool HasConnectedInput(IOEntity ioEntity, int inputSlot)
        {
            return inputSlot < ioEntity.inputs.Length
                && ioEntity.inputs[inputSlot].connectedTo.Get() != null;
        }

        private static void HideIOSlots(IOSlot[] slots)
        {
            foreach (var slot in slots)
            {
                slot.type = IOType.Generic;
            }
        }

        private static void RemoveProblemComponents(BaseEntity entity)
        {
            foreach (var collider in entity.GetComponentsInChildren<Collider>())
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            UnityEngine.Object.DestroyImmediate(entity.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(entity.GetComponent<GroundWatch>());
        }

        private bool ShouldIgnoreEntity(IOEntity ioEntity)
        {
            // Parented entities are assumed to be controlled by other plugins that can manage power themselves, except
            // entities that are normally parented in vanilla.
            if (ioEntity.HasParent() && !IsEntityNormallyParented(ioEntity))
                return true;

            // Ignore turrets and sam sites with switches controlled by other plugins.
            if (ioEntity is AutoTurret or SamSite
                && GetChildEntity<ElectricSwitch>(ioEntity) is {} electricSwitch
                && !_attachedSwitches.Contains(electricSwitch))
                return true;

            return false;
        }

        private bool TryProvidePower(IOEntity ioEntity, int inputSlot, int powerAmount)
        {
            if (ioEntity.inputs.Length <= inputSlot || InputUpdateWasBlocked(ioEntity, inputSlot, powerAmount))
                return false;

            ioEntity.UpdateFromInput(powerAmount, inputSlot);

            if (powerAmount >= 0)
            {
                _modifiedEntities.Add(ioEntity);
            }
            else
            {
                _modifiedEntities.Remove(ioEntity);
            }

            return true;
        }

        private void ResetEntityPower(IOEntity ioEntity)
        {
            var entityConfig = GetEntityConfig(ioEntity);
            if (entityConfig == null)
                return;

            foreach (var inputSlot in entityConfig.InputSlots)
            {
                if (HasConnectedInput(ioEntity, inputSlot))
                    continue;

                // Reset power to 0 for in-scope input slots that have no connected input.
                TryProvidePower(ioEntity, inputSlot, 0);
            }
        }

        private void ForgetDestroyedEntities()
        {
            HashSet<IOEntity> entitiesToForget = null;

            foreach (var modifiedEntity in _modifiedEntities)
            {
                if (modifiedEntity != null)
                    continue;

                entitiesToForget ??= new HashSet<IOEntity>();
                entitiesToForget.Add(modifiedEntity);
            }

            foreach (var switchEntity in _attachedSwitches)
            {
                if (switchEntity != null)
                    continue;

                entitiesToForget ??= new HashSet<IOEntity>();
                entitiesToForget.Add(switchEntity);
            }

            if (entitiesToForget != null)
            {
                foreach (var entity in entitiesToForget)
                {
                    _modifiedEntities.Remove(entity);

                    if (entity is ElectricSwitch electricSwitch)
                    {
                        _attachedSwitches.Remove(electricSwitch);
                    }
                }
            }
        }

        private void ProvideSwitchableEntityPower(IOEntity ioEntity, EntityConfig entityConfig, Vector3 switchPosition, Quaternion switchRotation, int inputSlot)
        {
            var powerAmount = entityConfig.GetPowerForSlot(inputSlot);
            if (powerAmount <= 0)
                return;

            var electricSwitch = GetChildEntity<ElectricSwitch>(ioEntity);
            if (electricSwitch is not null && !_attachedSwitches.Contains(electricSwitch))
                return;

            if (HasConnectedInput(ioEntity, inputSlot))
            {
                if (electricSwitch != null && !electricSwitch.IsDestroyed)
                {
                    electricSwitch.Kill();
                }

                return;
            }

            // When we already have an attached switch, we rely on the OnSwitchToggled hook to manage power.
            if (electricSwitch != null)
                return;

            electricSwitch = AttachSwitchEntity(ioEntity, switchPosition, switchRotation);

            if (_data.WasEntitySwitchedOn(ioEntity))
            {
                // Restore the previous switch state since the plugin was reloaded.
                electricSwitch.SetSwitch(true);
                TryProvidePower(ioEntity, inputSlot, powerAmount);
            }
            else if (ioEntity.IsPowered())
            {
                // Update the switch state if another plugin automatically powered the entity (such as Turret Loadouts).
                electricSwitch.SetSwitch(true);
            }
        }

        private void MaybeProvidePower(IOEntity ioEntity, EntityConfig entityConfig)
        {
            if (ShouldIgnoreEntity(ioEntity))
                return;

            if (_config.AddSwitchToPowerlessAutoTurrets && ioEntity is AutoTurret turret)
            {
                ProvideSwitchableEntityPower(turret, entityConfig, TurretSwitchPosition, TurretSwitchRotation, AutoTurretInputSlot);
                return;
            }

            if (_config.AddSwitchToPowerlessSamSites && ioEntity is SamSite samSite)
            {
                ProvideSwitchableEntityPower(samSite, entityConfig, SamSiteSwitchPosition, SamSiteSwitchRotation, SamSiteInputSlot);
                return;
            }

            foreach (var inputSlot in entityConfig.InputSlots)
            {
                var powerAmount = entityConfig.GetPowerForSlot(inputSlot);

                // Don't update power if specified to be 0 to avoid conflicts with other plugins
                if (powerAmount > 0 && !HasConnectedInput(ioEntity, inputSlot))
                {
                    TryProvidePower(ioEntity, inputSlot, powerAmount);
                }
            }
        }

        private void ProcessIOEntity(IOEntity ioEntity, bool delay)
        {
            if (ioEntity == null)
                return;

            var entityConfig = GetEntityConfig(ioEntity);
            if (entityConfig is not { Enabled: true })
                return;

            if (!EntityOwnerHasPermission(ioEntity, entityConfig))
                return;

            if (delay)
            {
                var ioEntity2 = ioEntity;
                var entityConfig2 = entityConfig;

                NextTick(() =>
                {
                    if (ioEntity2 == null)
                        return;

                    MaybeProvidePower(ioEntity2, entityConfig2);
                });
            }
            else
            {
                MaybeProvidePower(ioEntity, entityConfig);
            }
        }

        private bool EntityOwnerHasPermission(IOEntity ioEntity, EntityConfig entityConfig)
        {
            if (!entityConfig.RequirePermission)
                return true;

            var ownerEntity = GetOwnerEntity(ioEntity);
            if (ownerEntity.OwnerID == 0)
                return false;

            var ownerIdString = ownerEntity.OwnerID.ToString();
            return permission.UserHasPermission(ownerIdString, PermissionAll)
                || permission.UserHasPermission(ownerIdString, entityConfig.PermissionName);
        }

        private ElectricSwitch AttachSwitchEntity(IOEntity parentIoEntity, Vector3 position, Quaternion rotation)
        {
            var electricSwitch = GameManager.server.CreateEntity(ElectricSwitchPrefab, position, rotation) as ElectricSwitch;
            if (electricSwitch == null)
                return null;

            HideIOSlots(electricSwitch.inputs);
            HideIOSlots(electricSwitch.outputs);
            RemoveProblemComponents(electricSwitch);
            electricSwitch.SetFlag(Flag_HasPower, true);
            electricSwitch.baseProtection = _immortalProtection;
            electricSwitch.pickup.enabled = false;
            electricSwitch.EnableSaving(false);
            electricSwitch.SetParent(parentIoEntity);
            electricSwitch.Spawn();
            _attachedSwitches.Add(electricSwitch);
            return electricSwitch;
        }

        #endregion

        #region Dynamic Hook Subscriptions

        private class DynamicHookSubscriber<T> : IEnumerable<T>
        {
            private PowerlessElectronics _plugin;
            private HashSet<T> _list = new();
            private string[] _hookNames;

            public DynamicHookSubscriber(PowerlessElectronics plugin, params string[] hookNames)
            {
                _plugin = plugin;
                _hookNames = hookNames;
            }

            public bool Contains(T item)
            {
                return _list.Contains(item);
            }

            public void Add(T item)
            {
                if (_list.Add(item) && _list.Count == 1)
                {
                    SubscribeAll();
                }
            }

            public void Remove(T item)
            {
                if (_list.Remove(item) && _list.Count == 0)
                {
                    UnsubscribeAll();
                }
            }

            public void SubscribeAll()
            {
                foreach (var hookName in _hookNames)
                {
                    _plugin.Subscribe(hookName);
                }
            }

            public void UnsubscribeAll()
            {
                foreach (var hookName in _hookNames)
                {
                    _plugin.Unsubscribe(hookName);
                }
            }

            public IEnumerator<T> GetEnumerator()
            {
                return _list.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        #endregion

        #region Data

        private class StoredData
        {
            [JsonProperty("EntitiesSwitchedOn", DefaultValueHandling = DefaultValueHandling.Ignore)]
            private HashSet<ulong> EntitiesSwitchedOn = new();

            public static StoredData Load()
            {
                return Interface.Oxide.DataFileSystem.ReadObject<StoredData>(nameof(PowerlessElectronics)) ?? new StoredData();
            }

            public static StoredData Reset()
            {
                return new StoredData().Save();
            }

            private StoredData Save()
            {
                Interface.Oxide.DataFileSystem.WriteObject(nameof(PowerlessElectronics), this);
                return this;
            }

            public bool WasEntitySwitchedOn(IOEntity ioEntity)
            {
                return EntitiesSwitchedOn.Contains(ioEntity.net.ID.Value);
            }

            public void SaveEntitiesSwitchedOn(IEnumerable<ElectricSwitch> electricSwitches)
            {
                var didChange = false;

                if (EntitiesSwitchedOn.Count > 0)
                {
                    EntitiesSwitchedOn.Clear();
                    didChange = true;
                }

                foreach (var electricSwitch in electricSwitches)
                {
                    if (electricSwitch == null || !electricSwitch.IsOn())
                        continue;

                    var parentIoEntity = electricSwitch.GetParentEntity() as IOEntity;
                    if (parentIoEntity == null)
                        continue;

                    EntitiesSwitchedOn.Add(parentIoEntity.net.ID.Value);
                    didChange = true;
                }

                if (didChange)
                {
                    Save();
                }
            }
        }

        #endregion

        #region Configuration

        private EntityConfig GetEntityConfig(IOEntity ioEntity)
        {
            return _config.Entities.GetValueOrDefault(ioEntity.ShortPrefabName);
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class Configuration : BaseConfiguration
        {
            private const string ElevatorShortPrefabName = "elevator";
            private const string ElevatorIoEntityShortPrefabName = "elevatorioentity";

            private static readonly string[] IgnoredEntities =
            {
                // Has inputs to toggle on/off but does not consume power.
                "small_fuel_generator.deployed",

                // Has audio input only
                "connectedspeaker.deployed",
                "soundlight.deployed",

                // Static entity
                "caboose_xorswitch",

                // Has no power input
                "fogmachine",
                "spookyspeaker",
                "snowmachine",
                "strobelight",
            };

            private static bool HasElectricalInput(IOEntity ioEntity)
            {
                foreach (var input in ioEntity.inputs)
                {
                    if (input.type == IOType.Electric)
                        return true;
                }

                return false;
            }

            [JsonProperty("Add switch to powerless auto turrets")]
            public bool AddSwitchToPowerlessAutoTurrets = false;

            [JsonProperty("Add switch to powerless SAM sites")]
            public bool AddSwitchToPowerlessSamSites = false;

            [JsonProperty("Entities")]
            public Dictionary<string, EntityConfig> Entities = new()
            {
                ["andswitch.entity"] = new EntityConfig
                {
                    InputSlots = new[] { 0, 1 },
                    PowerAmounts = new[] { 0, 0 },
                },

                ["electrical.combiner.deployed"] = new EntityConfig
                {
                    InputSlots = new[] { 0, 1 },
                    PowerAmounts = new[] { 0, 0 },
                },

                // Has no pickup entity.
                ["electrical.modularcarlift.deployed"] = new EntityConfig(),

                [ElevatorShortPrefabName] = new EntityConfig
                {
                    InputSlots = new[] { 2 },
                },

                ["fluidswitch"] = new EntityConfig
                {
                    InputSlots = new[] { 2 },
                },

                ["industrialconveyor.deployed"] = new EntityConfig
                {
                    InputSlots = new[] { 1 },
                },

                ["industrialcrafter.deployed"] = new EntityConfig
                {
                    InputSlots = new[] { 1 },
                },

                ["storageadaptor.deployed"] = new EntityConfig
                {
                    InputSlots = new[] { 1 },
                },

                // Has no pickup entity.
                ["microphonestandio.entity"] = new EntityConfig(),
                ["electricfurnace.io"] = new EntityConfig(),

                ["orswitch.entity"] = new EntityConfig
                {
                    InputSlots = new[] { 0, 1 },
                    PowerAmounts = new[] { 0, 0 },
                },

                ["poweredwaterpurifier.deployed"] = new EntityConfig
                {
                    InputSlots = new[] { 1 },
                },

                ["xorswitch.entity"] = new EntityConfig
                {
                    InputSlots = new[] { 0, 1 },
                    PowerAmounts = new[] { 0, 0 },
                },

                // FlagTogglePhotoFrame signs have SimpleLight child entities which have no pickup target, so there is
                // no easy way to auto-detect them. We don't want to automatically detect all SimpleLight prefabs
                // because it would detect ShutterFrame signs which would be automatically closed when deployed if they
                // were to be given free power.
                ["lightupframe.ioent.large"] = new EntityConfig(),
                ["lightupframe.ioent.medium"] = new EntityConfig(),
                ["lightupframe.ioent.small"] = new EntityConfig(),
                ["lightupframe.ioent.standing"] = new EntityConfig(),
                ["lightupframe.ioent.xl"] = new EntityConfig(),
                ["lightupframe.ioent.xxl"] = new EntityConfig(),
            };

            public List<string> AddMissingPrefabs()
            {
                var addedPrefabs = new List<string>();

                foreach (var prefab in GameManifest.Current.entities)
                {
                    var ioEntity = GameManager.server.FindPrefab(prefab.ToLower())?.GetComponent<IOEntity>();
                    if (ioEntity == null || string.IsNullOrEmpty(ioEntity.ShortPrefabName))
                        continue;

                    if (Entities.TryGetValue(ioEntity.ShortPrefabName, out _))
                        continue;

                    if (!HasElectricalInput(ioEntity)
                        || ioEntity.pickup.itemTarget == null
                        || ioEntity.ShortPrefabName.ToLower().Contains("static")
                        || IgnoredEntities.Contains(ioEntity.ShortPrefabName.ToLower()))
                        continue;

                    addedPrefabs.Add(ioEntity.ShortPrefabName);
                }

                if (addedPrefabs.Count == 0)
                    return null;

                foreach (var shortPrefabName in addedPrefabs)
                {
                    Entities[shortPrefabName] = new EntityConfig();
                }

                SortEntities();

                addedPrefabs.Sort();
                return addedPrefabs;
            }

            public bool MigratePrefabs()
            {
                var didChange = false;

                // Move `elevatorioentity` to `elevator`
                if (Entities.Remove(ElevatorIoEntityShortPrefabName, out var elevatorIOEntityConfig))
                {
                    if (!Entities.TryGetValue(ElevatorShortPrefabName, out var elevatorConfig))
                    {
                        elevatorConfig = new EntityConfig { InputSlots = new[] { 2 } };
                        Entities[ElevatorShortPrefabName] = elevatorConfig;
                    }
                    elevatorConfig.RequirePermission = elevatorIOEntityConfig.RequirePermission;
                    elevatorConfig.PowerAmount = elevatorIOEntityConfig.PowerAmount;
                    didChange = true;
                }

                return didChange;
            }

            public void GeneratePermissionNames()
            {
                foreach (var entry in Entities)
                {
                    // Make the permission name less redundant
                    entry.Value.PermissionName = string.Format(PermissionEntityFormat, entry.Key)
                        .Replace("electric.", string.Empty)
                        .Replace("electrical.", string.Empty)
                        .Replace(".deployed", string.Empty)
                        .Replace("_deployed", string.Empty)
                        .Replace(".entity", string.Empty);

                    // Rename `elevator` to `elevatorioentity` for backwards compatibility
                    if (entry.Value.PermissionName.EndsWith($".{ElevatorShortPrefabName}"))
                    {
                        entry.Value.PermissionName = entry.Value.PermissionName.Replace(ElevatorShortPrefabName, ElevatorIoEntityShortPrefabName);
                    }
                }
            }

            private void SortEntities()
            {
                var shortPrefabNames = Entities.Keys.ToList();
                shortPrefabNames.Sort();

                var newEntities = new Dictionary<string, EntityConfig>();
                foreach (var shortName in shortPrefabNames)
                {
                    newEntities[shortName] = Entities[shortName];
                }

                Entities = newEntities;
            }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class EntityConfig
        {
            private static readonly int[] StandardInputSlot = { 0 };

            [JsonProperty("RequirePermission")]
            public bool DeprecatedRequirePermission { set => RequirePermission = value; }
            [JsonProperty("Require permission")]
            public bool RequirePermission;

            // Hidden from config when it's using the default value
            [JsonProperty("InputSlots")]
            public int[] DeprecatedInputSlots { set => InputSlots = value; }
            [JsonProperty("Input slots")]
            public int[] InputSlots = StandardInputSlot;

            public bool ShouldSerializeInputSlots() =>
                !InputSlots.SequenceEqual(StandardInputSlot);

            // Hidden from config when the plural form is used
            [JsonProperty("PowerAmount")]
            public int DeprecatedPowerAmount { set => PowerAmount = value; }
            [JsonProperty("Generate power amount")]
            public int PowerAmount;

            public bool ShouldSerializePowerAmount() =>
                PowerAmounts == null;

            // Hidden from config when null
            [JsonProperty("PowerAmounts")]
            public int[] DeprecatedPowerAmounts { set => PowerAmounts = value; }
            [JsonProperty("Generate power amounts", DefaultValueHandling = DefaultValueHandling.Ignore)]
            public int[] PowerAmounts;

            [JsonIgnore]
            public string PermissionName;

            public bool Enabled
            {
                get
                {
                    foreach (var slot in InputSlots)
                    {
                        if (GetPowerForSlot(slot) > 0)
                            return true;
                    }

                    return false;
                }
            }

            public int GetPowerForSlot(int slotNumber)
            {
                var index = Array.IndexOf(InputSlots, slotNumber);

                // We can't power an input slot that we don't know about
                if (index == -1)
                    return 0;

                // Allow plural array form to take precedence if present
                if (PowerAmounts == null)
                    return PowerAmount;

                // InputSlots and PowerAmounts are expected to be parallel arrays
                return index < PowerAmounts.Length ? PowerAmounts[index] : 0;
            }
        }

        private Configuration GetDefaultConfig() => new();

        #region Configuration Helpers

        [JsonObject(MemberSerialization.OptIn)]
        private class BaseConfiguration
        {
            public bool UsingDefaults;

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonHelper.Deserialize(ToJson()) as Dictionary<string, object>;
        }

        private static class JsonHelper
        {
            public static object Deserialize(string json) => ToObject(JToken.Parse(json));

            private static object ToObject(JToken token)
            {
                switch (token.Type)
                {
                    case JTokenType.Object:
                        return token.Children<JProperty>()
                                    .ToDictionary(prop => prop.Name,
                                                  prop => ToObject(prop.Value));

                    case JTokenType.Array:
                        return token.Select(ToObject).ToList();

                    default:
                        return ((JValue)token).Value;
                }
            }
        }

        private bool MaybeUpdateConfig(BaseConfiguration config)
        {
            var currentWithDefaults = config.ToDictionary();
            var currentRaw = Config.ToDictionary(x => x.Key, x => x.Value);
            return MaybeUpdateConfigDict(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigDict(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            var changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                if (currentRaw.TryGetValue(key, out var currentRawValue))
                {
                    var currentDictValue = currentRawValue as Dictionary<string, object>;
                    if (currentWithDefaults[key] is Dictionary<string, object> defaultDictValue)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigDict(defaultDictValue, currentDictValue))
                        {
                            changed = true;
                        }
                    }
                }
                else
                {
                    currentRaw[key] = currentWithDefaults[key];
                    changed = true;
                }
            }

            return changed;
        }

        protected override void LoadDefaultConfig() => _config = GetDefaultConfig();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                {
                    throw new JsonException();
                }

                if (MaybeUpdateConfig(_config))
                {
                    LogWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                LogWarning($"Configuration file {Name}.json is invalid; using defaults");
                _config.UsingDefaults = true;
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Log($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #endregion
    }
}
