using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Congratulations", "Mestre Pardal", "1.2.4")]
    [Description("Spawns and automatically ignites fireworks around players via command.")]
    public class Congratulations : RustPlugin
    {
        #region Config

        private ConfigData config;

        private class ConfigData
        {
            public List<string> FireworkShortnames = new List<string>
            {
                "firework.boomer.blue",
                "firework.boomer.green",
                "firework.boomer.red",
                "firework.boomer.violet"
            };

            public int FireworksCount = 4;

            public float RadiusAroundPlayer = 0.8f;

            public float HeightOffset = 0.1f;

            public bool LogToConsole = true;
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                if (config == null)
                    throw new Exception("Config null, creating new one.");
            }
            catch (Exception e)
            {
                PrintError($"Error reading config, creating default. {e}");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        #endregion

        #region Console Commands

        [ConsoleCommand("congrats")]
        private void CCongrats(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin)
            {
                arg.ReplyWith("No permission.");
                return;
            }

            if (arg.Args == null || arg.Args.Length < 1)
            {
                arg.ReplyWith("Usage: congrats <steamIdOrName>");
                return;
            }

            string targetArg = string.Join(" ", arg.Args);

            BasePlayer targetPlayer = FindPlayerByIdOrName(targetArg);
            if (targetPlayer == null)
            {
                arg.ReplyWith($"[Congratulations] Player '{targetArg}' not found (online or sleeping).");
                return;
            }

            if (config.LogToConsole)
                Puts($"[Congratulations] Command from console: spawning fireworks around {targetPlayer.displayName} ({targetPlayer.UserIDString}).");

            FireShowAroundPlayer(targetPlayer);

            arg.ReplyWith($"[Congratulations] Fireworks spawned around {targetPlayer.displayName}!");
        }

        /// <summary>
        /// NEW: console congratsall — 1 random firework per online player
        /// </summary>
        [ConsoleCommand("congratsall")]
        private void CCmdCongratsAll(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin)
            {
                arg.ReplyWith("No permission.");
                return;
            }

            DoCongratsAll();

            arg.ReplyWith("[Congratulations] One firework launched for every online player!");
        }

        #endregion

        #region Chat Commands

        /// <summary>
        /// NEW: /congratsall — same as console command but via chat
        /// </summary>
        [ChatCommand("congratsall")]
        private void ChatCongratsAll(BasePlayer player, string command, string[] args)
        {
            if (player == null || !player.IsAdmin)
            {
                player.ChatMessage("<color=#ff5555>Sem permissão, meu lindo.</color>");
                return;
            }

            DoCongratsAll();

            player.ChatMessage("<color=#55ff55>🎆 Foi lançado 1 fogo de artifício em cada player online!</color>");
        }

        #endregion

        #region Player Find Helpers

        private BasePlayer FindPlayerByIdOrName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            if (ulong.TryParse(input, out var steamId))
            {
                var byId = BasePlayer.FindAwakeOrSleeping(steamId.ToString());
                if (byId != null)
                    return byId;
            }

            input = input.ToLower();

            BasePlayer found = null;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.displayName != null && player.displayName.ToLower().Contains(input))
                {
                    found = player;
                    break;
                }
            }

            if (found != null)
                return found;

            foreach (var player in BasePlayer.sleepingPlayerList)
            {
                if (player.displayName != null && player.displayName.ToLower().Contains(input))
                {
                    found = player;
                    break;
                }
            }

            return found;
        }

        #endregion

        #region Fireworks Logic

        /// <summary>
        /// Existing multi-firework circle show around one player
        /// </summary>
        private void FireShowAroundPlayer(BasePlayer player)
        {
            if (player == null || player.transform == null)
                return;

            if (config.FireworkShortnames == null || config.FireworkShortnames.Count == 0)
            {
                Puts("[Congratulations] No fireworks configured. Check the config file.");
                return;
            }

            int count = Mathf.Clamp(config.FireworksCount, 1, 64);
            Vector3 pos = player.transform.position;

            if (config.LogToConsole)
                Puts($"[Congratulations] Spawning {count} fireworks around {player.displayName} at {pos}");

            float angleStep = 360f / count;
            float radius = Mathf.Max(config.RadiusAroundPlayer, 0.1f);

            for (int i = 0; i < count; i++)
            {
                string shortName = config.FireworkShortnames[i % config.FireworkShortnames.Count];

                float angleDeg = i * angleStep;
                float rad = angleDeg * Mathf.Deg2Rad;

                Vector3 spawnPos = pos + new Vector3(
                    Mathf.Cos(rad) * radius,
                    config.HeightOffset,
                    Mathf.Sin(rad) * radius
                );

                SpawnFirework(shortName, spawnPos);
            }
        }

        /// <summary>
        /// NEW core logic — 1 random firework per online player
        /// </summary>
        private void DoCongratsAll()
        {
            if (config.FireworkShortnames == null || config.FireworkShortnames.Count == 0)
            {
                Puts("[Congratulations] No fireworks configured. Check the config file.");
                return;
            }

            int total = 0;

            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player.transform == null)
                    continue;

                string randomShortname = config.FireworkShortnames[
                    UnityEngine.Random.Range(0, config.FireworkShortnames.Count)
                ];

                Vector3 pos = player.transform.position + new Vector3(0f, config.HeightOffset, 0f);

                SpawnFirework(randomShortname, pos);

                total++;
            }

            if (config.LogToConsole)
                Puts($"[Congratulations] CongratsAll launched 1 firework for {total} online players.");
        }

        private void SpawnFirework(string shortName, Vector3 position)
        {
            if (string.IsNullOrEmpty(shortName))
                return;

            ItemDefinition def = ItemManager.FindItemDefinition(shortName);
            if (def == null)
            {
                Puts($"[Congratulations] ItemDefinition not found for shortname '{shortName}'.");
                return;
            }

            var deployable = def.GetComponent<ItemModDeployable>();
            if (deployable == null || deployable.entityPrefab == null)
            {
                Puts($"[Congratulations] Item '{shortName}' is not a deployable firework (no ItemModDeployable).");
                return;
            }

            string prefabPath = deployable.entityPrefab.resourcePath;
            if (string.IsNullOrEmpty(prefabPath))
            {
                Puts($"[Congratulations] Firework '{shortName}' has no valid prefab path.");
                return;
            }

            Quaternion rotation = Quaternion.identity;
            BaseEntity entity = GameManager.server.CreateEntity(prefabPath, position, rotation, true);

            if (entity == null)
            {
                Puts($"[Congratulations] Failed to create entity for '{shortName}' at {position}.");
                return;
            }

            entity.Spawn();

            TryIgniteFirework(entity);

            if (config.LogToConsole)
                Puts($"[Congratulations] Spawned firework '{shortName}' at {position} ({prefabPath}).");
        }

        private void TryIgniteFirework(BaseEntity entity)
        {
            try
            {
                BaseFirework fw = entity.GetComponent<BaseFirework>() ?? entity.GetComponentInChildren<BaseFirework>();
                if (fw == null)
                {
                    if (config.LogToConsole)
                        Puts("[Congratulations] No BaseFirework component found on entity, cannot ignite.");
                    return;
                }

                fw.limitActiveCount = false;
                fw.StaggeredTryLightFuse();
            }
            catch (Exception ex)
            {
                PrintError($"[Congratulations] Error trying to ignite firework: {ex}");
            }
        }

        #endregion
    }
}
