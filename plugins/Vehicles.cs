using Facepunch;
using Oxide.Core;
using Oxide.Core.Plugins;
using Rust;
using System;
using System.Collections.Generic;
using UnityEngine;
//	TODO:
//		Add loadout list for vehicle modules and engine components.
//		Rework fetching checks.
//		Rework train track checks.
//		Re-add extra mounts on server startup.
namespace Oxide.Plugins
{
	[Info("Vehicles", "bsdinis", "0.1.8")]
	class Vehicles : RustPlugin
	{
		void Init()
		{
			try
			{
				config = Config.ReadObject<ConfigData>();
				if (config == null)
				{
					throw new Exception();
				}
				else
				{
					SaveConfig();
				}
			}
			catch
			{
				PrintError("CONFIG FILE IS INVALID!\nCheck config file and reload Vehicles.");
				Interface.Oxide.UnloadPlugin(Name);
				return;
			}

			data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

			Unsubscribe(nameof(OnEntitySpawned));

			if (!config.ClearCooldownsOnMapWipe)
			{
				Unsubscribe(nameof(OnNewSave));
			}
			if (!config.DestroyVehiclesOnDisconnect)
			{
				Unsubscribe(nameof(OnPlayerDisconnected));
			}
			if (!config.PreventVehiclesDecay)
			{
				Unsubscribe(nameof(OnEntityTakeDamage));
			}

			bool changed = false;
			Dictionary<string, VehicleSettings> newVehicleConfig = new Dictionary<string, VehicleSettings>();
			foreach (KeyValuePair<string, VehicleSettings> vehicleConfig in config.Vehicles)
			{
				string oldSuffix = vehicleConfig.Key;
				string newSuffix = oldSuffix.ToLower();
				if (oldSuffix != newSuffix)
				{
					changed = true;
					if (!config.Vehicles.ContainsKey(newSuffix))
					{
						PrintWarning($"Suffix \"{oldSuffix}\" has been renamed to \"{newSuffix}\". Do not use uppercase for vehicle suffixes.");
					}
					else
					{
						PrintWarning($"Suffix \"{oldSuffix}\" has been removed as \"{newSuffix}\" already exists. Do not use uppercase for vehicle suffixes.");
						continue;
					}
				}
				newVehicleConfig.Add(newSuffix, vehicleConfig.Value);
				if (vehicleConfig.Value.SpawnCooldown != null)
				{
					foreach (KeyValuePair<string, double?> perm in vehicleConfig.Value.SpawnCooldown)
					{
						string spawnPerm = perm.Key;
						if (!string.IsNullOrWhiteSpace(spawnPerm) && !permission.PermissionExists(spawnPerm))
						{
							permission.RegisterPermission(spawnPerm, this);
						}
					}
				}
				if (vehicleConfig.Value.FetchCooldown != null)
				{
					foreach (KeyValuePair<string, double?> perm in vehicleConfig.Value.FetchCooldown)
					{
						string fetchPerm = perm.Key;
						if (!string.IsNullOrWhiteSpace(fetchPerm) && !permission.PermissionExists(fetchPerm))
						{
							permission.RegisterPermission(fetchPerm, this);
						}
					}
				}
				if (vehicleConfig.Value.ExtraMounts != null)
				{
					foreach (KeyValuePair<string, List<PositionRotation>> perm in vehicleConfig.Value.ExtraMounts)
					{
						string mountsPerm = perm.Key;
						if (!string.IsNullOrWhiteSpace(mountsPerm) && !permission.PermissionExists(mountsPerm))
						{
							permission.RegisterPermission(mountsPerm, this);
						}
					}
				}
				if (vehicleConfig.Value.ExtraSeats != null)
				{
					foreach (KeyValuePair<string, List<PositionRotation>> perm in vehicleConfig.Value.ExtraSeats)
					{
						string seatsPerm = perm.Key;
						if (!string.IsNullOrWhiteSpace(seatsPerm) && !permission.PermissionExists(seatsPerm))
						{
							permission.RegisterPermission(seatsPerm, this);
						}
					}
				}
				cmd.AddChatCommand(config.SpawnCommandPrefix+newSuffix, this, nameof(ChatSpawn));
				cmd.AddChatCommand(config.FetchCommandPrefix+newSuffix, this, nameof(ChatFetch));
				cmd.AddChatCommand(config.DespawnCommandPrefix+newSuffix, this, nameof(ChatDespawn));
			}
			if (changed)
			{
				config.Vehicles = newVehicleConfig;
			}
			if (config.Version != Version)
			{
				changed = true;
				config.Version = Version;
			}
			if (changed)
			{
				SaveConfig();
			}
		}

		void OnServerInitialized()
		{
			string warning = "\n";
			bool changed = false;
			Dictionary<ulong, Dictionary<string, List<ulong>>> newVehicleData = new Dictionary<ulong, Dictionary<string, List<ulong>>>();
			foreach (KeyValuePair<ulong, Dictionary<string, List<ulong>>> playerVehicles in data.vehicles)
			{
				ulong playerID = playerVehicles.Key;
				Dictionary<string, List<ulong>> newPlayerVehicles = new Dictionary<string, List<ulong>>();
				foreach (KeyValuePair<string, List<ulong>> vehicleList in playerVehicles.Value)
				{
					string suffix = vehicleList.Key;
					if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
					{
						string mismatch = $"The suffix \"{suffix}\" does not exist in the config file.";
						if (!warning.Contains(mismatch))
						{
							warning += (mismatch + "\n");
						}
					}
					List<ulong> newVehicleList = new List<ulong>();
					for (int i = 0; i < vehicleList.Value.Count; i++)
					{
						ulong entityID = vehicleList.Value[i];
						BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entityID)) as BaseEntity;
						if (entity == null)
						{
							changed = true;
							continue;
						}
						newVehicleList.Add(entityID);
						RemoveMapMarker(entity);
						if (vehicleSettings == null)
						{
							continue;
						}
						SetFuelConsumption(entity);
						float? idleFuelPerSec = GetFloat(playerID.ToString(), vehicleSettings.IdleFuelPerSecond);
						float? maxFuelPerSec = GetFloat(playerID.ToString(), vehicleSettings.MaxFuelPerSecond);
						if (idleFuelPerSec == null || maxFuelPerSec == null)
						{
							continue;
						}
						ModularCar car = entity as ModularCar;
						if (car == null)
						{
							continue;
						}
						for (int ii = 0; ii < car.AttachedModuleEntities.Count; ii++)
						{
							VehicleModuleEngine moduleEngine = car.AttachedModuleEntities[ii] as VehicleModuleEngine;
							if (moduleEngine != null)
							{
								SetModuleEngineFuelConsumption(moduleEngine);
							}
						}
					}
					if (newVehicleList.Count > 1)
					{
						newPlayerVehicles.Add(vehicleList.Key, newVehicleList);
					}
				}
				if (newPlayerVehicles.Count > 1)
				{
					newVehicleData.Add(playerID, newPlayerVehicles);
				}
			}
			if (!string.IsNullOrWhiteSpace(warning))
			{
				warning += "Do not rename suffixes manually as there will be no way for players to fetch or despawn their old suffixes.\nUndo the renaming, reload Vehicles and then use the console command 'vehicles.renamesuffix'.";
				PrintWarning(warning);
			}
			if (changed)
			{
				data.vehicles = newVehicleData;
			}

			Subscribe(nameof(OnEntitySpawned));
		}

		protected override void LoadDefaultConfig()
		{
			config = new ConfigData()
			{
				SpawnCommandPrefix = "my",
				FetchCommandPrefix = "g",
				DespawnCommandPrefix = "no",
				AllowMultipleIdentical = false,
				FetchOldVehicleInsteadOfSpawningIdentical = true,
				AllowFetchingWhenOccupied = false,
				DismountOccupantsWhenFetching = true,
				AllowDespawningWhenOccupied = false,
				RefundFuelOnDespawn = false,
				NotifyWhenVehicleDestroyed = false,
				DestroyVehiclesOnDisconnect = false,
				PreventVehiclesDecay = false,
				ClearCooldownsOnMapWipe = true,
				BlockWhenMountedOrParented = true,
				BlockWhenBuildingBlocked = true,
				BlockInSafeZone = true,
				BlockWhenCombatBlocked = true,
				BlockWhenRaidBlocked = true,
				RemoveChinookMapMarker = true,
				Vehicles = new Dictionary<string, VehicleSettings>()
				{
					{ "ball", new VehicleSettings { Name = "Soccer Ball", Prefab = "assets/content/vehicles/ball/ball.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "", 86400.0 }, { "vehicles.ball", 3600.0 }, { "vehicles.ball.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "", 1800.0 }, { "vehicles.ball", 60.0 }, { "vehicles.ball.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.ball", 20.0f }, { "vehicles.ball.VIP", 50.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = 0, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "attack", new VehicleSettings { Name = "Attack Helicopter", Prefab = "assets/content/vehicles/attackhelicopter/attackhelicopter.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.attack", 3600.0 }, { "vehicles.attack.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.attack", 60.0 }, { "vehicles.attack.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.attack", 5.0f }, { "vehicles.attack.VIP", 15.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.attack", 50.0f }, { "vehicles.attack.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.attack", 50.0f }, { "vehicles.attack.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.attack", 0 }, { "vehicles.attack.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.attack", false }, { "vehicles.attack.VIP", true } }, FuelPerSecond = new Dictionary<string, float?>() { { "vehicles.attack", 0.5f }, { "vehicles.attack.VIP", 0.0f } }, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "mini", new VehicleSettings { Name = "Minicopter", Prefab = "assets/content/vehicles/minicopter/minicopter.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.minicopter", 3600.0 }, { "vehicles.minicopter.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.minicopter", 60.0 }, { "vehicles.minicopter.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.minicopter", 5.0f }, { "vehicles.minicopter.VIP", 15.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.minicopter", 50.0f }, { "vehicles.minicopter.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.minicopter", 50.0f }, { "vehicles.minicopter.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.minicopter", 0 }, { "vehicles.minicopter.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.minicopter", false }, { "vehicles.minicopter.VIP", true } }, FuelPerSecond = new Dictionary<string, float?>() { { "vehicles.minicopter", 0.5f }, { "vehicles.minicopter.VIP", 0.0f } }, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = new Dictionary<string, List<PositionRotation>>() { { "vehicles.minicopter1", new List<PositionRotation>() { new PositionRotation(0.0f, 0.35f, -1.45f, 0f, 180f, 0.0f) } }, { "vehicles.minicopter2", new List<PositionRotation>() { new PositionRotation(0.6f, 0.2f, -0.2f, 0.0f, 0.0f, 0.0f), new PositionRotation(-0.6f, 0.2f, -0.2f, 0.0f, 0.0f, 0.0f) } }, { "vehicles.minicopter3", new List<PositionRotation>() { new PositionRotation(0.0f, 0.35f, -1.45f, 0.0f, 180.0f, 0.0f), new PositionRotation(0.6f, 0.2f, -0.2f, 0.0f, 0.0f, 0.0f), new PositionRotation(-0.6f, 0.2f, -0.2f, 0.0f, 0.0f, 0.0f) } }, }, ExtraSeats = new Dictionary<string, List<PositionRotation>>() { { "vehicles.minicopter1", new List<PositionRotation>() { new PositionRotation(0.0f, 0.4f, -1.1f, 0f, 180f, 0.0f) } }, { "vehicles.minicopter2", new List<PositionRotation>() { new PositionRotation(0.6f, 0.2f, -0.5f, 0.0f, 0.0f, 0.0f), new PositionRotation(-0.6f, 0.2f, -0.5f, 0.0f, 0.0f, 0.0f) } }, { "vehicles.minicopter3", new List<PositionRotation>() { new PositionRotation(0.0f, 0.4f, -1.1f, 0.0f, 180.0f, 0.0f), new PositionRotation(0.6f, 0.2f, -0.5f, 0.0f, 0.0f, 0.0f), new PositionRotation(-0.6f, 0.2f, -0.5f, 0.0f, 0.0f, 0.0f) } }, }, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "heli", new VehicleSettings { Name = "Scrap Transport Helicopter", Prefab = "assets/content/vehicles/scrap heli carrier/scraptransporthelicopter.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.scraphelicopter", 3600.0 }, { "vehicles.scraphelicopter.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.scraphelicopter", 60.0 }, { "vehicles.scraphelicopter.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.scraphelicopter", 5.0f }, { "vehicles.scraphelicopter.VIP", 15.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.scraphelicopter", 50.0f }, { "vehicles.scraphelicopter.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.scraphelicopter", 50.0f }, { "vehicles.scraphelicopter.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.scraphelicopter", 0 }, { "vehicles.scraphelicopter.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.scraphelicopter", false }, { "vehicles.scraphelicopter.VIP", true } }, FuelPerSecond = new Dictionary<string, float?>() { { "vehicles.scraphelicopter", 0.5f }, { "vehicles.scraphelicopter.VIP", 0.0f } }, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = new Dictionary<string, List<PositionRotation>>() { { "vehicles.scraphelicopter2", new List<PositionRotation>() { new PositionRotation(-1.235f, 1.0f, -2.75f, 0.0f, 180.0f, 0.0f), new PositionRotation(1.2f, 1.0f, -2.75f, 0.0f, 180.0f, 0.0f) } } }, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "boat", new VehicleSettings { Name = "Row Boat", Prefab = "assets/content/vehicles/boats/rowboat/rowboat.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.rowboat", 3600.0 }, { "vehicles.rowboat.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.rowboat", 60.0 }, { "vehicles.rowboat.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.rowboat", 3.0f }, { "vehicles.rowboat.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.rowboat", 50.0f }, { "vehicles.rowboat.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.rowboat", 50.0f }, { "vehicles.rowboat.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.rowboat", 0 }, { "vehicles.rowboat.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.rowboat", false }, { "vehicles.rowboat.VIP", true } }, FuelPerSecond = new Dictionary<string, float?>() { { "vehicles.rowboat", 0.1f }, { "vehicles.rowboat.VIP", 0.0f } }, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = true, CanNotSpawnOnWater = false } },
					{ "rhib", new VehicleSettings { Name = "RHIB", Prefab = "assets/content/vehicles/boats/rhib/rhib.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.rhib", 3600.0 }, { "vehicles.rhib.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.rhib", 60.0 }, { "vehicles.rhib.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.rhib", 5.0f }, { "vehicles.rhib.VIP", 15.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.rhib", 50.0f }, { "vehicles.rhib.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.rhib", 50.0f }, { "vehicles.rhib.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.rhib", 0 }, { "vehicles.rhib.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.rhib", false }, { "vehicles.rhib.VIP", true } }, FuelPerSecond = new Dictionary<string, float?>() { { "vehicles.rhib", 0.25f }, { "vehicles.rhib.VIP", 0.0f } }, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = true, CanNotSpawnOnWater = false } },
					{ "kayak", new VehicleSettings { Name = "Kayak", Prefab = "assets/content/vehicles/boats/kayak/kayak.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.kayak", 3600.0 }, { "vehicles.kayak.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.kayak", 60.0 }, { "vehicles.kayak.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.kayak", 3.0f }, { "vehicles.kayak.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.kayak", 50.0f }, { "vehicles.kayak.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.kayak", 50.0f }, { "vehicles.kayak.VIP", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = true, CanNotSpawnOnWater = false } },
					{ "sub1", new VehicleSettings { Name = "Solo Submarine", Prefab = "assets/content/vehicles/submarine/submarinesolo.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.submarinesolo", 3600.0 }, { "vehicles.submarinesolo.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.submarinesolo", 60.0 }, { "vehicles.submarinesolo.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.submarinesolo", 3.0f }, { "vehicles.submarinesolo.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.submarinesolo", 50.0f }, { "vehicles.submarinesolo.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.submarinesolo", 50.0f }, { "vehicles.submarinesolo.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.submarinesolo", 0 }, { "vehicles.submarinesolo.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.submarinesolo", false }, { "vehicles.submarinesolo.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.submarinesolo", 0.025f }, { "vehicles.submarinesolo.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.submarinesolo", 0.13f }, { "vehicles.submarinesolo.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = true, CanNotSpawnOnWater = false } },
					{ "sub2", new VehicleSettings { Name = "Duo Submarine", Prefab = "assets/content/vehicles/submarine/submarineduo.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.submarineduo", 3600.0 }, { "vehicles.submarineduo.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.submarineduo", 60.0 }, { "vehicles.submarineduo.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.submarineduo", 3.0f }, { "vehicles.submarineduo.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.submarineduo", 50.0f }, { "vehicles.submarineduo.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.submarineduo", 50.0f }, { "vehicles.submarineduo.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.submarineduo", 0 }, { "vehicles.submarineduo.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.submarineduo", false }, { "vehicles.submarineduo.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.submarineduo", 0.03f }, { "vehicles.submarineduo.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.submarineduo", 0.15f }, { "vehicles.submarineduo.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = true, CanNotSpawnOnWater = false } },
					{ "tug", new VehicleSettings { Name = "Tugboat", Prefab = "assets/content/vehicles/boats/tugboat/tugboat.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.tugboat", 3600.0 }, { "vehicles.tugboat.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.tugboat", 60.0 }, { "vehicles.tugboat.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.tugboat", 10.0f }, { "vehicles.tugboat.VIP", 25.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.tugboat", 50.0f }, { "vehicles.tugboat.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.tugboat", 50.0f }, { "vehicles.tugboat.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.tugboat", 0 }, { "vehicles.tugboat.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.tugboat", false }, { "vehicles.tugboat.VIP", true } }, FuelPerSecond = new Dictionary<string, float?>() { { "vehicles.tugboat", 0.33f }, { "vehicles.tugboat.VIP", 0.0f } }, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = true, CanNotSpawnOnWater = false } },
					{ "hab", new VehicleSettings { Name = "Hot Air Balloon", Prefab = "assets/prefabs/deployable/hot air balloon/hotairballoon.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.hotairballoon", 3600.0 }, { "vehicles.hotairballoon.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.hotairballoon", 60.0 }, { "vehicles.hotairballoon.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.hotairballoon", 3.0f }, { "vehicles.hotairballoon.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.hotairballoon", 50.0f }, { "vehicles.hotairballoon.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.hotairballoon", 50.0f }, { "vehicles.hotairballoon.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.hotairballoon", 0 }, { "vehicles.hotairballoon.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.hotairballoon", false }, { "vehicles.hotairballoon.VIP", true } }, FuelPerSecond = new Dictionary<string, float?>() { { "vehicles.hotairballoon", 0.25f }, { "vehicles.hotairballoon.VIP", 0.0f } }, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = 180, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "horse", new VehicleSettings { Name = "Ridable Horse", Prefab = "assets/rust.ai/nextai/testridablehorse.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.ridablehorse", 3600.0 }, { "vehicles.ridablehorse.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.ridablehorse", 60.0 }, { "vehicles.ridablehorse.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.ridablehorse", 3.0f }, { "vehicles.ridablehorse.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.ridablehorse", 50.0f }, { "vehicles.ridablehorse.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.ridablehorse", 50.0f }, { "vehicles.ridablehorse.VIP", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = true } },
					{ "sled", new VehicleSettings { Name = "Sled", Prefab = "assets/prefabs/misc/xmas/sled/sled.deployed.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.sled", 3600.0 }, { "vehicles.sled.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.sled", 60.0 }, { "vehicles.sled.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.sled", 3.0f }, { "vehicles.sled.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.sled", 50.0f }, { "vehicles.sled.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.sled", 50.0f }, { "vehicles.sled.VIP", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "snow", new VehicleSettings { Name = "Snowmobile", Prefab = "assets/content/vehicles/snowmobiles/snowmobile.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.snowmobile", 3600.0 }, { "vehicles.snowmobile.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.snowmobile", 60.0 }, { "vehicles.snowmobile.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.snowmobile", 3.0f }, { "vehicles.snowmobile.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.snowmobile", 50.0f }, { "vehicles.snowmobile.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.snowmobile", 50.0f }, { "vehicles.snowmobile.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.snowmobile", 0 }, { "vehicles.snowmobile.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.snowmobile", false }, { "vehicles.snowmobile.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.snowmobile", 0.03f }, { "vehicles.snowmobile.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.snowmobile", 0.15f }, { "vehicles.snowmobile.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "tomaha", new VehicleSettings { Name = "Tomaha Snowmobile", Prefab = "assets/content/vehicles/snowmobiles/tomahasnowmobile.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.tomaha", 3600.0 }, { "vehicles.tomaha.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.tomaha", 60.0 }, { "vehicles.tomaha.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.tomaha", 3.0f }, { "vehicles.tomaha.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.tomaha", 50.0f }, { "vehicles.tomaha.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.tomaha", 50.0f }, { "vehicles.tomaha.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.tomaha", 0 }, { "vehicles.tomaha.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.tomaha", false }, { "vehicles.tomaha.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.tomaha", 0.03f }, { "vehicles.tomaha.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.tomaha", 0.15f }, { "vehicles.tomaha.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "motorbike", new VehicleSettings { Name = "Motorbike", Prefab = "assets/content/vehicles/bikes/motorbike.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.motorbike", 3600.0 }, { "vehicles.motorbike.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.motorbike", 60.0 }, { "vehicles.motorbike.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.motorbike", 3.0f }, { "vehicles.motorbike.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.motorbike", 50.0f }, { "vehicles.motorbike.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.motorbike", 50.0f }, { "vehicles.motorbike.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.motorbike", 0 }, { "vehicles.motorbike.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.motorbike", false }, { "vehicles.motorbike.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.motorbike", 0.03f }, { "vehicles.motorbike.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.motorbike", 0.15f }, { "vehicles.motorbike.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "motorbike2", new VehicleSettings { Name = "Motorbike Sidecar", Prefab = "assets/content/vehicles/bikes/motorbike_sidecar.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.motorbike2", 3600.0 }, { "vehicles.motorbike2.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.motorbike2", 60.0 }, { "vehicles.motorbike2.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.motorbike2", 3.0f }, { "vehicles.motorbike2.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.motorbike2", 50.0f }, { "vehicles.motorbike2.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.motorbike2", 50.0f }, { "vehicles.motorbike2.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.motorbike2", 0 }, { "vehicles.motorbike2.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.motorbike2", false }, { "vehicles.motorbike2.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.motorbike2", 0.03f }, { "vehicles.motorbike2.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.motorbike2", 0.15f }, { "vehicles.motorbike2.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "bike", new VehicleSettings { Name = "Pedal Bike", Prefab = "assets/content/vehicles/bikes/pedalbike.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.bike", 3600.0 }, { "vehicles.bike.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.bike", 60.0 }, { "vehicles.bike.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.bike", 3.0f }, { "vehicles.bike.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.bike", 50.0f }, { "vehicles.bike.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.bike", 50.0f }, { "vehicles.bike.VIP", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "trike", new VehicleSettings { Name = "Pedal Trike", Prefab = "assets/content/vehicles/bikes/pedaltrike.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.trike", 3600.0 }, { "vehicles.trike.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.trike", 60.0 }, { "vehicles.trike.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.trike", 3.0f }, { "vehicles.trike.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.trike", 50.0f }, { "vehicles.trike.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.trike", 50.0f }, { "vehicles.trike.VIP", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "ch47", new VehicleSettings { Name = "Chinook", Prefab = "assets/prefabs/npc/ch47/ch47.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.chinook", 3600.0 }, { "vehicles.chinook.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.chinook", 60.0 }, { "vehicles.chinook.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.chinook", 3.0f }, { "vehicles.chinook.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.chinook", 50.0f }, { "vehicles.chinook.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.chinook", 50.0f }, { "vehicles.chinook.VIP", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "sedan", new VehicleSettings { Name = "Sedan", Prefab = "assets/content/vehicles/sedan_a/sedantest.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.sedan", 3600.0 }, { "vehicles.sedan.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.sedan", 60.0 }, { "vehicles.sedan.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.sedan", 3.0f }, { "vehicles.sedan.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.sedan", 50.0f }, { "vehicles.sedan.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.sedan", 50.0f }, { "vehicles.sedan.VIP", 0.0f } }, StartingFuel = null, LockFuelContainer = null, FuelPerSecond = null, IdleFuelPerSecond = null, MaxFuelPerSecond = null, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "4mod", new VehicleSettings { Name = "4 Module Car", Prefab = "assets/content/vehicles/modularcar/4module_car_spawned.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.4modulecar", 3600.0 }, { "vehicles.4modulecar.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.4modulecar", 60.0 }, { "vehicles.4modulecar.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.4modulecar", 3.0f }, { "vehicles.4modulecar.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.4modulecar", 50.0f }, { "vehicles.4modulecar.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.4modulecar", 50.0f }, { "vehicles.4modulecar.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.4modulecar", 0 }, { "vehicles.4modulecar.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.4modulecar", false }, { "vehicles.4modulecar.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.4modulecar", 0.025f }, { "vehicles.4modulecar.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.4modulecar", 0.08f }, { "vehicles.4modulecar.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "3mod", new VehicleSettings { Name = "3 Module Car", Prefab = "assets/content/vehicles/modularcar/3module_car_spawned.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.3modulecar", 3600.0 }, { "vehicles.3modulecar.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.3modulecar", 60.0 }, { "vehicles.3modulecar.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.3modulecar", 3.0f }, { "vehicles.3modulecar.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.3modulecar", 50.0f }, { "vehicles.3modulecar.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.3modulecar", 50.0f }, { "vehicles.3modulecar.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.3modulecar", 0 }, { "vehicles.3modulecar.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.3modulecar", false }, { "vehicles.3modulecar.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.3modulecar", 0.025f }, { "vehicles.3modulecar.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.3modulecar", 0.08f }, { "vehicles.3modulecar.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "2mod", new VehicleSettings { Name = "2 Module Car", Prefab = "assets/content/vehicles/modularcar/2module_car_spawned.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.2modulecar", 3600.0 }, { "vehicles.2modulecar.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.2modulecar", 60.0 }, { "vehicles.2modulecar.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.2modulecar", 3.0f }, { "vehicles.2modulecar.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.2modulecar", 50.0f }, { "vehicles.2modulecar.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.2modulecar", 50.0f }, { "vehicles.2modulecar.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.2modulecar", 0 }, { "vehicles.2modulecar.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.2modulecar", false }, { "vehicles.2modulecar.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.2modulecar", 0.025f }, { "vehicles.2modulecar.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.2modulecar", 0.08f }, { "vehicles.2modulecar.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "crane", new VehicleSettings { Name = "Magnet Crane", Prefab = "assets/content/vehicles/crane_magnet/magnetcrane.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.magnetcrane", 3600.0 }, { "vehicles.magnetcrane.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.magnetcrane", 60.0 }, { "vehicles.magnetcrane.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.magnetcrane", 3.0f }, { "vehicles.magnetcrane.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.magnetcrane", 50.0f }, { "vehicles.magnetcrane.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.magnetcrane", 50.0f }, { "vehicles.magnetcrane.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.magnetcrane", 0 }, { "vehicles.magnetcrane.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.magnetcrane", false }, { "vehicles.magnetcrane.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.magnetcrane", 0.06668f }, { "vehicles.magnetcrane.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.magnetcrane", 0.3334f }, { "vehicles.magnetcrane.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = false } },
					{ "cart", new VehicleSettings { Name = "Workcart", Prefab = "assets/content/vehicles/trains/workcart/workcart.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.workcart", 3600.0 }, { "vehicles.workcart.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.workcart", 60.0 }, { "vehicles.workcart.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.workcart", 3.0f }, { "vehicles.workcart.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.workcart", 50.0f }, { "vehicles.workcart.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.workcart", 50.0f }, { "vehicles.workcart.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.workcart", 0 }, { "vehicles.workcart.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.workcart", false }, { "vehicles.workcart.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.workcart", 0.025f }, { "vehicles.workcart.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.workcart", 0.075f }, { "vehicles.workcart.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = true } },
					{ "agcart", new VehicleSettings { Name = "Above Ground Workcart", Prefab = "assets/content/vehicles/trains/workcart/workcart_aboveground.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.abovegroundworkcart", 3600.0 }, { "vehicles.abovegroundworkcart.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.abovegroundworkcart", 60.0 }, { "vehicles.abovegroundworkcart.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart", 3.0f }, { "vehicles.abovegroundworkcart.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart", 50.0f }, { "vehicles.abovegroundworkcart.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart", 50.0f }, { "vehicles.abovegroundworkcart.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.abovegroundworkcart", 0 }, { "vehicles.abovegroundworkcart.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.abovegroundworkcart", false }, { "vehicles.abovegroundworkcart.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart", 0.025f }, { "vehicles.abovegroundworkcart.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart", 0.075f }, { "vehicles.abovegroundworkcart.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = true } },
					{ "agcart2", new VehicleSettings { Name = "Above Ground Workcart 2", Prefab = "assets/content/vehicles/trains/workcart/workcart_aboveground2.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.abovegroundworkcart2", 3600.0 }, { "vehicles.abovegroundworkcart2.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.abovegroundworkcart2", 60.0 }, { "vehicles.abovegroundworkcart2.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart2", 3.0f }, { "vehicles.abovegroundworkcart2.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart2", 50.0f }, { "vehicles.abovegroundworkcart2.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart2", 50.0f }, { "vehicles.abovegroundworkcart2.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.abovegroundworkcart2", 0 }, { "vehicles.abovegroundworkcart2.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.abovegroundworkcart2", false }, { "vehicles.abovegroundworkcart2.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart2", 0.025f }, { "vehicles.abovegroundworkcart2.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.abovegroundworkcart2", 0.075f }, { "vehicles.abovegroundworkcart2.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = true } },
					{ "locomotive", new VehicleSettings { Name = "Locomotive", Prefab = "assets/content/vehicles/trains/locomotive/locomotive.entity.prefab", SpawnCooldown = new Dictionary<string, double?>() { { "vehicles.locomotive", 3600.0 }, { "vehicles.locomotive.VIP", 300.0 } }, FetchCooldown = new Dictionary<string, double?>() { { "vehicles.locomotive", 60.0 }, { "vehicles.locomotive.VIP", 5.0 } }, MaxSpawnDistance = new Dictionary<string, float?>() { { "vehicles.locomotive", 3.0f }, { "vehicles.locomotive.VIP", 10.0f } }, FetchDistanceLimit = new Dictionary<string, float?>() { { "vehicles.locomotive", 50.0f }, { "vehicles.locomotive.VIP", 0.0f } }, DespawnDistanceLimit = new Dictionary<string, float?>() { { "vehicles.locomotive", 50.0f }, { "vehicles.locomotive.VIP", 0.0f } }, StartingFuel = new Dictionary<string, int?>() { { "vehicles.locomotive", 0 }, { "vehicles.locomotive.VIP", 1 } }, LockFuelContainer = new Dictionary<string, bool?>() { { "vehicles.locomotive", false }, { "vehicles.locomotive.VIP", true } }, FuelPerSecond = null, IdleFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.locomotive", 0.035f }, { "vehicles.locomotive.VIP", 0.0f } }, MaxFuelPerSecond = new Dictionary<string, float?>() { { "vehicles.locomotive", 0.1f }, { "vehicles.locomotive.VIP", 0.0f } }, ExtraMounts = null, ExtraSeats = null, YRotationSpawnOffset = -90, CanOnlySpawnOnWater = false, CanNotSpawnOnWater = true } }
				},
				Version = Version
			};
		}

		protected override void SaveConfig() => Config.WriteObject(config, true);

		ConfigData config;
		class ConfigData
		{
			public string SpawnCommandPrefix;
			public string FetchCommandPrefix;
			public string DespawnCommandPrefix;
			public bool AllowMultipleIdentical;
			public bool FetchOldVehicleInsteadOfSpawningIdentical;
			public bool AllowFetchingWhenOccupied;
			public bool DismountOccupantsWhenFetching;
			public bool AllowDespawningWhenOccupied;
			public bool RefundFuelOnDespawn;
			public bool NotifyWhenVehicleDestroyed;
			public bool DestroyVehiclesOnDisconnect;
			public bool PreventVehiclesDecay;
			public bool ClearCooldownsOnMapWipe;
			public bool BlockWhenMountedOrParented;
			public bool BlockWhenBuildingBlocked;
			public bool BlockInSafeZone;
			public bool BlockWhenCombatBlocked;
			public bool BlockWhenRaidBlocked;
			public bool RemoveChinookMapMarker;
			public Dictionary<string, VehicleSettings> Vehicles;
			public VersionNumber Version;
		}
		class VehicleSettings
		{
			public string Name;
			public string Prefab;
			public Dictionary<string, double?> SpawnCooldown;
			public Dictionary<string, double?> FetchCooldown;
			public Dictionary<string, float?> MaxSpawnDistance;
			public Dictionary<string, float?> FetchDistanceLimit;
			public Dictionary<string, float?> DespawnDistanceLimit;
			public Dictionary<string, int?> StartingFuel;
			public Dictionary<string, bool?> LockFuelContainer;
			public Dictionary<string, float?> FuelPerSecond;
			public Dictionary<string, float?> IdleFuelPerSecond;
			public Dictionary<string, float?> MaxFuelPerSecond;
			public Dictionary<string, List<PositionRotation>> ExtraMounts;
			public Dictionary<string, List<PositionRotation>> ExtraSeats;
			public float YRotationSpawnOffset;
			public bool CanOnlySpawnOnWater;
			public bool CanNotSpawnOnWater;
		}
		class PositionRotation
		{
			public PositionRotation(float px, float py, float pz, float rx, float ry, float rz)
			{
				pX = px;
				pY = py;
				pZ = pz;
				rX = rx;
				rY = ry;
				rZ = rz;
			}
			public float pX;
			public float pY;
			public float pZ;
			public float rX;
			public float rY;
			public float rZ;
		}

		StoredData data;
		class StoredData
		{
			public Dictionary<ulong, Dictionary<string, double>> spawnCooldown = new Dictionary<ulong, Dictionary<string, double>>();
			public Dictionary<ulong, Dictionary<string, double>> fetchCooldown = new Dictionary<ulong, Dictionary<string, double>>();
			public Dictionary<ulong, Dictionary<string, List<ulong>>> vehicles = new Dictionary<ulong, Dictionary<string, List<ulong>>>();
		}

		void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, data);

		void OnNewSave()
		{
			data.spawnCooldown.Clear();
			data.fetchCooldown.Clear();
			SaveData();
		}

		void OnServerSave()
		{
			SaveData();
		}

		void Unload()
		{
			SaveData();
		}

		protected override void LoadDefaultMessages()
		{
			lang.RegisterMessages(
				new Dictionary<string, string>()
				{
					["NoPermissionSpawn"] = "You do not have permission to spawn {0}s.",
					["NoPermissionFetch"] = "You do not have permission to fetch {0}s.",
					["MountedOrParented"] = "You cannot spawn or fetch vehicles while mounted or parented.",
					["BuildingBlocked"] = "You cannot spawn or fetch vehicles while building blocked.",
					["InSafeZone"] = "You cannot spawn or fetch vehicles in a safe zone.",
					["CombatBlocked"] = "You cannot spawn or fetch vehicles while combat blocked.",
					["RaidBlocked"] = "You cannot spawn or fetch vehicles while raid blocked.",
					["NotOnWater"] = "You can only spawn or fetch {0}s on water.",
					["OnWater"] = "You can not spawn or fetch {0}s on water.",
					["TracksNotFound"] = "You can only spawn or fetch {0}s on train tracks.",
					["LookingTooFar"] = "You must be looking at a position closer to you to be able to spawn or fetch {0}s.",
					["Destroyed"] = "Your {0} has been destroyed.",
					["AlreadySpawned"] = "You already own a {0}.\nUse '/{1}' to fetch it or '/{2}' to despawn it.",
					["SpawnCooldown"] = "You must wait {0} before spawning another {1}.",
					["FetchCooldown"] = "You must wait {0} before fetching your {1}.",
					["Spawned"] = "Your {0} has spawned.",
					["NotFound"] = "You do not have a {0}.",
					["TooFarFetch"] = "Your {0} is too far away to be fetched.",
					["TooFarDespawn"] = "Your {0} is too far away to be despawned.",
					["BeingUsedFetch"] = "Cannot fetch your {0} as it is currently being used by another player",
					["BeingUsedDespawn"] = "Cannot despawn your {0} as it is currently being used by another player",
					["Fetched"] = "You have fetched your {0}.",
					["Despawned"] = "You have despawned your {0}.{1}",
					["Refunded"] = "\nRefunded {0} low grade fuel."
				},
				this,
				"en"
			);
		}

		List<Tuple<string, string, string>> GetConfig()
		{
			List<Tuple<string, string, string>> list = new List<Tuple<string, string, string>>();
			foreach (KeyValuePair<string, VehicleSettings> vehicleConfig in config.Vehicles)
			{
				list.Add(new Tuple<string, string, string>(vehicleConfig.Key, vehicleConfig.Value.Name, vehicleConfig.Value.Prefab));
			}
			return list;
		}

		List<ulong> GetEntities(ulong playerID, string suffix)
		{
			if (data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities) && allEntities.TryGetValue(suffix, out List<ulong> entities))
			{
				return entities;
			}
			return null;
		}

		Dictionary<string, List<ulong>> GetAllEntities(ulong playerID)
		{
			if (data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities))
			{
				return allEntities;
			}
			return null;
		}

		bool IsPlayerEntity(ulong entityID)
		{
			foreach (KeyValuePair<ulong, Dictionary<string, List<ulong>>> playerVehicles in data.vehicles)
			{
				foreach (KeyValuePair<string, List<ulong>> vehicleList in playerVehicles.Value)
				{
					if (vehicleList.Value.Contains(entityID))
					{
						return true;
					}
				}
			}
			return false;
		}

		string GetSuffix(ulong entityID)
		{
			foreach (KeyValuePair<ulong, Dictionary<string, List<ulong>>> playerVehicles in data.vehicles)
			{
				foreach (KeyValuePair<string, List<ulong>> vehicleList in playerVehicles.Value)
				{
					for (int i = 0; i < vehicleList.Value.Count; i++)
					{
						if (vehicleList.Value[i] == entityID)
						{
							return vehicleList.Key;
						}
					}
				}
			}
			return null;
		}

		ulong GetOwnerUserID(ulong entityID)
		{
			foreach (KeyValuePair<ulong, Dictionary<string, List<ulong>>> playerVehicles in data.vehicles)
			{
				foreach (KeyValuePair<string, List<ulong>> vehicleList in playerVehicles.Value)
				{
					for (int i = 0; i < vehicleList.Value.Count; i++)
					{
						if (vehicleList.Value[i] == entityID)
						{
							return playerVehicles.Key;
						}
					}
				}
			}
			return 0uL;
		}

		bool DespawnNewestEntity(ulong playerID, string suffix, bool refundFuel = false, bool notify = false)
		{
			if (!data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities) || !allEntities.TryGetValue(suffix, out List<ulong> entities) || entities.Count < 1)
			{
				return false;
			}
			BaseNetworkable entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entities[entities.Count - 1]));
			BasePlayer player = BasePlayer.FindByID(playerID);
			int amount = 0;
			if (refundFuel && player != null)
			{
				BaseVehicle vehicle = entity as BaseVehicle;
				HotAirBalloon hab = entity as HotAirBalloon;
				amount = RefundFuel((vehicle != null ? vehicle.GetFuelSystem() as EntityFuelSystem : hab != null ? hab.fuelSystem as EntityFuelSystem : null), player);
			}
			if (config.NotifyWhenVehicleDestroyed || notify)
			{
				RemoveEntity(entity.net.ID.Value, suffix, playerID);
			}
			entity.Kill();
			if (notify && player != null)
			{
				string name = string.Empty;
				if (config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
				{
					name = vehicleSettings.Name;
				}
				player.ChatMessage(string.Format(lang.GetMessage("Despawned", this, player.UserIDString), (!string.IsNullOrEmpty(name) ? name : suffix), (amount > 0 ? string.Format(lang.GetMessage("Refunded", this, player.UserIDString), amount) : string.Empty)));
			}
			return true;
		}

		int DespawnAllEntities(ulong playerID, string suffix = "", bool refundFuel = false, bool notify = false)
		{
			if (!data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities))
			{
				return 0;
			}
			List<BaseNetworkable> destroy = Pool.GetList<BaseNetworkable>();
			if (!string.IsNullOrWhiteSpace(suffix))
			{
				if (!allEntities.TryGetValue(suffix, out List<ulong> entities))
				{
					Pool.FreeList(ref destroy);
					return 0;
				}
				for (int i = 0; i < entities.Count; i++)
				{
					destroy.Add(BaseNetworkable.serverEntities.Find(new NetworkableId(entities[i])));
				}
			}
			else
			{
				foreach (KeyValuePair<string, List<ulong>> vehicleList in allEntities)
				{
					for (int i = 0; i < vehicleList.Value.Count; i++)
					{
						destroy.Add(BaseNetworkable.serverEntities.Find(new NetworkableId(vehicleList.Value[i])));
					}
				}
			}
			int count = destroy.Count;
			BasePlayer player = BasePlayer.FindByID(playerID);
			for (int i = (count - 1); i >= 0; i--)
			{
				int amount = 0;
				BaseNetworkable entity = destroy[i];
				if (refundFuel && player != null)
				{
					BaseVehicle vehicle = entity as BaseVehicle;
					HotAirBalloon hab = entity as HotAirBalloon;
					amount = RefundFuel((vehicle != null ? vehicle.GetFuelSystem() as EntityFuelSystem : hab != null ? hab.fuelSystem as EntityFuelSystem : null), player);
				}
				string entitySuffix = GetSuffix(entity.net.ID.Value);
				if (config.NotifyWhenVehicleDestroyed || notify)
				{
					RemoveEntity(entity.net.ID.Value, entitySuffix, playerID);
				}
				entity.Kill();
				if (notify && player != null)
				{
					string name = string.Empty;
					if (config.Vehicles.TryGetValue(entitySuffix, out VehicleSettings vehicleSettings))
					{
						name = vehicleSettings.Name;
					}
					player.ChatMessage(string.Format(lang.GetMessage("Despawned", this, player.UserIDString), (!string.IsNullOrEmpty(name) ? name : suffix), (amount > 0 ? string.Format(lang.GetMessage("Refunded", this, player.UserIDString), amount) : string.Empty)));
				}
			}
			Pool.FreeList(ref destroy);
			return count;
		}

		bool AddEntity(ulong playerID, string suffix, ulong entityID)
		{
			if (!config.Vehicles.ContainsKey(suffix))
			{
				return false;
			}
			if (data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities))
			{
				if (allEntities.TryGetValue(suffix, out List<ulong> entities))
				{
					entities.Add(entityID);
				}
				else
				{
					allEntities.Add(suffix, new List<ulong>() { entityID });
				}
			}
			else
			{
				data.vehicles.Add(playerID, new Dictionary<string, List<ulong>>() { { suffix, new List<ulong>() { entityID } } } );
			}
			Interface.CallHook("OnEntityAdded", playerID, suffix, entityID);
			return true;
		}

		bool RemoveEntity(ulong entityID, string suffix = "", ulong playerID = 0uL)
		{
			if (playerID != 0uL && !string.IsNullOrWhiteSpace(suffix))
			{
				if (!data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities) || !allEntities.TryGetValue(suffix, out List<ulong> entities) || !entities.Contains(entityID))
				{
					return false;
				}
				entities.Remove(entityID);
				Interface.CallHook("OnEntityRemoved", playerID, suffix, entityID);
				if (entities.Count < 1)
				{
					allEntities.Remove(suffix);
					if (allEntities.Count < 1)
					{
						data.vehicles.Remove(playerID);
					}
				}
				return true;
			}
			bool found = false;
			foreach (KeyValuePair<ulong, Dictionary<string, List<ulong>>> playerVehicles in data.vehicles)
			{
				foreach (KeyValuePair<string, List<ulong>> vehicleList in playerVehicles.Value)
				{
					for (int i = (vehicleList.Value.Count - 1); i >= 0; i--)
					{
						if (vehicleList.Value[i] != entityID)
						{
							continue;
						}
						found = true;
						vehicleList.Value.Remove(entityID);
						Interface.CallHook("OnEntityRemoved", playerVehicles.Key, vehicleList.Key, entityID);
						if (!found)
						{
							continue;
						}
						if (vehicleList.Value.Count < 1)
						{
							playerVehicles.Value.Remove(vehicleList.Key);
						}
						break;
					}
					if (!found)
					{
						continue;
					}
					if (playerVehicles.Value.Count < 1)
					{
						data.vehicles.Remove(playerVehicles.Key);
					}
					break;
				}
			}
			return found;
		}

		void OnEntityKill(BaseEntity entity)
		{
			if (!entity.IsValid())
			{
				return;
			}
			ulong entityID = entity.net.ID.Value;
			string suffix = GetSuffix(entityID);
			if (suffix == null)
			{
				return;
			}
			ulong playerID = GetOwnerUserID(entityID);
			Interface.CallHook("OnEntityKilled", playerID, suffix, entityID);
			RemoveEntity(entity.net.ID.Value, suffix, playerID);
			if (!config.NotifyWhenVehicleDestroyed)
			{
				return;
			}
			BasePlayer player = BasePlayer.FindByID(playerID);
			if (player == null)
			{
				return;
			}
			string name = string.Empty;
			if (config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				name = vehicleSettings.Name;
			}
			player.ChatMessage(string.Format(lang.GetMessage("Destroyed", this, player.UserIDString), (!string.IsNullOrEmpty(name) ? name : suffix)));
		}

		readonly DateTime december2021 = new DateTime(2021, 12, 26);

		double GetCooldownLeft(ulong playerID, string suffix, bool fetch = false)
		{
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return 0.0;
			}
			Dictionary<ulong, Dictionary<string, double>> dict = (fetch ? data.fetchCooldown : data.spawnCooldown);
			double epoch = DateTime.UtcNow.Subtract(december2021).TotalSeconds;
			double? cooldown = GetDouble(playerID.ToString(), (fetch ? vehicleSettings.FetchCooldown : vehicleSettings.SpawnCooldown));
			if (cooldown == null || cooldown.Value <= 0.0)
			{
				return 0.0;
			}
			if (!dict.TryGetValue(playerID, out Dictionary<string, double> allTimeSinceEpoch) || !allTimeSinceEpoch.TryGetValue(suffix, out double timeSinceEpoch))
			{
				return -1.0;
			}
			double passed = (epoch - timeSinceEpoch);
			double left = (cooldown.Value - passed);
			return left;
		}

		bool AddCooldown(ulong playerID, string suffix, bool fetch = false)
		{
			if (!config.Vehicles.ContainsKey(suffix))
			{
				return false;
			}
			Dictionary<ulong, Dictionary<string, double>> dict = (fetch ? data.fetchCooldown : data.spawnCooldown);
			double epoch = DateTime.UtcNow.Subtract(december2021).TotalSeconds;
			if (!dict.TryGetValue(playerID, out Dictionary<string, double> allTimeSinceEpoch))
			{
				dict.Add(playerID, new Dictionary<string, double>() { { suffix, epoch } } );
			}
			else
			{
				if (!allTimeSinceEpoch.ContainsKey(suffix))
				{
					allTimeSinceEpoch.Add(suffix, epoch);
				}
				else
				{
					allTimeSinceEpoch[suffix] = epoch;
				}
			}
			Interface.CallHook("OnCooldownAdded", playerID, suffix, fetch);
			return true;
		}

		bool ClearCooldowns(ulong playerID, string suffix = "", bool fetch = false)
		{
			Dictionary<ulong, Dictionary<string, double>> dict = (fetch ? data.fetchCooldown : data.spawnCooldown);
			if (!dict.TryGetValue(playerID, out Dictionary<string, double> allTimeSinceEpoch))
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(suffix))
			{
				if (!allTimeSinceEpoch.ContainsKey(suffix))
				{
					return false;
				}
				allTimeSinceEpoch.Remove(suffix);
				Interface.CallHook("OnCooldownCleared", playerID, suffix, fetch);
				if (allTimeSinceEpoch.Count < 1)
				{
					dict.Remove(playerID);
				}
			}
			else
			{
				dict.Remove(playerID);
			}
			return true;
		}

		string CooldownToString(double cooldown)
		{
			string left = string.Empty;
			if (cooldown < 0.0)
			{
				cooldown *= -1;
				left += "(negative) ";
			}
			TimeSpan passed = TimeSpan.FromSeconds(cooldown);
			if ((int)passed.TotalDays > 0)
			{
				left += passed.Days + (passed.Days == 1 ? " day" : " days");
			}
			if (passed.Hours > 0)
			{
				left += (passed.Days == 0 ? string.Empty : (passed.Minutes > 0 || passed.Seconds > 0 ? ", " : " and ")) + passed.Hours + (passed.Hours == 1 ? " hour" : " hours");
			}
			if (passed.Minutes > 0)
			{
				left += (passed.Days == 0 && passed.Hours == 0 ? string.Empty : (passed.Seconds > 0 ? ", " : " and ")) + passed.Minutes + (passed.Minutes == 1 ? " minute" : " minutes");
			}
			if (passed.Seconds > 0)
			{
				left += (passed.Days == 0 && passed.Hours == 0 && passed.Minutes == 0 ? string.Empty : " and ") + passed.Seconds + (passed.Seconds == 1 ? " second" : " seconds");
			}
			if (string.IsNullOrWhiteSpace(left))
			{
				left += passed.Milliseconds + (passed.Milliseconds == 1 ? " millisecond" : " milliseconds");
			}
			return left;
		}

		bool HasPermission(string playerId, string suffix, bool fetch = false)
		{
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return false;
			}
			foreach (KeyValuePair<string, double?> perm in (fetch ? vehicleSettings.FetchCooldown : vehicleSettings.SpawnCooldown))
			{
				if (string.IsNullOrWhiteSpace(perm.Key) || permission.UserHasPermission(playerId, perm.Key))
				{
					return true;
				}
			}
			return false;
		}

		bool? GetBool(string playerId, Dictionary<string, bool?> perms)
		{
			if (perms == null)
			{
				return null;
			}
			bool? b = null;
			foreach (KeyValuePair<string, bool?> perm in perms)
			{
				if (string.IsNullOrWhiteSpace(perm.Key) || permission.UserHasPermission(playerId, perm.Key))
				{
					b = perm.Value;
				}
			}
			return b;
		}

		double? GetDouble(string playerId, Dictionary<string, double?> perms)
		{
			if (perms == null)
			{
				return null;
			}
			double? d = null;
			foreach (KeyValuePair<string, double?> perm in perms)
			{
				if (string.IsNullOrWhiteSpace(perm.Key) || permission.UserHasPermission(playerId, perm.Key))
				{
					d = perm.Value;
				}
			}
			return d;
		}

		float? GetFloat(string playerId, Dictionary<string, float?> perms)
		{
			if (perms == null)
			{
				return null;
			}
			float? f = null;
			foreach (KeyValuePair<string, float?> perm in perms)
			{
				if (string.IsNullOrWhiteSpace(perm.Key) || permission.UserHasPermission(playerId, perm.Key))
				{
					f = perm.Value;
				}
			}
			return f;
		}

		int? GetInt(string playerId, Dictionary<string, int?> perms)
		{
			if (perms == null)
			{
				return null;
			}
			int? i = null;
			foreach (KeyValuePair<string, int?> perm in perms)
			{
				if (string.IsNullOrWhiteSpace(perm.Key) || permission.UserHasPermission(playerId, perm.Key))
				{
					i = perm.Value;
				}
			}
			return i;
		}

		List<PositionRotation> GetPositionRotation(string playerId, Dictionary<string, List<PositionRotation>> perms)
		{
			if (perms == null)
			{
				return null;
			}
			List<PositionRotation> prList = null;
			foreach (KeyValuePair<string, List<PositionRotation>> perm in perms)
			{
				if (string.IsNullOrWhiteSpace(perm.Key) || permission.UserHasPermission(playerId, perm.Key))
				{
					prList = perm.Value;
				}
			}
			return prList;
		}

		[PluginReference]
		readonly Plugin NoEscape;

		bool CanSpawn(BasePlayer player)
		{
			if (config.BlockWhenMountedOrParented && (player.isMounted || player.HasParent()))
			{
				player.ChatMessage(lang.GetMessage("MountedOrParented", this, player.UserIDString));
				return false;
			}
			if (config.BlockWhenBuildingBlocked && player.IsBuildingBlocked())
			{
				player.ChatMessage(lang.GetMessage("BuildingBlocked", this, player.UserIDString));
				return false;
			}
			if (config.BlockInSafeZone && player.InSafeZone())
			{
				player.ChatMessage(lang.GetMessage("InSafeZone", this, player.UserIDString));
				return false;
			}
			if (!NoEscape)
			{
				return true;
			}
			if (config.BlockWhenCombatBlocked && NoEscape.Call<bool>("IsCombatBlocked", player.UserIDString))
			{
				player.ChatMessage(lang.GetMessage("CombatBlocked", this, player.UserIDString));
				return false;
			}
			if (config.BlockWhenRaidBlocked && NoEscape.Call<bool>("IsRaidBlocked", player.UserIDString))
			{
				player.ChatMessage(lang.GetMessage("RaidBlocked", this, player.UserIDString));
				return false;
			}
			return true;
		}

		bool CheckSurface(string suffix, Vector3 position, out bool water)
		{
			water = false;
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return false;
			}
			if (WaterLevel.Test(position, false, false))
			{
				water = true;
				return !vehicleSettings.CanNotSpawnOnWater;
			}
			return !vehicleSettings.CanOnlySpawnOnWater;
		}

		bool TryMoveToTrainTrack(TrainCar train, Vector3 position)
		{
			if (!TrainTrackSpline.TryFindTrackNear(train.GetFrontWheelPos(), 15.0f, out TrainTrackSpline trainTrackSpline, out float frontWheelSplineDist))
			{
				return false;
			}
			train.FrontWheelSplineDist = frontWheelSplineDist;
			Vector3 positionAndTangent = trainTrackSpline.GetPositionAndTangent(train.FrontWheelSplineDist, train.transform.forward, out Vector3 targetFrontWheelTangent);
			train.SetTheRestFromFrontWheelData(ref trainTrackSpline, positionAndTangent, targetFrontWheelTangent, train.localTrackSelection, null, true);
			train.FrontTrackSection = trainTrackSpline;
			return true;
		}

		Vector3 GetGroundPositionLookingAt(BasePlayer player, float maxDistance)
		{
			Ray headRay = player.eyes.HeadRay();
			if (Physics.Raycast(headRay, out RaycastHit hitInfo, maxDistance, Layers.Solid))
			{
				return hitInfo.point;
			}
			Vector3 position = (headRay.origin + headRay.direction * maxDistance);
			position.y = (Physics.Raycast(position, Vector3.down, out RaycastHit hitInfoDown, 50.0f, Layers.Solid) ? hitInfoDown.point.y : TerrainMeta.HeightMap.GetHeight(position));
			if (player.Distance(position) > maxDistance)
			{
				return Vector3.zero;
			}
			return position;
		}

		void RemoveMapMarker(BaseEntity entity)
		{
			if (!config.RemoveChinookMapMarker)
			{
				return;
			}
			CH47Helicopter ch47 = entity as CH47Helicopter;
			if (ch47 == null)
			{
				return;
			}
			BaseEntity mapMarker = ch47.mapMarkerInstance;
			if (mapMarker != null)
			{
				mapMarker.Kill();
			}
			ch47.mapMarkerEntityPrefab.guid = null;
		}

		bool SetFuelConsumption(BaseEntity entity)
		{
			MagnetCrane crane = entity as MagnetCrane;
			if (crane != null)
			{
				crane.maxDistanceFromOrigin = 0.0f;
			}
			string suffix = GetSuffix(entity.net.ID.Value);
			if (suffix == null)
			{
				return false;
			}
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return false;
			}
			ulong playerID = GetOwnerUserID(entity.net.ID.Value);
			HotAirBalloon hab = entity as HotAirBalloon;
			float? fuelPerSec = GetFloat(playerID.ToString(), vehicleSettings.FuelPerSecond);
			if (hab != null)
			{
				if (vehicleSettings.FuelPerSecond != null)
				{
					hab.fuelPerSec = fuelPerSec.Value;
				}
				Interface.CallHook("OnFuelConsumptionSet", playerID, suffix, entity);
				return true;
			}
			BaseVehicle vehicle = entity as BaseVehicle;
			if (vehicle == null)
			{
				return false;
			}
			float? idleFuelPerSec = GetFloat(playerID.ToString(), vehicleSettings.IdleFuelPerSecond);
			float? maxFuelPerSec = GetFloat(playerID.ToString(), vehicleSettings.MaxFuelPerSecond);
			if (fuelPerSec != null)
			{
				PlayerHelicopter helicopter = entity as PlayerHelicopter;
				MotorRowboat motorboat = entity as MotorRowboat;
				if (helicopter != null)
				{
					helicopter.fuelPerSec = fuelPerSec.Value;
				}
				else if (motorboat != null)
				{
					motorboat.fuelPerSec = fuelPerSec.Value;
				}
			}
			else if (idleFuelPerSec != null && maxFuelPerSec != null)
			{
				BaseSubmarine submarine = entity as BaseSubmarine;
				Snowmobile snow = entity as Snowmobile;
				//Bike bike = entity as Bike;
				TrainEngine workcart = entity as TrainEngine;
				if (submarine != null)
				{
					submarine.idleFuelPerSec = idleFuelPerSec.Value;
					submarine.maxFuelPerSec = maxFuelPerSec.Value;
				}
				else if (snow != null)
				{
					snow.idleFuelPerSec = idleFuelPerSec.Value;
					snow.maxFuelPerSec = maxFuelPerSec.Value;
				}
				/*else if (bike != null)
				{
					bike.idleFuelPerSec = idleFuelPerSec.Value;
					bike.maxFuelPerSec = maxFuelPerSec.Value;
				}*/
				else if (crane != null)
				{
					crane.idleFuelPerSec = idleFuelPerSec.Value;
					crane.maxFuelPerSec = maxFuelPerSec.Value;
				}
				else if (workcart != null)
				{
					workcart.idleFuelPerSec = idleFuelPerSec.Value;
					workcart.maxFuelPerSec = maxFuelPerSec.Value;
				}
			}
			Interface.CallHook("OnFuelConsumptionSet", playerID, suffix, entity);
			return true;
		}

		bool SetModuleEngineFuelConsumption(VehicleModuleEngine moduleEngine)
		{
			ModularCar car = moduleEngine.Vehicle as ModularCar;
			if (car == null)
			{
				return false;
			}
			string suffix = GetSuffix(car.net.ID.Value);
			if (suffix == null)
			{
				return false;
			}
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return false;
			}
			ulong playerID = GetOwnerUserID(car.net.ID.Value);
			float? idleFuelPerSec = GetFloat(playerID.ToString(), vehicleSettings.IdleFuelPerSecond);
			float? maxFuelPerSec = GetFloat(playerID.ToString(), vehicleSettings.MaxFuelPerSecond);
			if (idleFuelPerSec == null || maxFuelPerSec == null)
			{
				return false;
			}
			VehicleModuleEngine.Engine engine = moduleEngine.engine;
			if (engine == null)
			{
				return false;
			}
			engine.idleFuelPerSec = idleFuelPerSec.Value;
			engine.maxFuelPerSec = maxFuelPerSec.Value;
			Interface.CallHook("OnModuleEngineFuelConsumptionSet", playerID, suffix, moduleEngine);
			return true;
		}

		void OnEntitySpawned(VehicleModuleEngine moduleEngine)
		{
			NextTick(
				() =>
				{
					if (moduleEngine.IsValid() && !moduleEngine.IsDestroyed)
					{
						SetModuleEngineFuelConsumption(moduleEngine);
					}
				}
			);
		}

		int AddFuel(EntityFuelSystem fuelSystem)
		{
			if (fuelSystem == null)
			{
				return -1;
			}
			StorageContainer fuelContainer = fuelSystem.fuelStorageInstance.Get(fuelSystem.isServer);
			if (fuelContainer == null || !fuelContainer.IsValid())
			{
				return -1;
			}
			BaseEntity entity = fuelContainer.GetParentEntity();
			string suffix = GetSuffix(entity.net.ID.Value);
			if (suffix == null)
			{
				return -1;
			}
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return -1;
			}
			ulong playerID = GetOwnerUserID(entity.net.ID.Value);
			bool? lockFuelContainer = GetBool(playerID.ToString(), vehicleSettings.LockFuelContainer);
			if (lockFuelContainer != null && lockFuelContainer.Value == true)
			{
				fuelContainer.SetFlag(BaseEntity.Flags.Locked, true);
				Interface.CallHook("OnFuelContainerLocked", playerID, suffix, entity);
			}
			ItemContainer container = fuelContainer.inventory;
			if (container == null)
			{
				return -1;
			}
			int? startingFuel = GetInt(playerID.ToString(), vehicleSettings.StartingFuel);
			if (startingFuel == null || startingFuel.Value == 0)
			{
				return 0;
			}
			Item fuel = ItemManager.Create(fuelContainer.allowedItem, startingFuel.Value);
			if (!fuel.MoveToContainer(container))
			{
				fuel.Remove();
				return -1;
			}
			Interface.CallHook("OnFuelAdded", playerID, suffix, entity, startingFuel.Value);
			return startingFuel.Value;
		}

		int RefundFuel(EntityFuelSystem fuelSystem, BasePlayer player)
		{
			if (fuelSystem == null)
			{
				return -1;
			}
			StorageContainer fuelContainer = fuelSystem.fuelStorageInstance.Get(fuelSystem.isServer);
			if (fuelContainer == null)
			{
				return -1;
			}
			ItemContainer container = fuelContainer.inventory;
			if (container == null)
			{
				return -1;
			}
			Item fuelItem = container.GetSlot(0);
			if (fuelItem == null)
			{
				return 0;
			}
			int amount = fuelItem.amount;
			player.GiveItem(fuelItem);
			Interface.CallHook("OnFuelRefunded", player, amount);
			return amount;
		}

		List<BaseVehicle.MountPointInfo> AddMounts(string playerId, string suffix, BaseVehicle vehicle)
		{
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return null;
			}
			List<PositionRotation> prList = GetPositionRotation(playerId, vehicleSettings.ExtraMounts);
			if (prList == null)
			{
				return null;
			}
			List<BaseVehicle.MountPointInfo> mounts = new List<BaseVehicle.MountPointInfo>();
			int num = (vehicle.mountPoints.Count > 1 ? 1 : 0);
			for (int i = 0; i < prList.Count; i++)
			{
				PositionRotation pr = prList[i];
				BaseVehicle.MountPointInfo mount = new BaseVehicle.MountPointInfo
				{
					pos = new Vector3(pr.pX, pr.pY, pr.pZ),
					rot = new Vector3(pr.rX, pr.rY, pr.rZ),
					prefab = vehicle.mountPoints[num].prefab,
					mountable = vehicle.mountPoints[num].mountable
				};
				vehicle.mountPoints.Add(mount);
				mounts.Add(mount);
			}
			return mounts;
		}

		List<BaseEntity> AddSeats(string playerId, string suffix, BaseEntity entity)
		{
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return null;
			}
			List<PositionRotation> prList = GetPositionRotation(playerId, vehicleSettings.ExtraSeats);
			if (prList == null)
			{
				return null;
			}
			List<BaseEntity> seats = new List<BaseEntity>();
			for (int i = 0; i < prList.Count; i++)
			{
				PositionRotation pr = prList[i];
				BaseEntity seat = GameManager.server.CreateEntity("assets/prefabs/vehicle/seats/passengerchair.prefab", entity.transform.position);
				if (seat == null)
				{
					continue;
				}
				seat.SetParent(entity);
				seat.transform.localPosition = new Vector3(pr.pX, pr.pY, pr.pZ);
				seat.transform.localRotation = Quaternion.Euler(pr.rX, pr.rY, pr.rZ);
				seat.Spawn();
				seats.Add(seat);
			}
			return seats;
		}

		List<BasePlayer> GetMountedOccupants(BaseEntity entity)
		{
			List<BasePlayer> m = new List<BasePlayer>();
			BaseVehicle vehicle = entity as BaseVehicle;
			if (vehicle == null)
			{
				return m;
			}
			foreach (BaseVehicle.MountPointInfo mountPoint in vehicle.allMountPoints)
			{
				if (mountPoint == null || mountPoint.mountable == null)
				{
					continue;
				}
				BasePlayer mounted = mountPoint.mountable.GetMounted();
				if (mounted != null)
				{
					m.Add(mounted);
				}
			}
			return m;
		}

		List<BasePlayer> GetParentedOccupants(BaseEntity entity)
		{
			List<BasePlayer> p = new List<BasePlayer>();
			BasePlayer[] parented = entity.GetComponentsInChildren<BasePlayer>();
			for (int i = 0; i < parented.Length; i++)
			{
				p.Add(parented[i]);
			}
			return p;
		}

		void DismountOccupants(List<BasePlayer> mounted)
		{
			for (int i = 0; i < mounted.Count; i++)
			{
				mounted[i].EnsureDismounted();
			}
		}

		void UnparentOccupants(List<BasePlayer> parented)
		{
			for (int i = 0; i < parented.Count; i++)
			{
				parented[i].SetParent(null, true, true);
			}
		}

		string GetMapGrid(Vector3 position)
		{
			char letter = 'A';
			letter = (char)(((int)letter) + (Mathf.Floor((position.x + (ConVar.Server.worldsize / 2)) / 146.3f) % 26));
			return $"{letter}{(Mathf.Floor(ConVar.Server.worldsize / 146.3f)) - Mathf.Floor((position.z + (ConVar.Server.worldsize / 2)) / 146.3f)}";
		}

		BaseEntity CreateEntity(ulong playerID, string suffix, Vector3 position, float YrotationOffset = -90.0f)
		{
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				return null;
			}
			BaseEntity entity = GameManager.server.CreateEntity(vehicleSettings.Prefab, position, Quaternion.Euler(0.0f, YrotationOffset, 0.0f));
			if (entity == null || entity.IsDestroyed)
			{
				return null;
			}
			entity.OwnerID = playerID;
			Interface.CallHook("OnEntityCreated", playerID, suffix, entity);
			return entity;
		}

		BaseEntity FetchEntity(ulong playerID, string suffix, Vector3 position, float YrotationOffset = -90.0f)
		{
			if (!data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities) || !allEntities.TryGetValue(suffix, out List<ulong> entities) || entities.Count < 1)
			{
				return null;
			}
			BaseEntity entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entities[entities.Count - 1])) as BaseEntity;
			if (entity.HasParent())
			{
				entity.SetParent(null, true, true);
			}
			if (entity is ModularCar)
			{
				List<ModularCarGarage> carLifts = Pool.GetList<ModularCarGarage>();
				Vis.Entities(entity.transform.position, 3f, carLifts, (Layers.Mask.Deployed | Layers.Mask.Default));
				ModularCarGarage carLift = null;
				for (int i = 0; i < carLifts.Count; i++)
				{
					if (carLifts[i].carOccupant == entity)
					{
						carLift = carLifts[i];
						break;
					}
				}
				Pool.FreeList(ref carLifts);
				if (carLift != null)
				{
					carLift.enabled = false;
					carLift.ReleaseOccupant();
					carLift.Invoke(() => carLift.enabled = true, 0.25f);
				}
			}
			entity.transform.position = position;
			entity.transform.rotation = Quaternion.Euler(0.0f, YrotationOffset, 0.0f);
			BaseVehicle vehicle = entity as BaseVehicle;
			if (vehicle != null)
			{
				Rigidbody rb = vehicle.rigidBody;
				if (rb != null)
				{
					float maxAngularVelocity = rb.maxAngularVelocity;
					rb.maxAngularVelocity = 0.0f;
					timer.Once(
						1.0f,
						() =>
						{
							if (vehicle != null && rb != null)
							{
								rb.maxAngularVelocity = maxAngularVelocity;
							}
						}
					);
				}
			}
			entity.SetVelocity(Vector3.zero);
			entity.SetAngularVelocity(Vector3.zero);
			entity.UpdateNetworkGroup();
			entity.SendNetworkUpdate();
			Interface.CallHook("OnEntityFetched", playerID, suffix, entity);
			return entity;
		}

		void OnPlayerDisconnected(BasePlayer player)
		{
			DespawnAllEntities(player.userID);
		}

		void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
		{
			if (!entity.IsValid() || info == null || info.damageTypes == null || !info.damageTypes.Has(DamageType.Decay))
			{
				return;
			}
			if (!IsPlayerEntity(entity.net.ID.Value))
			{
				BaseVehicleModule module = entity as BaseVehicleModule;
				if (module == null || !IsPlayerEntity(module.Vehicle.net.ID.Value))
				{
					return;
				}
			}
			info.damageTypes.Scale(DamageType.Decay, 0.0f);
		}
#region Chat commands
		void ChatSpawn(BasePlayer player, string command)
		{
			string suffix = command.ToLower().Remove(0, config.SpawnCommandPrefix.Length);
			VehicleSettings vehicleSettings = config.Vehicles[suffix];
			if (!HasPermission(player.UserIDString, suffix))
			{
				player.ChatMessage(string.Format(lang.GetMessage("NoPermissionSpawn", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			if (!CanSpawn(player))
			{
				return;
			}
			float? distance = GetFloat(player.UserIDString, vehicleSettings.MaxSpawnDistance);
			if (distance == null)
			{
				distance = 100.0f;
			}
			Vector3 position = GetGroundPositionLookingAt(player, distance.Value);
			if (position == Vector3.zero)
			{
				player.ChatMessage(string.Format(lang.GetMessage("LookingTooFar", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			if (!CheckSurface(suffix, position, out bool water))
			{
				player.ChatMessage(string.Format(lang.GetMessage((water ? "OnWater" : "NotOnWater"), this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			double left;
			ulong playerID = player.userID;
			BaseEntity oldEntity = null;
			if (data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities) && allEntities.TryGetValue(suffix, out List<ulong> entities) && entities.Count > 0)
			{
				oldEntity = BaseNetworkable.serverEntities.Find(new NetworkableId(entities[entities.Count - 1])) as BaseEntity;
			}
			if (oldEntity != null)
			{
				if (config.FetchOldVehicleInsteadOfSpawningIdentical)
				{
					if (HasPermission(player.UserIDString, suffix, true))
					{
						left = GetCooldownLeft(playerID, suffix, true);
						if (left > 0.0)
						{
							player.ChatMessage(string.Format(lang.GetMessage("FetchCooldown", this, player.UserIDString), CooldownToString(left), vehicleSettings.Name));
							return;
						}
						float? fetchDistanceLimit = GetFloat(player.UserIDString, vehicleSettings.FetchDistanceLimit);
						float oldEntityDistance = oldEntity.Distance(player);
						if (fetchDistanceLimit == null || fetchDistanceLimit.Value == 0.0f || oldEntityDistance <= fetchDistanceLimit.Value)
						{
							TrainCar oldTrain = oldEntity as TrainCar;
							if (oldTrain != null && !TryMoveToTrainTrack(oldTrain, position))
							{
								player.ChatMessage(string.Format(lang.GetMessage("TracksNotFound", this, player.UserIDString), vehicleSettings.Name));
								return;
							}
							List<BasePlayer> mounted = GetMountedOccupants(oldEntity);
							List<BasePlayer> parented = GetParentedOccupants(oldEntity);
							if (!config.AllowFetchingWhenOccupied && (mounted.Count > 0 || parented.Count > 0))
							{
								player.ChatMessage(string.Format(lang.GetMessage("BeingUsedFetch", this, player.UserIDString), vehicleSettings.Name));
								return;
							}
							if (config.DismountOccupantsWhenFetching)
							{
								DismountOccupants(mounted);
								UnparentOccupants(parented);
							}
							FetchEntity(playerID, suffix, position, (player.GetNetworkRotation().eulerAngles.y + vehicleSettings.YRotationSpawnOffset));
							AddCooldown(playerID, suffix, true);
							player.ChatMessage(string.Format(lang.GetMessage("Fetched", this, player.UserIDString), vehicleSettings.Name));
							return;
						}
						if (oldEntityDistance > fetchDistanceLimit.Value)
						{
							player.ChatMessage(string.Format(lang.GetMessage("TooFarFetch", this, player.UserIDString), vehicleSettings.Name));
							return;
						}
					}
					else
					{
						player.ChatMessage(string.Format(lang.GetMessage("NoPermissionFetch", this, player.UserIDString), vehicleSettings.Name));
						return;
					}
				}
				if (!config.AllowMultipleIdentical)
				{
					player.ChatMessage(string.Format(lang.GetMessage("AlreadySpawned", this, player.UserIDString), vehicleSettings.Name, config.FetchCommandPrefix+suffix, config.DespawnCommandPrefix+suffix));
					return;
				}
			}
			left = GetCooldownLeft(playerID, suffix);
			if (left > 0.0)
			{
				player.ChatMessage(string.Format(lang.GetMessage("SpawnCooldown", this, player.UserIDString), CooldownToString(left), vehicleSettings.Name));
				return;
			}
			BaseEntity entity = CreateEntity(playerID, suffix, position, (player.GetNetworkRotation().eulerAngles.y + vehicleSettings.YRotationSpawnOffset));
			if (entity == null)
			{
				return;
			}
			TrainCar train = entity as TrainCar;
			if (train != null && !TryMoveToTrainTrack(train, position))
			{
				entity.Spawn();
				entity.Kill();
				player.ChatMessage(string.Format(lang.GetMessage("TracksNotFound", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			BaseVehicle vehicle = entity as BaseVehicle;
			if (vehicle != null)
			{
				AddMounts(player.UserIDString, suffix, vehicle);
			}
			entity.Spawn();
			AddEntity(playerID, suffix, entity.net.ID.Value);
			AddCooldown(playerID, suffix);
			RemoveMapMarker(entity);
			AddSeats(player.UserIDString, suffix, entity);
			SetFuelConsumption(entity);
			HotAirBalloon hab = entity as HotAirBalloon;
			AddFuel((vehicle != null ? vehicle.GetFuelSystem() as EntityFuelSystem : hab != null ? hab.fuelSystem as EntityFuelSystem : null));
			player.ChatMessage(string.Format(lang.GetMessage("Spawned", this, player.UserIDString), vehicleSettings.Name));
		}

		void ChatFetch(BasePlayer player, string command)
		{
			string suffix = command.ToLower().Remove(0, config.FetchCommandPrefix.Length);
			VehicleSettings vehicleSettings = config.Vehicles[suffix];
			if (!HasPermission(player.UserIDString, suffix, true))
			{
				player.ChatMessage(string.Format(lang.GetMessage("NoPermissionFetch", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			if (!CanSpawn(player))
			{
				return;
			}
			ulong playerID = player.userID;
			double left = GetCooldownLeft(playerID, suffix, true);
			if (left > 0.0)
			{
				player.ChatMessage(string.Format(lang.GetMessage("FetchCooldown", this, player.UserIDString), CooldownToString(left), vehicleSettings.Name));
				return;
			}
			BaseEntity entity = null;
			if (data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities) && allEntities.TryGetValue(suffix, out List<ulong> entities) && entities.Count > 0)
			{
				entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entities[entities.Count - 1])) as BaseEntity;
			}
			if (entity == null)
			{
				player.ChatMessage(string.Format(lang.GetMessage("NotFound", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			float? fetchDistanceLimit = GetFloat(player.UserIDString, vehicleSettings.FetchDistanceLimit);
			if (fetchDistanceLimit != null && fetchDistanceLimit.Value > 0.0f && entity.Distance(player) > fetchDistanceLimit.Value)
			{
				player.ChatMessage(string.Format(lang.GetMessage("TooFarFetch", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			float? distance = GetFloat(player.UserIDString, vehicleSettings.MaxSpawnDistance);
			if (distance == null)
			{
				distance = 100.0f;
			}
			Vector3 position = GetGroundPositionLookingAt(player, distance.Value);
			if (position == Vector3.zero)
			{
				player.ChatMessage(string.Format(lang.GetMessage("LookingTooFar", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			if (!CheckSurface(suffix, position, out bool water))
			{
				player.ChatMessage(string.Format(lang.GetMessage((water ? "OnWater" : "NotOnWater"), this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			TrainCar train = entity as TrainCar;
			if (train != null && !TryMoveToTrainTrack(train, position))
			{
				player.ChatMessage(string.Format(lang.GetMessage("TracksNotFound", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			List<BasePlayer> mounted = GetMountedOccupants(entity);
			List<BasePlayer> parented = GetParentedOccupants(entity);
			if (!config.AllowFetchingWhenOccupied && (mounted.Count > 0 || parented.Count > 0))
			{
				player.ChatMessage(string.Format(lang.GetMessage("BeingUsedFetch", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			if (config.DismountOccupantsWhenFetching)
			{
				DismountOccupants(mounted);
				UnparentOccupants(parented);
			}
			FetchEntity(playerID, suffix, position, (player.GetNetworkRotation().eulerAngles.y + vehicleSettings.YRotationSpawnOffset));
			AddCooldown(playerID, suffix, true);
			player.ChatMessage(string.Format(lang.GetMessage("Fetched", this, player.UserIDString), vehicleSettings.Name));
		}

		void ChatDespawn(BasePlayer player, string command)
		{
			string suffix = command.ToLower().Remove(0, config.DespawnCommandPrefix.Length);
			ulong playerID = player.userID;
			VehicleSettings vehicleSettings = config.Vehicles[suffix];
			BaseEntity entity = null;
			if (data.vehicles.TryGetValue(playerID, out Dictionary<string, List<ulong>> allEntities) && allEntities.TryGetValue(suffix, out List<ulong> entities) && entities.Count > 0)
			{
				entity = BaseNetworkable.serverEntities.Find(new NetworkableId(entities[entities.Count - 1])) as BaseEntity;
			}
			if (entity == null)
			{
				player.ChatMessage(string.Format(lang.GetMessage("NotFound", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			float? despawnDistanceLimit = GetFloat(player.UserIDString, vehicleSettings.DespawnDistanceLimit);
			if (despawnDistanceLimit != null && despawnDistanceLimit.Value > 0.0f && entity.Distance(player) > despawnDistanceLimit.Value)
			{
				player.ChatMessage(string.Format(lang.GetMessage("TooFarDespawn", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			BaseVehicle vehicle = entity as BaseVehicle;
			if (!config.AllowDespawningWhenOccupied && ((vehicle != null && vehicle.AnyMounted()) || entity.GetComponentsInChildren<BasePlayer>().Length > 0))
			{
				player.ChatMessage(string.Format(lang.GetMessage("BeingUsedDespawn", this, player.UserIDString), vehicleSettings.Name));
				return;
			}
			int amount = 0;
			if (config.RefundFuelOnDespawn)
			{
				HotAirBalloon hab = entity as HotAirBalloon;
				amount = RefundFuel((vehicle != null ? vehicle.GetFuelSystem() as EntityFuelSystem : hab != null ? hab.fuelSystem as EntityFuelSystem : null), player);
			}
			if (config.NotifyWhenVehicleDestroyed)
			{
				RemoveEntity(entity.net.ID.Value, suffix, playerID);
			}
			entity.Kill();
			Interface.CallHook("OnEntityDespawned", playerID, suffix);
			player.ChatMessage(string.Format(lang.GetMessage("Despawned", this, player.UserIDString), vehicleSettings.Name, (amount > 0 ? string.Format(lang.GetMessage("Refunded", this, player.UserIDString), amount) : string.Empty)));
		}
#endregion
#region Console commands
		[ConsoleCommand("vehicles.renamesuffix")]
		void RenameSuffix(ConsoleSystem.Arg arg)
		{
			if (!arg.IsRcon && arg.Connection.authLevel < 2)
			{
				return;
			}
			if (arg.Args == null || arg.Args.Length < 1)
			{
				SendReply(arg, "The first argument is the suffix to be renamed and the second argument is its new suffix.");
				return;
			}
			string oldSuffix = arg.Args[0].ToLower();
			if (!config.Vehicles.ContainsKey(oldSuffix))
			{
				SendReply(arg, $"The config does not contain the suffix \"{oldSuffix}\".");
				return;
			}
			if (arg.Args.Length < 2)
			{
				SendReply(arg, "You must specify the new suffix.");
				return;
			}
			string newSuffix = arg.Args[1].ToLower();
			if (config.Vehicles.ContainsKey(newSuffix))
			{
				SendReply(arg, $"The config already contains the suffix \"{newSuffix}\".");
				return;
			}
			if (oldSuffix == newSuffix)
			{
				SendReply(arg, "The existing suffix and the new suffix must be different.");
				return;
			}
			Dictionary<string, VehicleSettings> newVehiclesConfig = new Dictionary<string, VehicleSettings>();
			foreach (KeyValuePair<string, VehicleSettings> vehicleConfig in config.Vehicles)
			{
				if (vehicleConfig.Key == oldSuffix)
				{
					newVehiclesConfig.Add(newSuffix, vehicleConfig.Value);
				}
				else
				{
					newVehiclesConfig.Add(vehicleConfig.Key, vehicleConfig.Value);
				}
			}
			config.Vehicles = newVehiclesConfig;
			SaveConfig();
			cmd.RemoveChatCommand(config.SpawnCommandPrefix+oldSuffix, this);
			cmd.RemoveChatCommand(config.FetchCommandPrefix+oldSuffix, this);
			cmd.RemoveChatCommand(config.DespawnCommandPrefix+oldSuffix, this);
			cmd.AddChatCommand(config.SpawnCommandPrefix+newSuffix, this, nameof(ChatSpawn));
			cmd.AddChatCommand(config.FetchCommandPrefix+newSuffix, this, nameof(ChatFetch));
			cmd.AddChatCommand(config.DespawnCommandPrefix+newSuffix, this, nameof(ChatDespawn));
			Dictionary<ulong, Dictionary<string, List<ulong>>> newVehicleData = new Dictionary<ulong, Dictionary<string, List<ulong>>>();
			foreach (KeyValuePair<ulong, Dictionary<string, List<ulong>>> playerVehicles in data.vehicles)
			{
				Dictionary<string, List<ulong>> newVehicleList = new Dictionary<string, List<ulong>>();
				foreach (KeyValuePair<string, List<ulong>> vehicleList in playerVehicles.Value)
				{
					if (vehicleList.Key == oldSuffix)
					{
						newVehicleList.Add(newSuffix, vehicleList.Value);
					}
					else
					{
						newVehicleList.Add(vehicleList.Key, vehicleList.Value);
					}
				}
				newVehicleData.Add(playerVehicles.Key, newVehicleList);
			}
			data.vehicles = newVehicleData;
			Dictionary<ulong, Dictionary<string, double>> newSpawnCooldownData = new Dictionary<ulong, Dictionary<string, double>>();
			foreach (KeyValuePair<ulong, Dictionary<string, double>> spawnCooldowns in data.spawnCooldown)
			{
				Dictionary<string, double> newSpawnCooldown = new Dictionary<string, double>();
				foreach (KeyValuePair<string, double> spawnCooldown in spawnCooldowns.Value)
				{
					if (spawnCooldown.Key == oldSuffix)
					{
						newSpawnCooldown.Add(newSuffix, spawnCooldown.Value);
					}
					else
					{
						newSpawnCooldown.Add(spawnCooldown.Key, spawnCooldown.Value);
					}
				}
				newSpawnCooldownData.Add(spawnCooldowns.Key, newSpawnCooldown);
			}
			data.spawnCooldown = newSpawnCooldownData;
			Dictionary<ulong, Dictionary<string, double>> newFetchCooldownData = new Dictionary<ulong, Dictionary<string, double>>();
			foreach (KeyValuePair<ulong, Dictionary<string, double>> fetchCooldowns in data.fetchCooldown)
			{
				Dictionary<string, double> newFetchCooldown = new Dictionary<string, double>();
				foreach (KeyValuePair<string, double> fetchCooldown in fetchCooldowns.Value)
				{
					if (fetchCooldown.Key == oldSuffix)
					{
						newFetchCooldown.Add(newSuffix, fetchCooldown.Value);
					}
					else
					{
						newFetchCooldown.Add(fetchCooldown.Key, fetchCooldown.Value);
					}
				}
				newFetchCooldownData.Add(fetchCooldowns.Key, newFetchCooldown);
			}
			data.fetchCooldown = newFetchCooldownData;
			SendReply(arg, $"Successfully renamed \"{oldSuffix}\" to \"{newSuffix}\".");
		}

		[ConsoleCommand("vehicles.spawn")]
		void ConsoleSpawn(ConsoleSystem.Arg arg)
		{
			if (!arg.IsRcon && arg.Connection.authLevel < 2)
			{
				return;
			}
			if (arg.Args == null || arg.Args.Length < 1)
			{
				SendReply(arg, "The first argument is the player's SteamID, the second argument is the suffix to spawn and the third argument (optional, based on the player's permission or defaults to 15) is the maximum distance allowed to spawn from the player.");
				return;
			}
			if (!ulong.TryParse(arg.Args[0], out ulong playerID) || !playerID.IsSteamId())
			{
				SendReply(arg, $"\"{arg.Args[0]}\" is not a valid SteamID.");
				return;
			}
			BasePlayer player = BasePlayer.FindByID(playerID);
			if (player == null)
			{
				SendReply(arg, $"Player with SteamID {playerID} could not be found.");
				return;
			}
			if (arg.Args.Length < 2)
			{
				SendReply(arg, "You must specify which suffix to spawn.");
				return;
			}
			string suffix = arg.Args[1];
			if (!config.Vehicles.TryGetValue(suffix, out VehicleSettings vehicleSettings))
			{
				SendReply(arg, $"The config does not contain the suffix \"{suffix}\".");
				return;
			}
			float? distance = null;
			if (arg.Args.Length < 3)
			{
				distance = GetFloat(player.UserIDString, vehicleSettings.MaxSpawnDistance);
			}
			else
			{
				if (float.TryParse(arg.Args[2], out float d))
				{
					distance = d;
				}
			}
			if (distance == null)
			{
				distance = 15.0f;
			}
			Vector3 position = GetGroundPositionLookingAt(player, distance.Value);
			if (position == Vector3.zero)
			{
				SendReply(arg, $"The player is not looking at a surface within {distance} meters.");
				return;
			}
			if (!CheckSurface(suffix, position, out bool water))
			{
				SendReply(arg, $"The player must be looking at {(water ? "water" : "land")} to be able to spawn a {vehicleSettings.Name}.");
				return;
			}
			BaseEntity entity = CreateEntity(playerID, suffix, position, (player.GetNetworkRotation().eulerAngles.y + vehicleSettings.YRotationSpawnOffset));
			if (entity == null)
			{
				return;
			}
			TrainCar train = entity as TrainCar;
			if (train != null && !TryMoveToTrainTrack(train, position))
			{
				entity.Spawn();
				entity.Kill();
				SendReply(arg, $"The player must be looking at train tracks to be able to spawn a {vehicleSettings.Name}.");
				return;
			}
			BaseVehicle vehicle = entity as BaseVehicle;
			if (vehicle != null)
			{
				AddMounts(player.UserIDString, suffix, vehicle);
			}
			entity.Spawn();
			AddEntity(playerID, suffix, entity.net.ID.Value);
			RemoveMapMarker(entity);
			AddSeats(player.UserIDString, suffix, entity);
			SetFuelConsumption(entity);
			HotAirBalloon hab = entity as HotAirBalloon;
			AddFuel((vehicle != null ? vehicle.GetFuelSystem() as EntityFuelSystem : hab != null ? hab.fuelSystem as EntityFuelSystem : null));
			SendReply(arg, $"Successfully spawned a {vehicleSettings.Name} for {player} at {GetMapGrid(position)} {position}.");
		}
#endregion
	}
}