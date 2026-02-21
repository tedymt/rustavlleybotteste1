using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("TopMoney", "Mestre Pardal", "2.1.0")]
    [Description("Exibe um ranking baseado no saldo do plugin Economics, de forma instantânea e sem salvar o servidor, com quantidade flexível.")]
    public class TopMoney : RustPlugin
    {
        [PluginReference]
        private Plugin Economics;

        private const string PermUse = "topmoney.use";
        private const int DefaultTopAmount = 10;

        private void Init()
        {
            permission.RegisterPermission(PermUse, this);
        }

        [ChatCommand("rank")]
        private void CmdTopMoney(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PermUse))
            {
                SendReply(player, "<color=red>Você não tem permissão para usar este comando.</color>");
                return;
            }

            int amount = DefaultTopAmount;
            if (args.Length > 0 && int.TryParse(args[0], out int parsedAmount))
            {
                amount = Math.Clamp(parsedAmount, 1, 100);
            }

            ShowRanking(player, false, false, amount);
        }

        [ConsoleCommand("rankadm")]
        private void CmdTopMoneyAdminConsole(ConsoleSystem.Arg arg)
        {
            Puts("Exibindo o ranking via console admin...");
            ShowRanking(null, true, true, DefaultTopAmount);
        }

        private void ShowRanking(BasePlayer player, bool isAdmin, bool broadcast, int topAmount)
        {
            if (Economics == null)
            {
                string erro = "<color=red>Plugin Economics não encontrado ou não carregado.</color>";
                if (player != null) SendReply(player, erro);
                else Puts(erro);
                return;
            }

            var balances = new Dictionary<ulong, double>();

            foreach (var user in covalence.Players.All)
            {
                if (user == null) continue;
                if (!ulong.TryParse(user.Id, out ulong userID)) continue;

                double balance = (double)(Economics.Call("Balance", user.Id) ?? 0.0);
                balances[userID] = balance;
            }

            if (balances.Count == 0)
            {
                string erro = "<color=red>Nenhuma informação financeira encontrada.</color>";
                if (player != null) SendReply(player, erro);
                else Puts(erro);
                return;
            }

            var sortedData = balances
                .OrderByDescending(entry => entry.Value)
                .Take(topAmount);

            int rank = 1;
            string message = $"<size=18><color=#00BFFF>:discord.trophy: Top {topAmount} dos Mais Ricos :discord.trophy:</color></size>\n";

            foreach (var entry in sortedData)
            {
                string playerName = covalence.Players.FindPlayerById(entry.Key.ToString())?.Name ?? "Desconhecido";
                string formattedBalance = entry.Value.ToString("N2", CultureInfo.InvariantCulture)
                    .Replace(",", "@").Replace(".", ",").Replace("@", ".");

                string color = rank == 1 ? "#00FF00" : rank == 2 ? "#FFFF00" : rank == 3 ? "#FFA500" : "#FF0000";
                message += $"<color={color}>{rank}º - {playerName} - $ {formattedBalance}</color>\n";
                rank++;
            }

            if (broadcast)
            {
                PrintToChat(message);
            }
            else if (player != null)
            {
                SendReply(player, message);
            }
            else
            {
                Puts(message);
            }
        }
    }
}
