using System;
using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine; // Para o uso correto de comandos no console do Rust

namespace Oxide.Plugins
{
    [Info("AutoBossSpawner", "SeuNome", "1.0.0")]
    [Description("Plugin para spawn automático de bosses a cada 5 horas.")]
    public class AutoBossSpawner : CovalencePlugin
    {
        private List<string> bossCommands = new List<string>
        {
            "rankadm",
            "rankadm"
        };

        private int currentIndex = 0;
        private const float spawnInterval = 5000f; //  horas em segundos
        private const float initialDelay = 300f; //  minutos em segundos
        private const float commandInterval = 3550f; //  segundos entre comandos

        private void OnServerInitialized()
        {
            // Executa os comandos iniciais com um intervalo de 30 segundos
            ExecuteInitialCommands();

            // Agendar os spawns dos bosses a cada 5 horas
            timer.Every(spawnInterval, SpawnNextBoss);
        }

        private void ExecuteInitialCommands()
        {
            for (int i = 0; i < bossCommands.Count; i++)
            {
                int index = i; // Evita capturar a variável de loop no lambda
                timer.Once(index * commandInterval, () => {
                    string command = bossCommands[index];
                    ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
                    Puts($"Executando comando inicial: {command}");
                });
            }
        }

        private void SpawnNextBoss()
        {
            string command = bossCommands[currentIndex];
            ConsoleSystem.Run(ConsoleSystem.Option.Server, command);
            Puts($"Executando comando: {command} (Boss {currentIndex + 1}/{bossCommands.Count})");

            // Atualiza o índice circularmente
            currentIndex = (currentIndex + 1) % bossCommands.Count;
        }
    }
}
