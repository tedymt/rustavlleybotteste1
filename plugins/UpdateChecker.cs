using System;
using System.IO;
using Oxide.Core;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using Newtonsoft.Json;
using System.Collections;
using Oxide.Game.Rust.Cui;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Oxide.Plugins
{
    [Info("UpdateChecker", "tofurahie", "4.3.2")]
    [Description("Update checker for all of your plugins")]
    internal class UpdateChecker : RustPlugin
    {
        #region Static

        private const string PERM = "updatechecker.setup";
        private IEnumerator CheckNewVersionCoroutine, SendDiscordMessageCoroutine;
        private bool IsChecking;

        private List<string> ConfigIgnoreList = new()
        {
            "RustEdit"
        };

        #region Classes

        private class Configuration
        {
            [JsonProperty("Command to open UI")]
            public string Command = "ucsetup";

            [JsonProperty("Command to send the test message to your discord")]
            public string CommandDiscord = "uctest";

            [JsonProperty("Console command to check orphaned config files")]
            public string CommandCheckOrphanedConfigFiles = "uc_check_config";

            [JsonProperty("Console command to move orphaned config files")]
            public string CommandDeleteOrphanedConfigFiles = "uc_clean_config";

            [JsonProperty("Discord WebHook")]
            public string DiscordWebHook = "";

            [JsonProperty("Discord message ID")]
            public string MessageID = "";

            [JsonProperty("Check updates on load [disable it if you have problem with the config]")]
            public bool CheckUpdateOnLoad = true;

            [JsonProperty("Embed side line color [hex]")]
            public string EmbedLineColor = "#ffffff";

            [JsonProperty("Difference between UTC and your time [in minutes]")]
            public int UTC = 60;

            [JsonProperty("Use 24 time format")]
            public bool TimeFormat = true;

            [JsonProperty("Check Interval(In minutes)")]
            public int CheckMinutes = 60;

            [JsonProperty("Ignore 'All plugins have the latest version' discord message")]
            public bool IgnoreAllPluginsUpToDateMessage = false;

            [JsonProperty("Ignore not found plugins")]
            public bool IgnoreNotFound = false;

            [JsonProperty("Ignore not loaded plugins")]
            public bool IgnoreNotLoaded = true;

            [JsonProperty("Add a link to the plugin to be updated")]
            public bool UseURL = true;

            [JsonProperty("Enable auto search")]
            public Dictionary<string, bool> AutoSearch = new()
            {
                ["uMod"] = true,
                ["Codefling"] = true,
                ["Lone.Design"] = true,
                ["Chaos"] = true,
                ["RustWorkshop"] = true,
                ["Github"] = true,
                ["ModPulse"] = true,
                ["RustPlugins"] = true,
                ["ServerArmour"] = true,
                ["ImperialPlugins"] = true,
                ["MyVector"] = true,
                ["SkyPlugins"] = true,
                ["Game4Freak"] = true,
            };

            [JsonProperty("List of plugins", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<PluginInfo> ListOfPlugins = new();
        }

        private class PluginInfo
        {
            [JsonIgnore] public bool IsFounded;
            [JsonIgnore] public bool IsLoaded;

            [JsonProperty("Name")]
            public string Name;

            [JsonProperty("Author")]
            public string Author;

            [JsonProperty("Plugin version")]
            public string Version;

            [JsonProperty("Link to plugin")]
            public string Url;

            [JsonProperty("Marketplace")]
            public string Marketplace;

            [JsonProperty("Ignore")]
            public bool Ignore;

            public PluginInfo(string author, string name, string version, string url = "", string marketplace = "", bool ignore = false, bool isLoaded = false)
            {
                Name = name;
                Author = author;
                Version = version;
                Url = url;
                Marketplace = marketplace;
                Ignore = ignore;
                IsLoaded = isLoaded;
            }
        }


        private class RequestData
        {
            public int status;
            public PluginData[] data;
        }

        private class PluginData
        {
            public string name;
            public string manualName;
            public string author;
            public string latestVersion;
            public string url;
            public string slug;
            public string marketplace;
            public string tags;
        }

        private class AboutPlugin
        {
            public string Name;
            public string Author;
            public string Version;
            public bool IsLoaded;

            public AboutPlugin(string name, string author, string version, bool isLoaded)
            {
                Name = name;
                Author = author;
                Version = version;
                IsLoaded = isLoaded;
            }
        }

        #endregion

        #endregion

        #region OxideHooks

        private void OnServerInitialized()
        {
            cmd.AddChatCommand(_config.Command, this, nameof(cmdChatSetup));
            cmd.AddChatCommand(_config.CommandDiscord, this, nameof(cmdDiscordTest));
            cmd.AddConsoleCommand(_config.CommandCheckOrphanedConfigFiles, this, nameof(console_uc_check_config));
            cmd.AddConsoleCommand(_config.CommandDeleteOrphanedConfigFiles, this, nameof(console_uc_clean_config));

            permission.RegisterPermission(PERM, this);

            if (_config.CheckUpdateOnLoad)
                CheckPlugins();

            timer.Every(60 * _config.CheckMinutes, CheckPlugins);
        }

        private void Unload()
        {
            UI.DestroyToAll(".bg");

            if (CheckNewVersionCoroutine != null)
                ServerMgr.Instance.StopCoroutine(CheckNewVersionCoroutine);

            if (SendDiscordMessageCoroutine != null)
                ServerMgr.Instance.StopCoroutine(SendDiscordMessageCoroutine);
        }

        #endregion

        #region Commands

        private void console_uc_check_config(ConsoleSystem.Arg arg)
        {
            if (arg.HasArgs() || arg.Player() != null)
                return;

            UpdatePluginList();

            var configsNames = "";
            foreach (var check in Directory.GetFiles(Interface.Oxide.ConfigDirectory, "*.json").Select(Path.GetFileNameWithoutExtension))
                if (_config.ListOfPlugins.All(x => !string.Equals(x.Name, check, StringComparison.CurrentCultureIgnoreCase)) &&
                    !ConfigIgnoreList.Contains(check, StringComparer.CurrentCultureIgnoreCase))
                    configsNames += check + ", ";

            if (configsNames == string.Empty)
            {
                PrintWarning($"All config files are in use");
                return;
            }

            PrintWarning(configsNames.TrimEnd().TrimEnd(',') +
                         $"\nYou can move orphaned config files to UpdateCheckerConfigsBackup via command \"{_config.CommandDeleteOrphanedConfigFiles}\"");
        }

        private void console_uc_clean_config(ConsoleSystem.Arg arg)
        {
            if (arg.HasArgs() || arg.Player() != null)
                return;

            UpdatePluginList();
            Directory.CreateDirectory(Interface.Oxide.ConfigDirectory + "/UpdateCheckerConfigsBackup");

            var count = 0;
            foreach (var check in Directory.GetFiles(Interface.Oxide.ConfigDirectory, "*.json"))
                if (_config.ListOfPlugins.All(x =>
                        !string.Equals(x.Name, Path.GetFileNameWithoutExtension(check), StringComparison.CurrentCultureIgnoreCase)) &&
                    !ConfigIgnoreList.Contains(Path.GetFileNameWithoutExtension(check), StringComparer.CurrentCultureIgnoreCase))
                {
                    File.Move(check, Interface.Oxide.ConfigDirectory + $"/UpdateCheckerConfigsBackup/{Path.GetFileNameWithoutExtension(check)}.json");
                    count++;
                }


            if (count == 0)
            {
                PrintWarning("All config files are in use");
                return;
            }

            PrintError($"{count} config files ${(count > 1 ? "were" : "was")} moved to {Interface.Oxide.ConfigDirectory + $"/UpdateCheckerConfigsBackup"}");
        }

        private void cmdChatSetup(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PERM))
            {
                SendReply(player, "You don't have permissions to use this command");
                return;
            }

            ShowUIBG(player);
        }

        private void cmdDiscordTest(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin && !permission.UserHasPermission(player.UserIDString, PERM))
            {
                SendReply(player, "You don't have permissions to use this command");
                return;
            }

            var updateList = _config.AutoSearch.ToDictionary(check => check.Key,
                check => (new List<string>(), new List<string> { "Your discord webhook is working" }));
            SendDiscordMessageCoroutine = SendMessageDiscord(updateList);
            ServerMgr.Instance.StartCoroutine(SendDiscordMessageCoroutine);
        }

        [ConsoleCommand("checkupdates")]
        private void cmdConsolecheckupdates(ConsoleSystem.Arg arg)
        {
            if (arg == null || arg.Player() != null)
                return;

            if (IsChecking)
            {
                PrintWarning("The plugin already scans other plugins for updates");
                return;
            }

            CheckPlugins();
        }

        [ConsoleCommand("UI_UC")]
        private void cmdConsoleUI_UC(ConsoleSystem.Arg arg)
        {
            if (!arg.HasArgs())
                return;

            var player = arg.Player();
            switch (arg.GetString(0))
            {
                case "SEARCH":
                    ShowUIPlugins(player, string.Join(" ", arg.Args.Skip(1)).Replace(" ", ""));
                    break;
                case "PAGE":
                    ShowUIPlugins(player, string.Join(" ", arg.Args.Skip(2)), arg.GetInt(1));
                    break;
                case "CHECK":
                    ShowUICurrentPluginInfo(player,
                        _config.ListOfPlugins.FirstOrDefault(x =>
                            string.Equals(x.Name, string.Join(" ", arg.Args.Skip(1)), StringComparison.CurrentCultureIgnoreCase)));
                    break;
                case "CHANGEURL":
                    _config.ListOfPlugins.FirstOrDefault(x => string.Equals(x.Name, arg.GetString(1))).Url = arg.GetString(2);
                    SaveConfig();
                    break;
                case "CHANGEIGNORE":
                    _config.ListOfPlugins.FirstOrDefault(x => string.Equals(x.Name, arg.GetString(1))).Ignore =
                        !_config.ListOfPlugins.FirstOrDefault(x => string.Equals(x.Name, arg.GetString(1))).Ignore;
                    ShowUICurrentPluginInfo(player,
                        _config.ListOfPlugins.FirstOrDefault(x =>
                            string.Equals(x.Name, string.Join(" ", arg.Args.Skip(1)), StringComparison.CurrentCultureIgnoreCase)));
                    SaveConfig();
                    break;
            }
        }

        #endregion

        #region Functions

        private IEnumerator SendMessageDiscord(Dictionary<string, (List<string>, List<string>)> infoList)
        {
            if (string.IsNullOrEmpty(_config.DiscordWebHook))
            {
                IsChecking = false;
                yield break;
            }

            var updateList = infoList.Aggregate("", (current1, checks) => checks.Value.Item2.Aggregate(current1, (current, check) => current + check));
            var message = new
            {
                content = "",
                embeds = new[]
                {
                    new
                    {
                        title = "",
                        description = updateList.Contains("All plugins have the latest version") ? "All plugins have the latest version" : updateList,
                        color = updateList.Contains("All plugins have the latest version") ? 65280
                            : int.Parse(_config.EmbedLineColor.Substring(1), System.Globalization.NumberStyles.HexNumber),
                    }
                }
            };

            var space = "";
            if (updateList.Length < 4000)
            {
                using var request = string.IsNullOrEmpty(_config.MessageID) ? new UnityWebRequest(_config.DiscordWebHook, "POST")
                    : new UnityWebRequest($"{_config.DiscordWebHook}/messages/{_config.MessageID}", "PATCH");
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message)));
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                    PrintError(request.responseCode == 404 ? "Failed to find the message with the specified ID in the config, please check it and try again."
                        : "The discord hook is borken, please check it and try again.");

                IsChecking = false;
                yield break;
            }

            foreach (var check in infoList)
                foreach (var url in check.Value.Item2)
                {
                    space += url;
                    if ((space + url + "\nThe list of updates has reached its limit, please update your plugins.").Length <= 4000)
                        continue;

                    message = new
                    {
                        content = "",
                        embeds = new[]
                        {
                            new
                            {
                                title = "",
                                description = space,
                                color = int.Parse(_config.EmbedLineColor.Substring(1), System.Globalization.NumberStyles.HexNumber),
                            },
                            new
                            {
                                title = "",
                                description = "The list of updates has reached its limit, please update your plugins.",
                                color = 16711680,
                            }
                        }
                    };

                    using var request = string.IsNullOrEmpty(_config.MessageID) ? new UnityWebRequest(_config.DiscordWebHook, "POST")
                        : new UnityWebRequest($"{_config.DiscordWebHook}/messages/{_config.MessageID}", "PATCH");
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message)));
                    request.SetRequestHeader("Content-Type", "application/json");

                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                        PrintError(request.responseCode == 404
                            ? "Failed to find the message with the specified ID in the config, please check it and try again."
                            : "The discord hook is borken, please check it and try again.");

                    yield break;
                }

            IsChecking = false;
        }

        private void CheckPlugins()
        {
            UpdatePluginList();
            SaveConfig();

            CheckNewVersionCoroutine = CheckNewVersion();
            ServerMgr.Instance.StartCoroutine(CheckNewVersionCoroutine);
        }

        private List<AboutPlugin> GetAllPlugins()
        {
            var allFoundedPlugins = new List<AboutPlugin>();
            foreach (var check in plugins.GetAll().Where(x => !x.IsCorePlugin))
                allFoundedPlugins.Add(new AboutPlugin(check.Name, check.Author, check.Version.ToString(), check.IsLoaded));

            foreach (var fileSystemInfo in new DirectoryInfo(Interface.Oxide.PluginDirectory).GetFiles("*" + "cs")
                         .Where(f => (f.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden))
                if (allFoundedPlugins.All(x => x.Name != Path.GetFileNameWithoutExtension(fileSystemInfo.Name)))
                {
                    var readAllText = File.ReadAllText(fileSystemInfo.FullName).Replace(" ", "");
                    var regexInfo = Regex.Match(readAllText, @"Info\(\""(.*?)\"",\""(.*?)\"",\""(.*?)\""");
                    var indexOfRustPlugins = readAllText.IndexOf(":RustPlugin") == -1
                        ? readAllText.IndexOf(":CarbonPlugin") == -1 ? readAllText.IndexOf(":CovalencePlugin") : readAllText.IndexOf(":CarbonPlugin")
                        : readAllText.IndexOf(":RustPlugin");
                    if (indexOfRustPlugins == -1)
                        continue;

                    var firstSub = readAllText.Substring(0, indexOfRustPlugins);
                    var sub = firstSub.Substring(firstSub.LastIndexOf("]", StringComparison.Ordinal));
                    var firstClass = sub.Remove(0, sub.IndexOf("class") + 5);

                    allFoundedPlugins.Add(new AboutPlugin(firstClass, regexInfo.Groups[2].Value, regexInfo.Groups[3].Value, false));
                }

            return allFoundedPlugins;
        }

        private void UpdatePluginList()
        {
            var allPlugins = GetAllPlugins();
            _config.ListOfPlugins.RemoveAll(cfgPlugin => allPlugins.All(serverPlugin => cfgPlugin.Name != serverPlugin.Name));

            foreach (var entry in allPlugins)
            {
                var pluginInfo = _config.ListOfPlugins.FirstOrDefault(x => string.Equals(x.Name, entry.Name, StringComparison.CurrentCultureIgnoreCase));
                if (pluginInfo != null)
                {
                    pluginInfo.Author = entry.Author;
                    pluginInfo.Version = entry.Version;
                    pluginInfo.IsLoaded = entry.IsLoaded;
                    if (entry.Name == "LightsOn")
                        pluginInfo.Url = "https://codefling.com/plugins/lights-on";
                }

                else
                    _config.ListOfPlugins.Add(new PluginInfo(entry.Author, entry.Name, entry.Version, "", "", false, entry.IsLoaded));
            }
        }

        private IEnumerator CheckNewVersion()
        {
            IsChecking = true;

            var updateList = _config.AutoSearch.ToDictionary(check => check.Key, check => (new List<string>(), new List<string>()));
            updateList["uMod"] = (
                new List<string> { $"{DateTime.UtcNow.AddMinutes(_config.UTC).ToString(_config.TimeFormat ? "d MMM yyyy HH:mm" : "d MMM yyyy hh:mm tt")}" },
                new List<string>
                    { $"### {DateTime.UtcNow.AddMinutes(_config.UTC).ToString(_config.TimeFormat ? "d MMM yyyy HH:mm" : "d MMM yyyy hh:mm tt")}" });
            updateList.Add("NotFound", (new List<string> { "\n[NotFound]" }, new List<string> { "\n[NotFound]" }));
            updateList.Add("NotLoaded", (new List<string> { "\n[NotLoaded]" }, new List<string> { "\n[NotLoaded]" }));
            updateList.Add("unknown", (new List<string>(), new List<string>()));

            var isUpToDate = true;

            for (var index = 0; index < _config.ListOfPlugins.Count; index++)
            {
                if (!IsLoaded)
                    yield break;

                var check = _config.ListOfPlugins[index];
                if (check.Ignore)
                    continue;

                if (!check.IsLoaded)
                {
                    updateList["NotLoaded"].Item1[0] += $" {check.Name},";
                    updateList["NotLoaded"].Item2[0] += $" {check.Name},";
                }

                var request = UnityWebRequest.Get(
                    $"https://serverarmour.com/api/v3/marketplace/search?plugin={(string.IsNullOrEmpty(check.Url) ? check.Name : check.Url)}");
                request.SetRequestHeader("User-Agent", $"Update Checker/{Version}");
                yield return request.SendWebRequest();

                if (request.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
                {
                    if (!request.error.Contains("Too Many Requests"))
                    {
                        request.Dispose();
                        continue;
                    }

                    index--;
                    if (request.error.Contains("Too Many Requests"))
                    {
                        PrintError("Waiting 30 seconds for rate limit");
                        request.Dispose();
                        yield return new WaitForSeconds(30f);

                        continue;
                    }

                    request.Dispose();
                    continue;
                }

                var json = JsonConvert.DeserializeObject<RequestData>(request.downloadHandler.text);
                if (json?.data == null || json.status != 200)
                {
                    updateList["NotFound"].Item1[0] += $" {check.Name},";
                    updateList["NotFound"].Item2[0] += $" {check.Name},";
                    continue;
                }

                var foundedPlugins = json.data;
                var orderedFoundedPlugins = foundedPlugins.OrderBy(x => Array.IndexOf(_config.AutoSearch.Keys.ToArray(), x.marketplace))?.ToList();
                foreach (var pluginData in orderedFoundedPlugins)
                {
                    if (!string.IsNullOrEmpty(check.Url) && !check.Url.Contains(pluginData.url) && !pluginData.url.Contains(check.Url))
                        continue;

                    PluginData newPluginData = null;
                    if (string.IsNullOrEmpty(check.Url))
                    {
                        if (_config.AutoSearch.ContainsKey(pluginData.marketplace) && !_config.AutoSearch[pluginData.marketplace])
                            continue;

                        foreach (var subPluginData in orderedFoundedPlugins)
                            if (subPluginData.author != null && subPluginData.author.ToLower().Contains(check.Author.ToLower()) &&
                                (subPluginData.name.ToLower().Replace(" ", "").Contains(check.Name.ToLower()) ||
                                 !string.IsNullOrEmpty(subPluginData.manualName) && subPluginData.manualName.ToLower().Contains(check.Name.ToLower()) ||
                                 !string.IsNullOrEmpty(subPluginData.slug) && subPluginData.slug.ToLower().Replace("-", "").Contains(check.Name.ToLower())))
                            {
                                newPluginData = subPluginData;
                                break;
                            }


                        if (newPluginData == null)
                            foreach (var subPluginData in orderedFoundedPlugins)
                                if (string.Equals(subPluginData.name.Replace(" ", ""), check.Name, StringComparison.CurrentCultureIgnoreCase) ||
                                    !string.IsNullOrEmpty(subPluginData.manualName) && subPluginData.manualName.ToLower().Contains(check.Name.ToLower()) ||
                                    !string.IsNullOrEmpty(subPluginData.slug) && subPluginData.slug.ToLower().Replace("-", "").Contains(check.Name.ToLower()))
                                {
                                    newPluginData = subPluginData;
                                    break;
                                }

                        if (newPluginData == null)
                            foreach (var subPluginData in orderedFoundedPlugins)
                                if (subPluginData.tags != null && subPluginData.tags.Contains(pluginData.name.ToLower()))
                                {
                                    newPluginData = subPluginData;
                                    break;
                                }

                        if (newPluginData == null)
                            continue;
                    }
                    else
                        newPluginData = pluginData;

                    check.IsFounded = true;
                    check.Url = newPluginData.url;
                    check.Marketplace = newPluginData.marketplace;

                    if (string.IsNullOrEmpty(newPluginData.latestVersion) || ParseVersion(newPluginData.latestVersion) <= ParseVersion(check.Version))
                        break;

                    isUpToDate = false;

                    updateList[updateList.ContainsKey(check.Marketplace) ? check.Marketplace : "unknown"].Item1.Add(
                        $"\n[{check.Marketplace}] {check.Name} {check.Version} -> {newPluginData.latestVersion} {(_config.UseURL ? $"{check.Url}" : "")}");
                    updateList[updateList.ContainsKey(check.Marketplace) ? check.Marketplace : "unknown"].Item2.Add(
                        $"\n[{check.Marketplace}] {check.Name} {check.Version} -> [{newPluginData.latestVersion}]({(_config.UseURL ? $"<{check.Url}>" : "")})");
                    break;
                }

                if (check.IsFounded)
                    continue;

                updateList["NotFound"].Item1[0] += $" {check.Name},";
                updateList["NotFound"].Item2[0] += $" {check.Name},";
            }

            updateList["NotFound"].Item1[0] = updateList["NotFound"].Item1[0].TrimEnd(',');
            updateList["NotFound"].Item2[0] = updateList["NotFound"].Item2[0].TrimEnd(',');

            updateList["NotLoaded"].Item1[0] = updateList["NotLoaded"].Item1[0].TrimEnd(',');
            updateList["NotLoaded"].Item2[0] = updateList["NotLoaded"].Item2[0].TrimEnd(',');

            if (_config.IgnoreNotFound || !updateList["NotFound"].Item1[0].Contains(" "))
                updateList.Remove("NotFound");

            if (_config.IgnoreNotLoaded || !updateList["NotLoaded"].Item1[0].Contains(" "))
                updateList.Remove("NotLoaded");

            if (isUpToDate)
            {
                updateList["unknown"].Item1.Add("\nAll plugins have the latest version");
                updateList["unknown"].Item2.Add("\nAll plugins have the latest version");
            }

            if (updateList.Any(x => x.Value.Item2.Count != 0) || (!_config.IgnoreNotFound && updateList["NotFound"].Item2[0].Split(' ').Length != 1))
            {
                PrintWarning(updateList.SelectMany(check => check.Value.Item1).Aggregate("", (current, item) => current + item));

                if (!isUpToDate || (isUpToDate && !_config.IgnoreAllPluginsUpToDateMessage))
                {
                    SendDiscordMessageCoroutine = SendMessageDiscord(updateList);
                    ServerMgr.Instance.StartCoroutine(SendDiscordMessageCoroutine);
                }
            }

            SaveConfig();
        }

        private VersionNumber ParseVersion(string v)
        {
            if (v == string.Empty)
                return new VersionNumber(1, 0, 0);

            var parts = v.Split('.');

            int major;
            if (!int.TryParse(parts[0], out major))
            {
                var majorPart = parts[0];
                var majorString = string.Empty;
                for (var i = majorPart.Length - 1; i >= 0; i--)
                {
                    if (!char.IsDigit(majorPart[i]))
                        break;

                    majorString = majorPart[i] + majorString;
                }

                if (string.IsNullOrEmpty(majorString))
                    return new VersionNumber(1, 0, 0);

                major = int.Parse(majorString);
            }

            if (parts.Length < 2)
                return new VersionNumber(major, 0, 0);

            int minor;
            if (!int.TryParse(parts[1], out minor))
                minor = 0;

            if (parts.Length < 3)
                return new VersionNumber(major, minor, 0);

            int patch;
            if (!int.TryParse(parts[2], out patch))
            {
                var patchPart = parts[2];
                var patchString = string.Empty;
                for (var i = 0; i < patchPart.Length; i++)
                {
                    if (!char.IsDigit(patchPart[i]))
                        break;

                    patchString += patchPart[i];
                }

                if (string.IsNullOrEmpty(patchString))
                    return new VersionNumber(major, minor, 0);

                patch = int.Parse(patchString);
            }

            return new VersionNumber(major, minor, patch);
        }

        #endregion

        #region UI

        private void ShowUICurrentPluginInfo(BasePlayer player, PluginInfo pluginInfo)
        {
            var container = new CuiElementContainer();

            UI.Input(ref container, ".current.plugin.name", ".current.plugin.name.input", ".current.plugin.name.input", oMin: "5 0", oMax: "-5 0",
                text: $"{pluginInfo.Name}", fontSize: 18, color: "1 1 1 1", align: TextAnchor.MiddleLeft, readOnly: true);

            UI.Input(ref container, ".current.plugin.author", ".current.plugin.author.input", ".current.plugin.author.input", oMin: "5 0", oMax: "-5 0",
                text: $"{pluginInfo.Author}", fontSize: 18, color: "1 1 1 1", align: TextAnchor.MiddleLeft, readOnly: true);

            UI.Input(ref container, ".current.plugin.version", ".current.plugin.version.input", ".current.plugin.version.input", oMin: "5 0", oMax: "-5 0",
                text: $"{pluginInfo.Version}", fontSize: 18, color: "1 1 1 1", align: TextAnchor.MiddleLeft, readOnly: true);

            UI.Input(ref container, ".current.plugin.url", ".current.plugin.url.input", ".current.plugin.url.input", oMin: "5 0", oMax: "-5 0",
                text: $"{pluginInfo.Url}", fontSize: 18, color: "1 1 1 1", align: TextAnchor.MiddleLeft, command: $"UI_UC CHANGEURL {pluginInfo.Name}",
                limit: 200);

            UI.Input(ref container, ".current.plugin.marketplace", ".current.plugin.marketplace.input", ".current.plugin.marketplace.input", oMin: "5 0",
                oMax: "-5 0", text: $"{pluginInfo.Marketplace}", fontSize: 18, color: "1 1 1 1", align: TextAnchor.MiddleLeft, readOnly: true);

            UI.Button(ref container, ".current.plugin.bg", ".current.plugin.ignore", ".current.plugin.ignore", aMin: "0 1", aMax: "1 1", oMin: "5 -175",
                oMax: "-5 -155", command: $"UI_UC CHANGEIGNORE {pluginInfo.Name}",
                text: $"Ignore the plugin <color={(pluginInfo.Ignore ? "green" : "red")}>{pluginInfo.Ignore}</color>", align: TextAnchor.MiddleLeft);

            UI.Create(player, container);

        }

        private void ShowUIPlugins(BasePlayer player, string search = "", int page = 0)
        {
            var container = new CuiElementContainer();

            UI.Panel(ref container, ".plugins.bg", ".plugins", ".plugins", bgColor: "0 0 0 0");

            var searchSort =
                (!string.IsNullOrEmpty(search)
                    ? _config.ListOfPlugins.Where(x => x.Name.ToLower().Contains(search.ToLower()) || x.Author.ToLower().Contains(search.ToLower()))
                    : _config.ListOfPlugins).OrderByDescending(x => x.IsFounded).ThenByDescending(x => x.Ignore);

            var posX = 8;
            var posY = -70;
            foreach (var check in searchSort.Skip(page * 63).Take(63))
            {
                UI.Button(ref container, ".plugins", ".plugin" + posX + posY, ".plugin" + posX + posY, "0 1", "0 1", $"{posX} {posY}",
                    $"{posX + 108} {posY + 45}",
                    $"<color=#{(check.Ignore ? "FFA500" : !check.IsFounded ? "FF0000" : "06c258")}>{check.Name}</color>\n<size=8>[{check.Author}]</size>", 12,
                    command: $"UI_UC CHECK {check.Name}");

                posX += 113;
                if (posX < 690)
                    continue;

                posX = 8;
                posY -= 45;
            }

            if (page > 0)
                UI.Button(ref container, ".plugins", ".previous", ".previous", "0.5 0", "0.5 0", "-25 5", "-5 25", "<",
                    command: $"UI_UC PAGE {page - 1} {search}");

            UI.Label(ref container, ".plugins", ".page", ".page", "0.5 0", "0.5 0", "-5 5", "5 25", $"{page + 1}");

            if (page + 1 < _config.ListOfPlugins.Count / 63f)
                UI.Button(ref container, ".plugins", ".next", ".next", "0.5 0", "0.5 0", "5 5", "25 25", ">", command: $"UI_UC PAGE {page + 1} {search}");

            UI.Create(player, container);
        }

        private void ShowUIBG(BasePlayer player)
        {
            var container = new CuiElementContainer();

            UI.MainParent(ref container);

            UI.Button(ref container, ".bg", ".bg", oMin: "-640 -360", oMax: "640 360", material: "assets/content/ui/uibackgroundblur-ingamemenu.mat",
                bgColor: "0.13 0.13 0.13 0.85");

            UI.Panel(ref container, ".bg", ".plugins.bg", oMin: "-400 -325", oMax: "400 125", material: "assets/content/ui/binocular_overlay.mat",
                bgColor: "0.25 0.25 0.25 0.95");


            UI.Panel(ref container, ".bg", ".search.bg", oMin: "-250 105", oMax: "250 145", material: "assets/content/ui/binocular_overlay.mat",
                bgColor: "0.15 0.15 0.15 0.95");

            UI.Label(ref container, ".search.bg", oMin: "10 0", text: "Search:", color: "0.85 0.85 0.85 0.95", align: TextAnchor.MiddleLeft);

            UI.Panel(ref container, ".search.bg", ".search.input", oMin: "65 5", oMax: "-10 -5", material: "assets/content/ui/uibackgroundblur-notice.mat",
                bgColor: "0.75 0.75 0.75 0.6");

            UI.Input(ref container, ".search.input", oMin: "5 0", oMax: "-5 0", text: "", fontSize: 18, color: "1 1 1 1", autoFocus: true,
                command: "UI_UC SEARCH");


            UI.Panel(ref container, ".bg", ".current.plugin.bg", oMin: "-250 160", oMax: "250 339", material: "assets/content/ui/binocular_overlay.mat",
                bgColor: "0.25 0.25 0.25 0.95");

            UI.Label(ref container, ".current.plugin.bg", aMin: "0 1", aMax: "1 1", oMin: "5 -30", oMax: "0 -5", text: "Name:", align: TextAnchor.MiddleLeft);

            UI.Panel(ref container, ".current.plugin.bg", ".current.plugin.name", aMin: "0 1", aMax: "1 1", oMin: "95 -30", oMax: "-5 -5",
                bgColor: "0.85 0.85 0.85 0.25");

            UI.Label(ref container, ".current.plugin.bg", aMin: "0 1", aMax: "1 1", oMin: "5 -60", oMax: "0 -35", text: "Author:",
                align: TextAnchor.MiddleLeft);

            UI.Panel(ref container, ".current.plugin.bg", ".current.plugin.author", aMin: "0 1", aMax: "1 1", oMin: "95 -60", oMax: "-5 -35",
                bgColor: "0.85 0.85 0.85 0.25");

            UI.Label(ref container, ".current.plugin.bg", aMin: "0 1", aMax: "1 1", oMin: "5 -90", oMax: "0 -65", text: "Version:",
                align: TextAnchor.MiddleLeft);

            UI.Panel(ref container, ".current.plugin.bg", ".current.plugin.version", aMin: "0 1", aMax: "1 1", oMin: "95 -90", oMax: "-5 -65",
                bgColor: "0.85 0.85 0.85 0.25");

            UI.Label(ref container, ".current.plugin.bg", aMin: "0 1", aMax: "1 1", oMin: "5 -120", oMax: "0 -95", text: "URL*:", align: TextAnchor.MiddleLeft);

            UI.Panel(ref container, ".current.plugin.bg", ".current.plugin.url", aMin: "0 1", aMax: "1 1", oMin: "95 -120", oMax: "-5 -95",
                bgColor: "0.85 0.85 0.85 0.65");

            UI.Label(ref container, ".current.plugin.bg", aMin: "0 1", aMax: "1 1", oMin: "5 -150", oMax: "0 -125", text: "Marketplace:",
                align: TextAnchor.MiddleLeft);

            UI.Panel(ref container, ".current.plugin.bg", ".current.plugin.marketplace", aMin: "0 1", aMax: "1 1", oMin: "95 -150", oMax: "-5 -125",
                bgColor: "0.85 0.85 0.85 0.25");

            UI.Create(player, container);

            ShowUIPlugins(player);
            ShowUICurrentPluginInfo(player, _config.ListOfPlugins.OrderByDescending(x => x.IsFounded).ThenByDescending(x => x.Ignore).First());
        }

        #endregion

        #region Config

        private Configuration _config;

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<Configuration>();
                if (_config == null)
                    throw new Exception();

                SaveConfig();
            }
            catch
            {
                PrintError("Your configuration file contains an error. Using default configuration values.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config);

        protected override void LoadDefaultConfig() => _config = new Configuration();

        #endregion

        #region GUI

        private class UI
        {
            private const string Layer = "UI_UpdateChecker";

            public static void MainParent(ref CuiElementContainer container, string name = null, string aMin = "0.5 0.5", string aMax = "0.5 0.5",
                bool overAll = true, bool keyboardEnabled = true, bool cursorEnabled = true) => container.Add(new CuiPanel
            {
                KeyboardEnabled = keyboardEnabled,
                CursorEnabled = cursorEnabled,
                RectTransform = { AnchorMin = aMin, AnchorMax = aMax },
                Image = { Color = "0 0 0 0" }
            }, overAll ? "Overlay" : "Hud", Layer + ".bg" + name, Layer + ".bg" + name);

            public static void Panel(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
                string aMax = "1 1", string oMin = "0 0", string oMax = "0 0", string bgColor = "0.33 0.33 0.33 1", string material = null,
                string sprite = null, int itemID = 0, ulong skinID = 0) => container.Add(new CuiElement
            {
                Parent = Layer + parent,
                Name = name != null && name.Contains(".other.") ? name.Replace(".other.", "") : Layer + name,
                DestroyUi = destroy == null ? null : Layer + destroy,
                Components =
                {
                    new CuiRectTransformComponent { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax },
                    new CuiImageComponent { Color = HexToRustFormat(bgColor), Material = material, Sprite = sprite, ItemId = itemID, SkinId = skinID },
                },
            });

            public static void Icon(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
                string aMax = "1 1", string oMin = "0 0", string oMax = "0 0", int itemID = 0, ulong skinID = 0) => container.Add(new CuiElement
            {
                Parent = Layer + parent,
                Name = Layer + name,
                DestroyUi = destroy == null ? null : Layer + destroy,
                Components =
                {
                    new CuiRectTransformComponent { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax },
                    new CuiImageComponent { ItemId = itemID, SkinId = skinID },
                },
            });

            public static void Image(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
                string aMax = "1 1", string oMin = "0 0", string oMax = "0 0", string image = "", string color = "1 1 1 1") => container.Add(new CuiElement
            {
                Parent = Layer + parent,
                Name = Layer + name,
                DestroyUi = destroy == null ? null : Layer + destroy,
                Components =
                {
                    new CuiRectTransformComponent { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax },
                    new CuiRawImageComponent
                    {
                        Png = !image.StartsWith("http") && !image.StartsWith("www") ? image : null,
                        Url = image.StartsWith("http") || image.StartsWith("www") ? image : null, Color = HexToRustFormat(color),
                        Sprite = "assets/content/textures/generic/fulltransparent.tga"
                    },
                },
            });

            public static void Label(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
                string aMax = "1 1", string oMin = "0 0", string oMax = "0 0", string text = null, int fontSize = 16, string color = "1 1 1 1",
                TextAnchor align = TextAnchor.MiddleCenter, string outlineDistance = null, string outlineColor = "0 0 0 1",
                VerticalWrapMode wrapMode = VerticalWrapMode.Truncate, string font = "robotocondensed-regular.ttf") => container.Add(new CuiElement
            {
                Parent = Layer + parent,
                Name = Layer + name,
                DestroyUi = destroy == null ? null : Layer + destroy,
                Components =
                {
                    new CuiRectTransformComponent { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax },
                    new CuiTextComponent
                        { Text = text, FontSize = fontSize, Color = HexToRustFormat(color), Align = align, Font = font, VerticalOverflow = wrapMode },
                    outlineDistance == null ? new CuiOutlineComponent { Distance = "0 0", Color = "0 0 0 0" } : new CuiOutlineComponent
                        { Distance = outlineDistance, Color = HexToRustFormat(outlineColor) },
                },
            });

            public static void Button(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
                string aMax = "1 1", string oMin = "0 0", string oMax = "0 0", string text = null, int fontSize = 16, string color = "1 1 1 1",
                string command = null, string bgColor = "0 0 0 0", VerticalWrapMode wrapMode = VerticalWrapMode.Truncate,
                TextAnchor align = TextAnchor.MiddleCenter, string font = "robotocondensed-regular.ttf", string material = null, string sprite = null) =>
                container.Add(new CuiButton
                {
                    RectTransform = { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax },
                    Text = { Text = text, FontSize = fontSize, Color = HexToRustFormat(color), Align = align, Font = font, VerticalOverflow = wrapMode },
                    Button =
                    {
                        Command = command, Close = command == null ? Layer + name : null, Color = HexToRustFormat(bgColor), Material = material, Sprite = sprite
                    }
                }, Layer + parent, command == null ? null : Layer + name, destroy == null ? null : Layer + destroy);

            public static void Input(ref CuiElementContainer container, string parent, string name = null, string destroy = null, string aMin = "0 0",
                string aMax = "1 1", string oMin = "0 0", string oMax = "0 0", string text = null, int limit = 40, int fontSize = 16, string color = "1 1 1 1",
                string command = null, TextAnchor align = TextAnchor.MiddleCenter, bool autoFocus = false, bool hudMenuInput = false, bool readOnly = false,
                bool isPassword = false, bool needsKeyboard = false, bool singleLine = true, string font = "robotocondensed-regular.ttf") => container.Add(
                new CuiElement
                {
                    Parent = Layer + parent,
                    Name = Layer + name,
                    DestroyUi = destroy == null ? null : Layer + destroy,
                    Components =
                    {
                        new CuiRectTransformComponent { AnchorMin = aMin, AnchorMax = aMax, OffsetMin = oMin, OffsetMax = oMax },
                        new CuiInputFieldComponent
                        {
                            Text = text, Command = command, CharsLimit = limit, FontSize = fontSize, Color = HexToRustFormat(color), Align = align, Font = font,
                            Autofocus = autoFocus, IsPassword = isPassword, ReadOnly = readOnly, HudMenuInput = hudMenuInput, NeedsKeyboard = needsKeyboard,
                            LineType = singleLine ? InputField.LineType.SingleLine : InputField.LineType.MultiLineNewline
                        },
                    }
                });

            public static void Outline(ref CuiElementContainer container, string layer, string size = "1 1 1 1", string color = "0 0 0 1",
                bool external = false)
            {
                var borders = size.Split(' ');

                if (borders[0] != "0")
                    Panel(ref container, layer, aMin: "0 1", aMax: "1 1", oMin: $"-{borders[0]} {(external ? "0" : "-" + borders[0])}",
                        oMax: $"{borders[0]} {(external ? borders[0] : "0")}", bgColor: color);
                if (borders[1] != "0")
                    Panel(ref container, layer, aMin: "1 0", aMax: "1 1", oMin: $"{(external ? "0" : "-" + borders[1])} -{borders[1]}",
                        oMax: $"{(external ? borders[1] : "0")} {borders[1]}", bgColor: color);
                if (borders[2] != "0")
                    Panel(ref container, layer, aMin: "0 0", aMax: "1 0", oMin: $"-{borders[2]} {(external ? "-" + borders[2] : "0")}",
                        oMax: $"{borders[2]} {(external ? "0" : borders[2])}", bgColor: color);
                if (borders[3] != "0")
                    Panel(ref container, layer, aMin: "0 0", aMax: "0 1", oMin: $"{(external ? "-" + borders[3] : "0")} -{borders[3]}",
                        oMax: $"{(external ? "0" : borders[3])} {borders[3]}", bgColor: color);
            }

            public static string HexToRustFormat(string hex)
            {
                if (string.IsNullOrEmpty(hex))
                    return hex;

                Color color;

                if (hex.Contains(":"))
                    return ColorUtility.TryParseHtmlString(hex.Substring(0, hex.IndexOf(":")), out color)
                        ? $"{color.r:F2} {color.g:F2} {color.b:F2} {hex.Substring(hex.IndexOf(":") + 1, hex.Length - hex.IndexOf(":") - 1)}" : hex;

                return ColorUtility.TryParseHtmlString(hex, out color) ? $"{color.r:F2} {color.g:F2} {color.b:F2} {color.a:F2}" : hex;
            }

            public static void Create(BasePlayer player, CuiElementContainer container)
            {
                CuiHelper.AddUi(player, container);
            }

            public static void CreateToAll(CuiElementContainer container, string layer)
            {
                foreach (var player in BasePlayer.activePlayerList)
                    CuiHelper.AddUi(player, container);
            }

            public static void Destroy(BasePlayer player, string layer) => CuiHelper.DestroyUi(player, Layer + layer);

            public static void DestroyToAll(string layer)
            {
                foreach (var player in BasePlayer.activePlayerList)
                    Destroy(player, layer);
            }
        }

        #endregion
    }
} 