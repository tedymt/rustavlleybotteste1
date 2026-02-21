using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Conveyor Stacks", "BlackLightning", "1.0.0")]
    [Description("Increases the max stack amount that industrial conveyors can transfer at once.")]
    internal class ConveyorStacks : CovalencePlugin
    {
        #region Fields

        private const string IndustrialConveyorPrefab = "assets/prefabs/deployable/playerioents/industrialconveyor/industrialconveyor.deployed.prefab";

        private Configuration _config;
        private IndustrialConveyor _conveyorTemplate;
        private readonly TrackedCoroutine _trackedCoroutine;
        private Coroutine _activeCoroutine;
        private List<IndustrialConveyor> _reusableConveyorList = new List<IndustrialConveyor>();

        public ConveyorStacks()
        {
            _trackedCoroutine = new TrackedCoroutine(this);
        }

        #endregion

        #region Hooks

        private void Init()
        {
            _config.Init(this);

            Unsubscribe(nameof(OnEntitySpawned));
        }

        private void OnServerInitialized()
        {
            _conveyorTemplate = GameManager.server.FindPrefab(IndustrialConveyorPrefab).GetComponent<IndustrialConveyor>();
            if (_conveyorTemplate == null)
            {
                LogError($"Please alert the developer. Unable to locate prefab: {IndustrialConveyorPrefab}");
            }

            ScheduleRefreshAllConveyors();
            Subscribe(nameof(OnEntitySpawned));
        }

        private void Unload()
        {
            CancelActiveCoroutine();

            if (_conveyorTemplate != null)
            {
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    var conveyor = entity as IndustrialConveyor;
                    if ((object)conveyor == null)
                        continue;

                    var hookResult = ExposedHooks.OnConveyorMaxStackAmountChange(conveyor);
                    if (hookResult is bool && !(bool)hookResult)
                        continue;

                    conveyor.MaxStackSizePerMove = _conveyorTemplate.MaxStackSizePerMove;
                }
            }
        }

        private void OnEntitySpawned(IndustrialConveyor conveyor)
        {
            NextTick(() =>
            {
                if (conveyor == null || conveyor.IsDestroyed)
                    return;

                RefreshConveyor(conveyor);
            });
        }

        private void OnGroupPermissionGranted(string groupName, string perm) => HandlePermissionChange(perm);
        private void OnGroupPermissionRevoked(string groupName, string perm) => HandlePermissionChange(perm);
        private void OnUserPermissionGranted(string userId, string perm) => HandlePermissionChange(perm);
        private void OnUserPermissionRevoked(string userId, string perm) => HandlePermissionChange(perm);
        private void OnUserGroupAdded(string userId, string groupName) => ScheduleRefreshAllConveyors();
        private void OnUserGroupRemoved(string userId, string groupName) => ScheduleRefreshAllConveyors();

        #endregion

        #region Exposed Hooks

        private static class ExposedHooks
        {
            public static object OnConveyorMaxStackAmountChange(IndustrialConveyor conveyor)
            {
                return Interface.CallHook("OnConveyorMaxStackAmountChange", conveyor);
            }
        }

        #endregion

        #region Helpers

        private class TrackedCoroutine : IEnumerator
        {
            private readonly Plugin _plugin;
            private IEnumerator _inner;

            public TrackedCoroutine(Plugin plugin, IEnumerator inner = null)
            {
                _plugin = plugin;
                _inner = inner;
            }

            public object Current => _inner.Current;

            public bool MoveNext()
            {
                bool result;
                _plugin.TrackStart();

                try
                {
                    result = _inner.MoveNext();
                }
                finally
                {
                    _plugin.TrackEnd();
                }

                return result;
            }

            public void Reset()
            {
                throw new NotImplementedException();
            }

            public TrackedCoroutine WithEnumerator(IEnumerator inner)
            {
                _inner = inner;
                return this;
            }
        }

        private void RefreshConveyor(IndustrialConveyor conveyor)
        {
            var hookResult = ExposedHooks.OnConveyorMaxStackAmountChange(conveyor);
            if (hookResult is bool && !(bool)hookResult)
                return;

            conveyor.MaxStackSizePerMove = _config.GetTransferMaxStackAmount(this, conveyor);
        }

        private IEnumerator RefreshConveyorsRoutine()
        {
            _reusableConveyorList.Clear();

            foreach (var networkable in BaseNetworkable.serverEntities)
            {
                var conveyor = networkable as IndustrialConveyor;
                if ((object)conveyor == null)
                    continue;

                _reusableConveyorList.Add(conveyor);
            }

            for (var i = 0; i < _reusableConveyorList.Count; i++)
            {
                if (i % 10 == 0)
                    yield return null;

                var entity = _reusableConveyorList[i];
                if (entity == null || entity.IsDestroyed)
                    continue;

                RefreshConveyor(entity);
            }

            _reusableConveyorList.Clear();
        }

        private void CancelActiveCoroutine()
        {
            if (_activeCoroutine != null)
            {
                ServerMgr.Instance.StopCoroutine(_activeCoroutine);
            }
        }

        private void ScheduleRefreshAllConveyors()
        {
            CancelActiveCoroutine();
            _activeCoroutine = ServerMgr.Instance.StartCoroutine(_trackedCoroutine.WithEnumerator(RefreshConveyorsRoutine()));
        }

        private void HandlePermissionChange(string perm)
        {
            if (!perm.StartsWith(nameof(ConveyorStacks), StringComparison.OrdinalIgnoreCase))
                return;

            ScheduleRefreshAllConveyors();
        }

        #endregion

        #region Configuration

        [JsonObject(MemberSerialization.OptIn)]
        private class Configuration : BaseConfiguration
        {
            private class StackProfile
            {
                public readonly int Amount;
                public readonly string Permission;

                public StackProfile(int amount, string permission)
                {
                    Amount = amount;
                    Permission = permission;
                }
            }

            [JsonProperty("Default transfer max stack amount")]
            private int DefaultAmount = 60;

            [JsonProperty("Transfer max stack amounts requiring permission")]
            private int[] AmountsRequiringPermission =
            {
                100,
                1000,
                10000,
                100000
            };

            private StackProfile[] _stackProfiles = Array.Empty<StackProfile>();

            public void Init(ConveyorStacks plugin)
            {
                Array.Sort(AmountsRequiringPermission);

                var stackProfileList = new List<StackProfile>();

                foreach (var amount in AmountsRequiringPermission)
                {
                    var permission = $"{nameof(ConveyorStacks)}.amount.{amount}".ToLower();
                    var stackProfile = new StackProfile(amount, permission);
                    stackProfileList.Add(stackProfile);
                    plugin.permission.RegisterPermission(permission, plugin);
                }

                stackProfileList.Sort((a, b) => a.Amount.CompareTo(b.Amount));
                _stackProfiles = stackProfileList.ToArray();
            }

            public int GetTransferMaxStackAmount(ConveyorStacks plugin, IndustrialConveyor conveyor)
            {
                if (conveyor.OwnerID == 0 || _stackProfiles.Length == 0)
                    return DefaultAmount;

                var userIdString = conveyor.OwnerID.ToString();

                for (var i = _stackProfiles.Length - 1; i >= 0; i--)
                {
                    var stackProfile = _stackProfiles[i];
                    if (plugin.permission.UserHasPermission(userIdString, stackProfile.Permission))
                        return stackProfile.Amount;
                }

                return DefaultAmount;
            }
        }

        private Configuration GetDefaultConfig() => new Configuration();

        #region Configuration Helpers

        [JsonObject(MemberSerialization.OptIn)]
        private class BaseConfiguration
        {
            private string ToJson() => JsonConvert.SerializeObject(this);

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
            return MaybeUpdateConfigSection(currentWithDefaults, currentRaw);
        }

        private bool MaybeUpdateConfigSection(Dictionary<string, object> currentWithDefaults, Dictionary<string, object> currentRaw)
        {
            bool changed = false;

            foreach (var key in currentWithDefaults.Keys)
            {
                object currentRawValue;
                if (currentRaw.TryGetValue(key, out currentRawValue))
                {
                    var defaultDictValue = currentWithDefaults[key] as Dictionary<string, object>;
                    var currentDictValue = currentRawValue as Dictionary<string, object>;

                    if (defaultDictValue != null)
                    {
                        if (currentDictValue == null)
                        {
                            currentRaw[key] = currentWithDefaults[key];
                            changed = true;
                        }
                        else if (MaybeUpdateConfigSection(defaultDictValue, currentDictValue))
                            changed = true;
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
                    PrintWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                PrintError(e.Message);
                PrintWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Puts($"Configuration changes saved to {Name}.json");
            Config.WriteObject(_config, true);
        }

        #endregion

        #endregion
    }
}
 