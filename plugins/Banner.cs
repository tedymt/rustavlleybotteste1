using System;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Banner", "IlIDestroyerIlI", "1.0.12")]
    [Description("Displays a customizable banner on the screen for all players.")]
    public class Banner : CovalencePlugin
    {
        private const string BannerUiName = "Banner_UI";
        private const string TimerBannerUiName = "TimerBanner_UI";
        private ConfigData _config;
        private bool bannerDisplayed = false;
        private bool timerBannerDisplayed = false;
        private string currentBannerText = null;

        private class ConfigData
        {
            public Configuration NormalBanner { get; set; } = new Configuration();
            public TimerConfiguration TimedBanner { get; set; } = new TimerConfiguration();
        }

        private class Configuration
        {
            public string TextColor { get; set; } = "white"; // Color of the text
            public float TextSize { get; set; } = 1.0f; // Size multiplier for the text
            public float BannerLocationY { get; set; } = 0.95f; // Y-position of the top of the banner
            public float BannerLocationX { get; set; } = 0.5f; // X-position for the center of the banner
            public float BannerHeight { get; set; } = 0.03f; // Height of the banner
            public float BannerWidth { get; set; } = 0.5f; // Width of the banner (relative to screen width)
            public string BackgroundColor { get; set; } = "0.0 0.0 0.0 0.7"; // Background color
        }

        private class TimerConfiguration : Configuration
        {
            public int DisplayTime { get; set; } = 10; // Duration to display the banner in seconds
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file with default settings.");
            _config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<ConfigData>() ?? new ConfigData();
            }
            catch (Exception ex)
            {
                PrintError($"Failed to load configuration file: {ex.Message}. Creating a new one.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            try
            {
                Config.WriteObject(_config, true);
            }
            catch (Exception ex)
            {
                PrintError($"Failed to save configuration file: {ex.Message}");
            }
        }

        private void Init()
        {
            permission.RegisterPermission("banner.use", this);
            permission.RegisterPermission("banner.remove", this);
            permission.RegisterPermission("banner.reload", this);
            permission.RegisterPermission("banner.timed", this);

            // Subscribe to player join event
            Subscribe(nameof(OnUserConnected));
        }

        private void OnUserConnected(IPlayer player)
        {
            // If a banner is currently displayed, show it to the newly connected player
            if (bannerDisplayed && !string.IsNullOrEmpty(currentBannerText))
            {
                DisplayBannerForPlayer(player, currentBannerText);
            }
            if (timerBannerDisplayed && !string.IsNullOrEmpty(currentBannerText))
            {
                DisplayTimerBannerForPlayer(player, currentBannerText);
            }
        }

        [Command("banner")]
        private void BannerCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission("banner.use"))
            {
                player.Reply("You do not have permission to use this command.");
                return;
            }

            if (bannerDisplayed)
            {
                player.Reply("A banner is already being displayed. Please remove it before creating a new one.");
                return;
            }

            if (args.Length == 0)
            {
                player.Reply("Usage: /banner <text>");
                return;
            }

            string text = string.Join(" ", args);
            currentBannerText = text;
            DisplayBanner(text);
            player.Reply("Banner displayed.");
            bannerDisplayed = true;
        }

        [Command("timedbanner")]
        private void TimedBannerCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission("banner.timed"))
            {
                player.Reply("You do not have permission to use this command.");
                return;
            }

            if (timerBannerDisplayed)
            {
                player.Reply("A timed banner is already being displayed. Please wait until it disappears.");
                return;
            }

            if (args.Length == 0)
            {
                player.Reply("Usage: /timedbanner <text>");
                return;
            }

            string text = string.Join(" ", args);
            currentBannerText = text;
            DisplayTimerBanner(text);
            player.Reply($"Timed banner displayed for {_config.TimedBanner.DisplayTime} seconds.");
            timerBannerDisplayed = true;
        }

        [Command("bannerremove")]
        private void BannerRemoveCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission("banner.remove"))
            {
                player.Reply("You do not have permission to use this command.");
                return;
            }

            RemoveBanner();
            player.Reply("All banners removed.");
            bannerDisplayed = false;
            timerBannerDisplayed = false;
            currentBannerText = null;
        }

        [Command("bannerreload")]
        private void BannerReloadCommand(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission("banner.reload"))
            {
                player.Reply("You do not have permission to use this command.");
                return;
            }

            LoadConfig();
            player.Reply("Banner configuration reloaded.");
        }

        private void DisplayBanner(string text)
        {
            // Display the banner to all connected players
            foreach (var player in players.Connected)
            {
                DisplayBannerForPlayer(player, text);
            }
        }

        private void DisplayBannerForPlayer(IPlayer player, string text)
        {
            DisplayBannerForPlayer(player, text, _config.NormalBanner, BannerUiName);
        }

        private void DisplayTimerBanner(string text)
        {
            // Display the timed banner to all connected players
            foreach (var player in players.Connected)
            {
                DisplayBannerForPlayer(player, text, _config.TimedBanner, TimerBannerUiName);
            }

            // Set up a timer to remove the banner after DisplayTime seconds
            timer.Once(_config.TimedBanner.DisplayTime, () =>
            {
                RemoveBanner();
                timerBannerDisplayed = false;
                currentBannerText = null;
            });
        }

        private void DisplayTimerBannerForPlayer(IPlayer player, string text)
        {
            DisplayBannerForPlayer(player, text, _config.TimedBanner, TimerBannerUiName);
        }

        private void DisplayBannerForPlayer(IPlayer player, string text, Configuration config, string uiName)
        {
            // Ensure valid UI coordinates based on config values
            float locationY = config.BannerLocationY;
            float locationX = config.BannerLocationX;
            float bannerHeight = config.BannerHeight;
            float bannerWidth = config.BannerWidth;
            float bannerXMin = locationX - (bannerWidth / 2);
            float bannerXMax = locationX + (bannerWidth / 2);

            // Create UI elements for the banner
            var elements = new CuiElementContainer();
            var banner = new CuiElement
            {
                Name = uiName,
                Parent = "Overlay",
                Components =
                {
                    new CuiImageComponent
                    {
                        Color = config.BackgroundColor, // Background color from config
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = $"{bannerXMin} {locationY - bannerHeight}", // Bottom-left corner
                        AnchorMax = $"{bannerXMax} {locationY}" // Top-right corner
                    }
                }
            };

            elements.Add(banner);

            var bannerText = new CuiElement
            {
                Parent = uiName,
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = text,
                        FontSize = Mathf.RoundToInt(20 * config.TextSize),
                        Align = TextAnchor.MiddleCenter,
                        Color = config.TextColor
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    }
                }
            };

            elements.Add(bannerText);

            CuiHelper.AddUi(player.Object as BasePlayer, elements);
        }

        private void RemoveBanner()
        {
            // Remove the banner for all connected players
            foreach (var player in players.Connected)
            {
                CuiHelper.DestroyUi(player.Object as BasePlayer, BannerUiName);
                CuiHelper.DestroyUi(player.Object as BasePlayer, TimerBannerUiName);
            }
            bannerDisplayed = false;
            timerBannerDisplayed = false;
            currentBannerText = null;
        }
    }
}
