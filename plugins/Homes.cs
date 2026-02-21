using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("Homes", "Chris", "1.0.7")]
    public class Homes : RustPlugin
    {
        [PluginReference] private Plugin NTeleportation;

        void OnServerInitialized()
        {
            if (config.perm)
            {
                permission.RegisterPermission("homes.use", this);
            }

        }

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, "homes_background");
                CuiHelper.DestroyUi(player, "newhome_blurr");
                CuiHelper.DestroyUi(player, "usure_blurr");
            }
        }

        [ConsoleCommand("teleporthome")]
        private void storemenuvalue(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;
            if (args.Length < 1) return;
            player.SendConsoleCommand($"chat.say \"/home {args[0].Replace("%", " ")}\" ");
        }


        void OnPlayerConnected(BasePlayer player)
        {   
            
            if (config.homesbtn){
                
                //plugin BUTTON
                var container = new CuiElementContainer();

                CuiHelper.DestroyUi(player, "homesbtn");
            
                container.Add(new CuiButton
                {
                    Button = { Close = "", Command = "chat.say /homes", Color = "1 1 1 0.1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                    RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1" , OffsetMin = "165 -37.5", OffsetMax = "240 -17.5" },
                    Text = { Text = "HOMES", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                    
                }, "Inventory", "homesbtn");


                CuiHelper.DestroyUi(player, "homesbtn");
                CuiHelper.AddUi(player, container);
                
            }
            
        }


        void Stockscreen(BasePlayer player)
        {
            var container = new CuiElementContainer();
            container.Add(new CuiPanel
            {
                Image = { Color = "0.70 0.67 0.65 0.3", Material = "assets/content/ui/uibackgroundblur.mat" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
                
            },
            "Overlay",
            "homes_background");
            ///////////////////////////////////////
            /*
            CuiHelper.DestroyUi(player, "homesbtn");
            
            container.Add(new CuiButton
            {
                Button = { Close = "", Command = "chat.say /homes", Color = "1 1 1 0.1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0 1", AnchorMax = "0 1" , OffsetMin = "165 -37.5", OffsetMax = "240 -17.5" },
                Text = { Text = "HOMES", FontSize = 9, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                    
            }, "Inventory", "homesbtn");
            */
            ///////////////////////////////////////
            container.Add(new CuiElement
            {
                Parent = "homes_background",
                Name = "extrablurr",
                Components =
                 {
                    new CuiImageComponent { Material = "assets/icons/iconmaterial.mat", Sprite = "assets/content/ui/ui.background.transparent.radial.psd", Color = "0 0 0 0.93", FadeIn = 0.3f},
                    new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                 },
                FadeOut = 0f
            });

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-680 -360", OffsetMax = "680 360" },
                CursorEnabled = true
                
            },
            "homes_background",
            "homes_background_offset");



            container.Add(new CuiPanel
            {
                Image = { Color = "0.18 0.18 0.18 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = "0.35 0.25", AnchorMax = "0.65 0.75" },
                CursorEnabled = false
                
            },
            "homes_background_offset",
            "mainbckg");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.36 0.35 0.33 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = "0.02 0.15", AnchorMax = "0.98 0.90" }
            },
            "mainbckg",
            "maininside");

            container.Add(new CuiButton
            {
                Button = { Close = "", Command = "", Color = "0 0 0 0", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.03 0.915", AnchorMax = "0.10 0.985" },
                //Text = { Text = "H", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 0.2 0 1", Font = "robotocondensed-regular.ttf"}
                
            }, "mainbckg", "homeimg");
            
            container.Add(new CuiElement
            {
                Parent = "homeimg",
                Components =
                {   
                    new CuiRawImageComponent { Url = "https://i.postimg.cc/2Sp6QWq8/home-custom.png", Sprite = "assets/content/textures/generic/fulltransparent.tga"},
                    new CuiRectTransformComponent { AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.9" }
                }
            });
            
            //EXTRA BUTTOONS
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = "0 -0.2", AnchorMax = "0.998 0" }
            },
            "mainbckg",
            "extrabtns");

            if (config.outpostbtn) {
                container.Add(new CuiButton
                {
                    Button = { Close = "homes_background", Command = "chat.say /outpost", Color = "0.18 0.18 0.18 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                    RectTransform = { AnchorMin = "0.02 0.55", AnchorMax = "0.49 0.90" },
                    Text = { Text = "TP Outpost", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                    
                }, "extrabtns", "tpoutpost");
            }

            if (config.banditbtn) {
                container.Add(new CuiButton
                {
                    Button = { Close = "homes_background", Command = "chat.say /bandit", Color = "0.18 0.18 0.18 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                    RectTransform = { AnchorMin = "0.51 0.55", AnchorMax = "0.98 0.90" },
                    Text = { Text = "TP Bandit", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                    
                }, "extrabtns", "tpbandit");
            }

            if (config.townbtn) {
                container.Add(new CuiButton
                {
                    Button = { Close = "homes_background", Command = "chat.say /town", Color = "0.18 0.18 0.18 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                    RectTransform = { AnchorMin = "0.02 0.10", AnchorMax = "0.49 0.45" },
                    Text = { Text = "TP Town", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                    
                }, "extrabtns", "townbtn");
            }
            
            



            container.Add(new CuiElement
            {
                Parent = "mainbckg",
                Name = "title",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "MY HOMES",
                        FontSize = 13,
                        Font = "robotocondensed-regular.ttf",
                        Align = TextAnchor.MiddleLeft,
                        Color = "1 1 1 1",                            
                        FadeIn = 0f,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.12 0.9",
                        AnchorMax = "0.95 1"
                    }

                },
            });

            container.Add(new CuiButton
            {
                Button = { Close = "homes_background", Command = "", Color = "0 0 0 0", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.89 0.91", AnchorMax = "0.97 0.99" },
                Text = { Text = "", FontSize = 13, Align = TextAnchor.MiddleRight, Color = "1 0.2 0 1", Font = "robotocondensed-regular.ttf"}
                
            }, "mainbckg", "closebtn");

            container.Add(new CuiElement
            {
                Parent = "closebtn",
                Name = "closebtn_icon",
                Components =
                 {
                    new CuiImageComponent { Material = "assets/icons/iconmaterial.mat", Sprite = "assets/icons/vote_down.png", Color = "1 1 1 1", FadeIn = 0.3f},
                    new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1"}
                 },
                FadeOut = 0f
            });




            //  COLDOWNS
            // 1
            container.Add(new CuiPanel
            {
                Image = { Color = "0.42 0.51 0.22 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.32 0.07" }
            },
            "mainbckg",
            "home_cooldown_panel");

            container.Add(new CuiElement
            {
                Parent = "home_cooldown_panel",
                Name = "cd1text",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "HOME COOLDOWN\n",
                        FontSize = 11,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 0.4",                            
                        FadeIn = 0f,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 2"
                    }

                },
            });
            
            // 2
            container.Add(new CuiPanel
            {
                Image = { Color = "0.42 0.51 0.22 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat" },
                RectTransform = { AnchorMin = "0.35 0.02", AnchorMax = "0.65 0.07" }
            },
            "mainbckg",
            "town_cooldown_panel");

            container.Add(new CuiElement
            {
                Parent = "town_cooldown_panel",
                Name = "cd2text",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "TOWN COOLDOWN\n",
                        FontSize = 11,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 0.4",                            
                        FadeIn = 0f,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 2"
                    }

                },
            });

            // 3
            container.Add(new CuiPanel
            {
                Image = { Color = "0.42 0.51 0.22 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.68 0.02", AnchorMax = "0.98 0.07" }
            },
            "mainbckg",
            "tpr_cooldown_panel");

            container.Add(new CuiElement
            {
                Parent = "tpr_cooldown_panel",
                Name = "cd3text",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "TPR COOLDOWN\n",
                        FontSize = 11,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 0.4",                            
                        FadeIn = 0f,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 1",
                        AnchorMax = "1 2"
                    }

                },
            });

            
            CuiHelper.DestroyUi(player, "mainbckg");
            CuiHelper.AddUi(player, container);
        }



        void Mainscreen(BasePlayer player)
        {
            var container = new CuiElementContainer();


            // ADD HOME
            container.Add(new CuiButton
            {                                                //"0.55 0.53 0.51 1"
                Button = { Close = "", Command = "home_new_request addpopup", Color = "0.17 0.17 0.17 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.02 0.9", AnchorMax = "0.49 0.975" },
                Text = { Text = "Add home".ToUpper(), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "maininside", "addhome");

            container.Add(new CuiElement
            {
                Parent = "addhome",
                Components =
                {   
                    new CuiRawImageComponent { Url = "https://i.postimg.cc/WpDZCphw/home-plus-outline-custom.png", Sprite = "assets/content/textures/generic/fulltransparent.tga"},
                    new CuiRectTransformComponent { AnchorMin = "0.02 0.05", AnchorMax = "0.13 0.95" }
                }
            });



            // REQUEST TELEPORT
            container.Add(new CuiButton
            {
                Button = { Close = "", Command = "home_new_request requesttp", Color = "0.17 0.17 0.17 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.51 0.9", AnchorMax = "0.98 0.975" },
                Text = { Text = "Request teleport".ToUpper(), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "maininside", "reqtele");

            container.Add(new CuiElement
            {
                Parent = "reqtele",
                Components =
                {   
                    new CuiRawImageComponent { Url = "https://i.postimg.cc/4dz3R3hm/account-arrow-right-outline-custom.png", Sprite = "assets/content/textures/generic/fulltransparent.tga"},
                    new CuiRectTransformComponent { AnchorMin = "0.02 0.05", AnchorMax = "0.13 0.95" }
                }
            });

            CuiHelper.AddUi(player, container);
        }

            

        List<string> grid = new List<string>()
        {
            "0.02 0.80-0.98 0.975",
            "0.02 0.60-0.98 0.775",
            "0.02 0.40-0.98 0.575",
            "0.02 0.20-0.98 0.375",
            "0.02 0.80-0.98 0.975",
            "0.02 0.60-0.98 0.775",
            "0.02 0.40-0.98 0.575",
            "0.02 0.20-0.98 0.375",
        };

        [ConsoleCommand("homes_page")]
        private void homes_pages(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;
            if (args.Length < 1) return;

            PopulateGrid(player, int.Parse(args[0]));
        }

        void PopulateGrid(BasePlayer player, int page = 0)
        {
            var homes = NTeleportation.Call<Dictionary<string, Vector3>>("API_GetHomes", player);
            var homesNames = homes.Keys.ToList();

            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.9" }
            },
            
            "maininside",
            "homes_grid");

            CuiHelper.DestroyUi(player, "homes_grid");
            CuiHelper.AddUi(player, container);
            
            for (int i = 0; i < 4; i++)
            {   
                if (homesNames.Count <= i + page * 4) break;

                string[] anchors = grid[i].Split('-');
                string name = homesNames[i + page * 4];

                //
                //
                float distance = Vector3.Distance(player.transform.position, homes[name]) * 1.01f;
                distance = (float)Math.Round(distance, 2);
                int usesLeft = NTeleportation.Call<int>("GetLimitRemaining", player, "home");
                int cooldown = NTeleportation.Call<int>("GetCooldownRemaining", player, "home");
                CuiHelper.AddUi(player, 
                    HomeCard(name, homes[name], anchors, distance, usesLeft)
                );
                
            }

            
            if (homesNames.Count > page * 4 + 4)
            {
                var c = new CuiElementContainer();
                c.Add(new CuiButton
                    {
                        Button = { Close = "", Command = $"homes_page {page + 1}", Color = "0.27 0.65 1 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                        RectTransform = { AnchorMin = "0.8 0.02", AnchorMax = "0.98 0.075" },
                        Text = { Text = "NEXT", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf"}
                    
                    }, "homes_grid", "nextpagebutton");
                CuiHelper.AddUi(player, c);
            }

            if (page > 0)
            {
                var d = new CuiElementContainer();
                d.Add(new CuiButton
                    {
                        Button = { Close = "", Command = $"homes_page {page - 1}", Color = "0.27 0.65 1 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                        RectTransform = { AnchorMin = "0.02 0.02", AnchorMax = "0.2 0.075" },
                        Text = { Text = "PREV", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-bold.ttf"}
                    
                    }, "homes_grid", "prevpagebutton");
                CuiHelper.AddUi(player, d);
            }
        }

        string HomeCard(string name, Vector3 pos, string[] anchors, float distance, int usesLeft)
        {   
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
                {
                    Image = { Color = "0.18 0.18 0.18 1" },
                    RectTransform = { AnchorMin = anchors[0], AnchorMax = anchors[1] }
                },
                "homes_grid",
                $"home_{name}");
                
                container.Add(new CuiElement
                {
                    Parent = $"home_{name}",
                    Name = "home_title",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = $"{name.ToUpper()} <color=#ffffff40>{MapHelper.PositionToString(pos)}</color>",
                            FontSize = 14,
                            Font = "robotocondensed-bold.ttf",
                            Align = TextAnchor.UpperLeft,
                            Color = "1 1 1 1",                            
                            FadeIn = 0f,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.02 0.025",
                            AnchorMax = "0.98 0.85"
                        }
                    },
                });

                container.Add(new CuiElement
                {
                    Parent = $"home_{name}",
                    Name = "home_distance",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = $"{distance} metres away",
                            FontSize = 12,
                            Font = "robotocondensed-regular.ttf",
                            Align = TextAnchor.LowerLeft,
                            Color = "0.55 0.53 0.51 0.7",                            
                            FadeIn = 0f,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.18 0.14",
                            AnchorMax = "0.6 0.90"
                        }

                    },
                });

                container.Add(new CuiElement
                {
                    Parent = $"home_{name}",
                    Name = "home_usesleft",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = $"{usesLeft} USES LEFT",
                            FontSize = 10,
                            Font = "robotocondensed-bold.ttf",
                            Align = TextAnchor.LowerLeft,
                            Color = "1 1 1 0.4",                            
                            FadeIn = 0f,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.02 0.14",
                            AnchorMax = "0.68 0.90"
                        }

                    },
                });
                
                
                container.Add(new CuiButton
                {
                    Button = { Close = "homes_background", Command = $"teleporthome {name.Replace(" ", "%")}", Color = "0.42 0.51 0.22 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                    RectTransform = { AnchorMin = "0.7 0.15", AnchorMax = "0.88 0.85" },
                    Text = { Text = "TELEPORT", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.67 0.95 0.16 1", Font = "robotocondensed-bold.ttf"}
                
                }, $"home_{name}", "teleportbutton");

                container.Add(new CuiButton
                {
                    Button = { Close = "", Command = $"deletehome2 popup {name}", Color = "0.8 0.25 0.17 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                    RectTransform = { AnchorMin = "0.90 0.15", AnchorMax = "0.98 0.85" },
                    Text = { Text = "", FontSize = 10, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
                }, $"home_{name}", "removehomebutton");

                
                container.Add(new CuiElement
                {   
                Parent = "removehomebutton",
                Components =
                 {
                    new CuiImageComponent { Material = "assets/icons/iconmaterial.mat", Sprite = "assets/icons/clear.png", Color = "1 1 1 1", FadeIn = 0.3f},
                    new CuiRectTransformComponent {AnchorMin = "0.18 0.2", AnchorMax = "0.8 0.8"}
                 },
                FadeOut = 0f
            });

            return $"{container}";
        }

        void Gridhomes (BasePlayer player)
        {
            var container = new CuiElementContainer();

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 0.9" }
            },
            "maininside",
            "homes_grid");

            CuiHelper.AddUi(player, container);

            PopulateGrid(player, 0);
        }






        








        [ChatCommand("homes")]
        private void openmain(BasePlayer player)
        {   
            if (!permission.UserHasPermission(player.UserIDString, "homes.use") && config.perm)
            {
                player.ChatMessage("You don't have permission to use this command.");
                return;
            }      

            player.ClientRPCPlayer(null, player, "OnDied");
            CuiHelper.DestroyUi(player, "homes_background");
            Stockscreen(player);
            Mainscreen(player);
            PopulateGrid(player);
            ShowCooldowns(player);
              
            
        }

        

        [ConsoleCommand("deletehome2")]
        private void deletehome2(ConsoleSystem.Arg arg)
        {  

            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;
            if (args.Length < 2) return;

            if (args[0] == "popup")
            {
                Usure(player, args[1]);
            }
            if (args[0] == "delete")
            {
                player.SendConsoleCommand($"chat.say \"/home remove {args[1]}\"");
                CuiHelper.DestroyUi(player, "usure_blurr");
                timer.Once(0.3f, () => { openmain(player); });  
            }
        }    

        [ChatCommand("testcd")]
        void ShowCooldowns(BasePlayer player)
        {
            int cooldown = NTeleportation.Call<int>("GetCooldownRemaining", player, "home");
            int cooldown1 = NTeleportation.Call<int>("GetCooldownRemaining", player, "town");
            int cooldown2 = NTeleportation.Call<int>("GetCooldownRemaining", player, "tpr");
            

            var container = new CuiElementContainer();
            if (cooldown <= 0)
            {
                container.Add(new CuiElement
                {
                    Parent = $"home_cooldown_panel",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "READY",
                            FontSize = 11,
                            Font = "robotocondensed-bold.ttf",
                            Align = TextAnchor.MiddleCenter,
                            Color = "0.67 0.95 0.16 1",                            
                            FadeIn = 0f,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1"
                        }
                    },
                });
                string update = @"[	
						{
							""name"": ""home_cooldown_panel"",
							""update"": true,
							""components"":
							[
								{
									""type"":""UnityEngine.UI.Image"",
									""color"": ""0.42 0.51 0.22 1""
								},
							]
						}
                ]";
                CuiHelper.AddUi(player, update);
            }
            else 
            {
                container.Add(new CuiElement
                {   
                    Parent = "home_cooldown_panel",
                    Components = {
                        new CuiTextComponent{ Text = "%TIME_LEFT% seconds", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf",},
                        new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1" },
                        new CuiCountdownComponent { EndTime = 0, StartTime = cooldown, Command = "refreshtimer"}
                    },
                    FadeOut = 0f
                });
                string update = @"[	
						{
							""name"": ""home_cooldown_panel"",
							""update"": true,
							""components"":
							[
								{
									""type"":""UnityEngine.UI.Image"",
									""color"": ""0.79 0.35 0.08 1""
								},
							]
						}
                ]";
                CuiHelper.AddUi(player, update);
            }

            if (cooldown1 <= 0)
            {
                container.Add(new CuiElement
                {
                    Parent = $"town_cooldown_panel",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "READY",
                            FontSize = 11,
                            Font = "robotocondensed-bold.ttf",
                            Align = TextAnchor.MiddleCenter,
                            Color = "0.67 0.95 0.16 1",                            
                            FadeIn = 0f,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1"
                        }
                    },
                });
                string update = @"[	
						{
							""name"": ""town_cooldown_panel"",
							""update"": true,
							""components"":
							[
								{
									""type"":""UnityEngine.UI.Image"",
									""color"": ""0.42 0.51 0.22 1""
								},
							]
						}
                ]";
                CuiHelper.AddUi(player, update);
            }
            else 
            {
                container.Add(new CuiElement
                {   
                    Parent = "town_cooldown_panel",
                    Components = {
                        new CuiTextComponent{ Text = "%TIME_LEFT% seconds", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf",},
                        new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1" },
                        new CuiCountdownComponent { EndTime = 0, StartTime = cooldown1, Command = "refreshtimer"}
                    },
                    FadeOut = 0f
                });
                string update = @"[	
						{
							""name"": ""town_cooldown_panel"",
							""update"": true,
							""components"":
							[
								{
									""type"":""UnityEngine.UI.Image"",
									""color"": ""0.79 0.35 0.08 1""
								},
							]
						}
                ]";
                CuiHelper.AddUi(player, update);

            }

            
            if (cooldown2 <= 0)
            {
                container.Add(new CuiElement
                {
                    Parent = $"tpr_cooldown_panel",
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = "READY",
                            FontSize = 11,
                            Font = "robotocondensed-bold.ttf",
                            Align = TextAnchor.MiddleCenter,
                            Color = "0.67 0.95 0.16 1",                            
                            FadeIn = 0f,
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0 0",
                            AnchorMax = "1 1"
                        }
                    },
                });
                //"0.42 0.51 0.22 1" green
                string update = @"[	
						{
							""name"": ""tpr_cooldown_panel"",
							""update"": true,
							""components"":
							[
								{
									""type"":""UnityEngine.UI.Image"",
									""color"": ""0.42 0.51 0.22 1""
								},
							]
						}
                ]";
                CuiHelper.AddUi(player, update);

            }
            else 
            {   
                container.Add(new CuiElement
                {   
                    Parent = "tpr_cooldown_panel",
                    Components = {
                        new CuiTextComponent{ Text = "%TIME_LEFT% seconds", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf",},
                        new CuiRectTransformComponent {AnchorMin = "0 0", AnchorMax = "1 1" },
                        new CuiCountdownComponent { EndTime = 0, StartTime = cooldown2, Command = "refreshtimer"}
                    },
                    FadeOut = 0f
                });
                string update = @"[	
						{
							""name"": ""tpr_cooldown_panel"",
							""update"": true,
							""components"":
							[
								{
									""type"":""UnityEngine.UI.Image"",
									""color"": ""0.79 0.35 0.08 1""
								},
							]
						}
                ]";
                CuiHelper.AddUi(player, update);
            }

            CuiHelper.AddUi(player, container);
        }

        [ConsoleCommand("refreshtimer")]
        private void refreshtimer(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            
            ShowCooldowns(player);


        }

        
        private void Usure(BasePlayer player, string homeName)
        {
            var container = new CuiElementContainer();



            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.93" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            },
            "Overlay",
            "usure_blurr");

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-680 -360", OffsetMax = "680 360" },
                CursorEnabled = true
                
            },
            "usure_blurr",
            "usure_offset");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 1"},
                RectTransform = { AnchorMin = "0.4 0.45", AnchorMax = "0.6 0.57" }
            },
            "usure_offset",
            "areusure_down");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.12 0.12 1"},
                RectTransform = { AnchorMin = "0.005 0.02", AnchorMax = "0.99 0.97" }
            },
            "areusure_down",
            "areusure");

            container.Add(new CuiElement
            {
                Parent = "areusure",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "ARE YOU SURE YOU WANT TO DESTROY THIS HOME ?",
                        FontSize = 12,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleCenter,
                        Color = "1 1 1 1",                            
                        FadeIn = 0f,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0 0.5",
                        AnchorMax = "1 1"
                    }
                },
            });

            container.Add(new CuiButton
            {
                Button = { Close = "", Command = $"deletehome2 delete {homeName}", Color = "0.42 0.51 0.22 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.03 0.08", AnchorMax = "0.48 0.45" },
                Text = { Text = "YES", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "areusure", "usure_yes");
            
            container.Add(new CuiButton
            {
                Button = { Close = "usure_blurr", Command = "", Color = "0.4 0.4 0.4 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.52 0.08", AnchorMax = "0.97 0.45" },
                Text = { Text = "NO", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "areusure", "usure_no");
            
            CuiHelper.DestroyUi(player, "usure_blurr");
            CuiHelper.AddUi(player, container);
        }

        private Dictionary<BasePlayer, string> input = new Dictionary<BasePlayer, string>();

        [ConsoleCommand("inputfieldcmd")]
        private void inputfieldcmd(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            string inputtext = string.Join("_", args);
            if (input.ContainsKey(player))
            {
                input[player] = inputtext;
            }
            else
            {
                input.Add(player, inputtext);
            }
        }

        [ConsoleCommand("home_new_request")]
        private void home_new_request(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (arg.Player() == null) return;

            if (args[0] == "add") 
            {
                player.SendConsoleCommand($"chat.say \"/home add {input[player]}\"");

                CuiHelper.DestroyUi(player, "homes_background");
                CuiHelper.DestroyUi(player, "newhome_blurr");
            }

            if (args[0] == "tpr") 
            {
                player.SendConsoleCommand($"chat.say \"/tpr {input[player]}\"");
                CuiHelper.DestroyUi(player, "homes_background");
                CuiHelper.DestroyUi(player, "newhome_blurr");
            }

            if (args[0] == "addpopup") 
            {   
                Addnewhome(player);
            }

            if (args[0] == "requesttp") 
            {
                Request(player);
            }
        }




        private void Addnewhome(BasePlayer player)
        {
            var container = new CuiElementContainer();
            
            if (input.ContainsKey(player))
            {
                input[player] = "";
            }
            else
            {
                input.Add(player, "");
            }

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.93" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                KeyboardEnabled = true
            },
            "Overlay",
            "newhome_blurr");

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-680 -360", OffsetMax = "680 360" },
                CursorEnabled = true
                
            },
            "newhome_blurr",
            "newhome_offset");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 1"},
                RectTransform = { AnchorMin = "0.4 0.45", AnchorMax = "0.6 0.57" }
            },
            "newhome_offset",
            "newhome_down");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.12 0.12 1"},
                RectTransform = { AnchorMin = "0.005 0.02", AnchorMax = "0.99 0.97" }
            },
            "newhome_down",
            "newhome");
            
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.75"},
                RectTransform = { AnchorMin = "0.35 0.55", AnchorMax = "0.97 0.9" }
            },
            "newhome",
            "input_panel");

            
            container.Add(new CuiElement
            {
                Parent = "newhome",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "Home name:",
                        FontSize = 14,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleLeft,
                        Color = "1 1 1 1",                            
                        FadeIn = 0f,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.05 0.55",
                        AnchorMax = "0.349 0.9"
                    }
                },
            });
            
            

            container.Add(new CuiButton
            {
                Button = { Close = "", Command = "home_new_request add", Color = "0.42 0.51 0.22 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.03 0.08", AnchorMax = "0.48 0.45" },
                Text = { Text = "ADD", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "newhome", "newhome_yes");
            
            container.Add(new CuiButton
            {
                Button = { Close = "newhome_blurr", Command = "", Color = "0.4 0.4 0.4 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.52 0.08", AnchorMax = "0.97 0.45" },
                Text = { Text = "BACK", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "newhome", "newhome_no");

            container.Add(new CuiElement
                {
                    Parent = "input_panel",
                    Name = "input",

                    Components =
                    {
                        new CuiInputFieldComponent
                        {

                            Text = "",
                            CharsLimit = 20,
                            Color = "1 1 1 1",
                            IsPassword = false,
                            Command = "inputfieldcmd",
                            Font = "robotocondensed-regular.ttf",
                            FontSize = 13,
                            Align = TextAnchor.MiddleLeft,
                            Autofocus = true,
                        },

                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.05 0",
                            AnchorMax = "1 1"

                        }

                    },
             });
            
            CuiHelper.DestroyUi(player, "newhome_blurr");
            CuiHelper.AddUi(player, container);
        }


        private void Request(BasePlayer player)
        {
            var container = new CuiElementContainer();
            
            if (input.ContainsKey(player))
            {
                input[player] = "";
            }
            else
            {
                input.Add(player, "");
            }

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.93" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                KeyboardEnabled = true
            },
            "Overlay",
            "newhome_blurr");

            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0.5 0.5", AnchorMax = "0.5 0.5", OffsetMin = "-680 -360", OffsetMax = "680 360" },
                CursorEnabled = true
                
            },
            "newhome_blurr",
            "newhome_offset");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.5 0.5 0.5 1"},
                RectTransform = { AnchorMin = "0.4 0.45", AnchorMax = "0.6 0.57" }
            },
            "newhome_offset",
            "newhome_down");

            container.Add(new CuiPanel
            {
                Image = { Color = "0.12 0.12 0.12 1"},
                RectTransform = { AnchorMin = "0.005 0.02", AnchorMax = "0.99 0.97" }
            },
            "newhome_down",
            "newhome");
            
            container.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0.75"},
                RectTransform = { AnchorMin = "0.35 0.55", AnchorMax = "0.97 0.9" }
            },
            "newhome",
            "input_panel");

            
            container.Add(new CuiElement
            {
                Parent = "newhome",
                Components =
                {
                    new CuiTextComponent
                    {
                        Text = "Player name:",
                        FontSize = 14,
                        Font = "robotocondensed-bold.ttf",
                        Align = TextAnchor.MiddleLeft,
                        Color = "1 1 1 1",                            
                        FadeIn = 0f,
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.05 0.55",
                        AnchorMax = "0.349 0.9"
                    }
                },
            });
            
            

            container.Add(new CuiButton
            {
                Button = { Close = "", Command = "home_new_request tpr", Color = "0.42 0.51 0.22 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.03 0.08", AnchorMax = "0.48 0.45" },
                Text = { Text = "REQUEST", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "newhome", "newhome_yes");
            
            container.Add(new CuiButton
            {
                Button = { Close = "newhome_blurr", Command = "", Color = "0.4 0.4 0.4 1", Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat"},
                RectTransform = { AnchorMin = "0.52 0.08", AnchorMax = "0.97 0.45" },
                Text = { Text = "BACK", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1", Font = "robotocondensed-regular.ttf"}
                
            }, "newhome", "newhome_no");

            container.Add(new CuiElement
                {
                    Parent = "input_panel",
                    Name = "input",

                    Components =
                    {
                        new CuiInputFieldComponent
                        {

                            Text = "",
                            CharsLimit = 20,
                            Color = "1 1 1 1",
                            IsPassword = false,
                            Command = "inputfieldcmd",
                            Font = "robotocondensed-regular.ttf",
                            FontSize = 13,
                            Align = TextAnchor.MiddleLeft,
                            Autofocus = true,
                        },

                        new CuiRectTransformComponent
                        {
                            AnchorMin = "0.05 0",
                            AnchorMax = "1 1"

                        }

                    },
             });
            
            CuiHelper.DestroyUi(player, "newhome_blurr");
            CuiHelper.AddUi(player, container);
        }
        
        private Configuration config;
        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();
            SaveConfig();
        }
        
        protected override void LoadDefaultConfig()
        {
            config = Configuration.CreateConfig();
        }
        
        protected override void SaveConfig() => Config.WriteObject(config);     
        
        class Configuration
        {   
            [JsonProperty("Easy home Button")]
            public bool homesbtn {get; set;}

            [JsonProperty("Outpost Button")]
            public bool outpostbtn {get; set;}

            [JsonProperty("Bandit Camp Button")]
            public bool banditbtn {get; set;}

            [JsonProperty("Town Button")]
            public bool townbtn {get; set;}
        
            [JsonProperty("Require Perm")]
            public bool perm {get; set;}


        
            public static Configuration CreateConfig()
            {
                return new Configuration
                {   
                    homesbtn = false,
                    outpostbtn = false,
                    banditbtn = false,
                    townbtn = false,
                    perm = false
                };
            }
        }
    }
}