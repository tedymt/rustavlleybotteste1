using System.Collections.Generic;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("EcoPorcent", "Mestre Pardal", "1.0.5")]
    [Description("Gera um data com porcentagem do saldo do Economics para jogadores com permissões específicas.")]
    public class EcoPorcent : CovalencePlugin
    {
        // Permissões
        private const string PermAdmin = "ecoporcent.admin";
        private const string Perm10 = "ecoporcent.10";
        private const string Perm25 = "ecoporcent.25";
        private const string Perm50 = "ecoporcent.50";
        private const string Perm100 = "ecoporcent.100";

        // Datafiles
        private const string EcoPorcentDataFile = "EcoPorcent";
        private const string EconomicsDataFile = "Economics";

        // Estrutura EXACT do Economics.json
        private class EconomicsData
        {
            public Dictionary<string, double> Balances = new Dictionary<string, double>();
        }

        private EconomicsData ecoPorcentData;

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(Perm10, this);
            permission.RegisterPermission(Perm25, this);
            permission.RegisterPermission(Perm50, this);
            permission.RegisterPermission(Perm100, this);

            LoadEcoPorcentData();
        }

        private void LoadEcoPorcentData()
        {
            try
            {
                ecoPorcentData = Interface.Oxide.DataFileSystem.ReadObject<EconomicsData>(EcoPorcentDataFile);
                if (ecoPorcentData == null || ecoPorcentData.Balances == null)
                    throw new System.Exception("EcoPorcent inválido");
            }
            catch
            {
                ecoPorcentData = new EconomicsData();
                Interface.Oxide.DataFileSystem.WriteObject(EcoPorcentDataFile, ecoPorcentData);
                PrintWarning("EcoPorcent.json estava vazio/corrompido. Foi recriado.");
            }
        }

        [Command("ecoporcent")]
        private void CmdEcoPorcent(IPlayer player, string command, string[] args)
        {
            if (!player.IsServer && !player.HasPermission(PermAdmin))
            {
                player.Reply("[EcoPorcent] Você não tem permissão para usar este comando.");
                return;
            }

            EconomicsData ecoData;

            try
            {
                ecoData = Interface.Oxide.DataFileSystem.ReadObject<EconomicsData>(EconomicsDataFile);

                if (ecoData == null || ecoData.Balances == null)
                    throw new System.Exception("Economics.json inválido");
            }
            catch
            {
                player.Reply("[EcoPorcent] Não foi possível ler o Economics.json.");
                return;
            }

            int processed = 0;
            int c10 = 0, c25 = 0, c50 = 0, c100 = 0;

            foreach (var entry in ecoData.Balances)
            {
                string userId = entry.Key;
                double balance = entry.Value;

                double percentValue = 0;

                // Ordem de prioridade: 100 > 50 > 25 > 10
                if (permission.UserHasPermission(userId, Perm100))
                {
                    percentValue = balance * 1.00;
                    c100++;
                }
                else if (permission.UserHasPermission(userId, Perm50))
                {
                    percentValue = balance * 0.50;
                    c50++;
                }
                else if (permission.UserHasPermission(userId, Perm25))
                {
                    percentValue = balance * 0.25;
                    c25++;
                }
                else if (permission.UserHasPermission(userId, Perm10))
                {
                    percentValue = balance * 0.10;
                    c10++;
                }
                else
                {
                    continue;
                }

                ecoPorcentData.Balances[userId] = percentValue;
                processed++;
            }

            Interface.Oxide.DataFileSystem.WriteObject(EcoPorcentDataFile, ecoPorcentData);

            player.Reply(
                $"[EcoPorcent] {processed} players processados.\n" +
                $" - 10%: {c10}\n" +
                $" - 25%: {c25}\n" +
                $" - 50%: {c50}\n" +
                $" - 100%: {c100}"
            );
        }
    }
}
