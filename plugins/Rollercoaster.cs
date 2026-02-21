using System;
using System.Collections.Generic;
using System.Text;
using Oxide.Core;
using UnityEngine;
using ProtoBuf;

namespace Oxide.Plugins
{
    [Info("Rollercoaster", "Substrata", "1.0.3")]
    [Description("Spawn a sedan rollercoaster on button press")]

    class Rollercoaster : RustPlugin
    {
        #region Fields
        private const uint sedanRailPrefabID = 207357730;

        float monumentSearchRadius = 150f;

        private Vector3 localSedanButtonPosOffset = new Vector3(-0.1191987f, 0.02911444f, -0.1355738f);
        private Quaternion localSedanButtonRotOffset = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f);

        private Vector3 localSedanPosOffset = new Vector3(-0.1052655f, 0.02689573f, -0.1437552f);
        private Quaternion localSedanRotOffset = new Quaternion(5.872136E-05f, 0.7069608f, -5.869712E-05f, 0.7072527f);

        private Dictionary<MonumentInfo, RollercoasterData> rollercoasters = new Dictionary<MonumentInfo, RollercoasterData>();
        
        private class RollercoasterData
        {
            public PressButton sedanButton;
            public TrainEngine sedan;
        }
        #endregion

        #region Oxide Hooks
        void Init()
        {
            Unsubscribe(nameof(OnEntityDismounted));
            Unsubscribe(nameof(OnButtonPress));
        }

        void OnServerInitialized()
        {
            FindRollerCoasters();
            InitializeRollercoasters();
        }

        void OnEntityDismounted(BaseMountable mountable, BasePlayer player)
        {
            if (mountable == null || player == null) return;

            TrainEngine sedan = mountable.VehicleParent() as TrainEngine;
            if (sedan == null || sedan.prefabID != sedanRailPrefabID) return;

            NextTick(() =>
            {
                if (sedan != null && !sedan.AnyMounted())
                {
                    foreach (var rollercoaster in rollercoasters)
                    {
                        if (rollercoaster.Value.sedan == sedan)
                        {
                            rollercoaster.Value.sedan = null;
                            break;
                        }
                    }

                    sedan.DismountAllPlayers();
                    sedan?.Kill();
                }
            });
        }

        void OnButtonPress(PressButton button, BasePlayer player)
        {
            if (button == null || player == null) return;

            foreach (var rollercoaster in rollercoasters)
            {
                if (rollercoaster.Value.sedanButton == button)
                {
                    TrainEngine sedan = rollercoaster.Value.sedan;
                    if (sedan == null || Vector3.Distance(player.transform.position, sedan.transform.position) > 8f)
                    {
                        SpawnSedan(rollercoaster.Key, player);
                    }

                    return;
                }
            }
        }
        #endregion

        #region Main
        void InitializeRollercoasters()
        {
            HashSet<TrainEngine> sedansToRemove = new HashSet<TrainEngine>();

            foreach (BaseNetworkable ent in BaseNetworkable.serverEntities)
            {
                if (ent is PressButton button)
                {
                    foreach (var rollercoaster in rollercoasters)
                    {
                        Vector3 sedanButtonPos = rollercoaster.Key.transform.localToWorldMatrix.MultiplyPoint3x4(localSedanButtonPosOffset);

                        if (Vector3.Distance(button.transform.position, sedanButtonPos) < 2f)
                        {
                            rollercoaster.Value.sedanButton = button;
                            continue;
                        }
                    }
                }
                else if (ent is TrainEngine sedan && sedan.prefabID == sedanRailPrefabID)
                {
                    foreach (var rollercoaster in rollercoasters)
                    {
                        if (Vector3.Distance(sedan.transform.position, rollercoaster.Key.transform.position) < 150f)
                        {
                            if (rollercoaster.Value.sedan == null && sedan.AnyMounted())
                            {
                                rollercoaster.Value.sedan = sedan;
                                continue;
                            }
                            else
                            {
                                sedansToRemove.Add(sedan);
                            }
                        }
                    }
                }
            }

            foreach (TrainEngine sedanToRemove in sedansToRemove)
            {
                sedanToRemove.DismountAllPlayers();
                sedanToRemove?.Kill();
            }

            if (rollercoasters.Count > 0)
            {
                foreach (var rollercoaster in rollercoasters)
                {
                    if (rollercoaster.Value.sedanButton == null)
                    {
                        // Try respawn button, if missing
                        Vector3 sedanButtonPos = rollercoaster.Key.transform.localToWorldMatrix.MultiplyPoint3x4(localSedanButtonPosOffset);
                        Quaternion sedanButtonRot = rollercoaster.Key.transform.rotation * localSedanButtonRotOffset;
                        PressButton sedanButton = GameManager.server.CreateEntity("assets/prefabs/io/electric/switches/pressbutton/pressbutton.prefab", sedanButtonPos, sedanButtonRot) as PressButton;
                        sedanButton?.Spawn();
                        if (sedanButton != null)
                        {
                            rollercoaster.Value.sedanButton = sedanButton;
                        }
                    }
                }

                Subscribe(nameof(OnEntityDismounted));
                Subscribe(nameof(OnButtonPress));

                StringBuilder sb = new StringBuilder();

                sb.AppendLine($"{rollercoasters.Count} {(rollercoasters.Count == 1 ? "rollercoaster" : "rollercoasters")} detected on this map:");

                foreach (var rollercoaster in rollercoasters)
                {
                    sb.AppendLine($"Rollercoaster {rollercoaster.Key.transform.position} | Button: {(rollercoaster.Value.sedanButton != null ? "Connected!" : "Not Found")}");
                }

                PrintWarning(sb.ToString());
            }
            else
            {
                PrintWarning("No rollercoasters detected on this map");
            }
        }

        void SpawnSedan(MonumentInfo monumentInfo, BasePlayer player = null)
        {
            if (monumentInfo == null) return;
            if (!rollercoasters.TryGetValue(monumentInfo, out RollercoasterData rollercoasterData)) return;

            TrainEngine sedan = rollercoasterData.sedan;
            if (sedan != null)
            {
                if (sedan.AnyMounted())
                {
                    SendReply(player, Lang("InUse", player.UserIDString));
                    return;
                }
                else
                {
                    sedan.DismountAllPlayers();
                    sedan?.Kill();
                }
            }

            Vector3 sedanPos = monumentInfo.transform.localToWorldMatrix.MultiplyPoint3x4(localSedanPosOffset);
            Quaternion sedanRot = monumentInfo.transform.rotation * localSedanRotOffset;

            TrainEngine newSedan = GameManager.server.CreateEntity("assets/content/vehicles/sedan_a/sedanrail.entity.prefab", sedanPos, sedanRot) as TrainEngine;
            newSedan?.Spawn();

            if (newSedan != null)
            {
                rollercoasterData.sedan = newSedan;

                AddSedanFuel(newSedan);

                if (player != null)
                {
                    SendReply(player, Lang("Spawned", player.UserIDString, player.displayName));
                    Server.Broadcast(Lang("Spawned_Broadcast", null, player.displayName));
                }
            }
        }

        void AddSedanFuel(TrainEngine sedan)
        {
            if (sedan == null || sedan.prefabID != sedanRailPrefabID) return;

            ItemContainer fuelStorage = sedan.GetComponentInChildren<StorageContainer>()?.inventory;
            if (fuelStorage == null) return;

            sedan.engineController?.FuelSystem?.AddFuel(100);
            fuelStorage.SetLocked(true);
        }
        #endregion

        #region Find Rollercoasters
        uint polePrefabId = 3439528891;
        uint poleWithRopePrefabId = 641083791;
        uint poleWithDanglingRopePrefabId = 3178298419;

        float poleSearchRadius = 34f;

        List<PrefabData> monumentMarkers = new List<PrefabData>();
        List<PrefabData> danglingRopePoles = new List<PrefabData>();
        List<PrefabData> normalPoles = new List<PrefabData>();
        List<PrefabData> ropePoles = new List<PrefabData>();

        void FindRollerCoasters()
        {
            // First pass: catalog all pole positions
            foreach (PrefabData prefabData in World.Serialization.world.prefabs)
            {
                uint prefabId = prefabData.id;

                if (prefabId == poleWithDanglingRopePrefabId)
                {
                    danglingRopePoles.Add(prefabData);
                }
                else if (prefabId == polePrefabId)
                {
                    normalPoles.Add(prefabData);
                }
                else if (prefabId == poleWithRopePrefabId)
                {
                    ropePoles.Add(prefabData);
                }
            }

            // Get all monument spheres
            List<MonumentInfo> availableMonuments = new List<MonumentInfo>();
            foreach (MonumentInfo monumentInfo in TerrainMeta.Path.Monuments)
            {
                if (monumentInfo.name == "assets/bundled/prefabs/modding/volumes_and_triggers/prevent_building_monument_sphere.prefab")
                {
                    availableMonuments.Add(monumentInfo);
                }
            }

            // Second pass: check each dangling rope pole to see if it has the exact configuration
            foreach (var danglingPole in danglingRopePoles)
            {
                Vector3 prefabPosition = new Vector3(danglingPole.position.x, danglingPole.position.y, danglingPole.position.z);

                if (IsValidMonumentLocation(prefabPosition))
                {
                    monumentMarkers.Add(danglingPole);

                    // Find the closest monument sphere to this validated pole
                    MonumentInfo closestMonument = null;
                    float closestDistance = float.MaxValue;

                    foreach (var monument in availableMonuments)
                    {
                        float distance = Vector3.Distance(monument.transform.position, prefabPosition);
                        if (distance <= monumentSearchRadius && distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestMonument = monument;
                        }
                    }

                    if (closestMonument != null)
                    {
                        if (!rollercoasters.TryGetValue(closestMonument, out RollercoasterData rollercoasterData))
                        {
                            rollercoasterData = new RollercoasterData();
                            rollercoasters[closestMonument] = rollercoasterData;
                        }
                        availableMonuments.Remove(closestMonument);
                    }
                }
            }
        }

        bool IsValidMonumentLocation(Vector3 danglingPolePosition)
        {
            int foundNormalPoles = 0;
            int foundRopePoles = 0;

            // Count normal poles within radius
            foreach (var pole in normalPoles)
            {
                Vector3 polePosition = new Vector3(pole.position.x, pole.position.y, pole.position.z);
                if (Vector3.Distance(danglingPolePosition, polePosition) <= poleSearchRadius)
                {
                    foundNormalPoles++;
                }
            }

            // Count rope poles within radius
            foreach (var pole in ropePoles)
            {
                Vector3 polePosition = new Vector3(pole.position.x, pole.position.y, pole.position.z);
                if (Vector3.Distance(danglingPolePosition, polePosition) <= poleSearchRadius)
                {
                    foundRopePoles++;
                }
            }

            // Check for exact match
            if (foundNormalPoles == 5 && foundRopePoles == 214)
            {
                return true;
            }

            return false;
        }
        #endregion

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["InUse"] = "The rollercoaster is currently in use, please wait...",
                ["Spawned"] = "Enjoy your rollercoaster, {0}!",
                ["Spawned_Broadcast"] = "{0} is riding the rollercoaster!"
            }, this);
        }

        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        #endregion
    }
}
