//version 1.2.3
using Newtonsoft.Json;
using Oxide.Game.Rust.Cui;
using System.Collections.Generic;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("WPSocialLinks", "David", "1.3.23")]
    [Description("Social Media Links Addon for Welcome Panel")]

    public class WPSocialLinks : RustPlugin
    {   

        #region [Further Customization]

        private Dictionary<string, string> _p = new Dictionary<string, string>
        {
                // PANEL 1
                { "p1_Amin", "0.1 0.65" },    
                { "p1_Amax", "0.475 0.85" },   
                // PANEL 2
                { "p2_Amin", "0.525 0.65" },    
                { "p2_Amax", "0.900 0.85" }, 
                // PANEL 3
                { "p3_Amin", "0.1 0.43" },    
                { "p3_Amax", "0.475 0.63" }, 
                // PANEL 4 
                { "p4_Amin", "0.525 0.43" },    
                { "p4_Amax", "0.900 0.63" }, 
                // PANEL 5
                { "p5_Amin", "0.1 0.21" },    
                { "p5_Amax", "0.475 0.41" }, 
                // PANEL 6
                { "p6_Amin", "0.525 0.21" },    
                { "p6_Amax", "0.900 0.41" }, 
                // SUB PANELS
                    //LOGO PANEL
                    { "lp_Amin", "0.03 0.09" },
                    { "lp_Amax", "0.32 0.91" },
                    //TEXT PANEL
                    { "tp_Amin", "0.34 0.41" },
                    { "tp_Amax", "0.97 0.91" },
                    //BUTTON
                    { "btn_Amin", "0.34 0.09" },
                    { "btn_Amax", "0.97 0.35" },      
        };

        #endregion

        #region [Image Handling]

        string layer = "WelcomePanel_content";

        [PluginReference] Plugin ImageLibrary, WelcomePanel;

        private void DownloadImages()
        {   
            if (ImageLibrary == null) return;

            string[] images = { $"{config.panel1.iconURL}", $"{config.panel2.iconURL}", $"{config.panel3.iconURL}", $"{config.panel4.iconURL}", $"{config.panel5.iconURL}",
             $"{config.panel6.iconURL}" };

            foreach (string img in images)
                ImageLibrary.Call("AddImage", img, img); 
        }

        private string Img(string url)
        {
            if (ImageLibrary != null) {
                
                if (!(bool) ImageLibrary.Call("HasImage", url))
                    return url;
                else
                    return (string) ImageLibrary?.Call("GetImage", url);
            }
            else return url;
        }

        #endregion

        #region [Hooks]

        private void OnServerInitialized()
        {       
            LoadConfig();    
            DownloadImages();    
        }

        #endregion

        #region [Custom Note]

        [ConsoleCommand("create_note")]
        private void create_note(ConsoleSystem.Arg arg)
        { 
            var player = arg?.Player(); 
            var args = arg.Args;
            if (arg.Player() == null) return;

            if (args.Length < 1) return;

           
           
            if (args[0] == "1") { CreateNot(player); NoteCreateLink(player, config.panel1.noteSkinID, config.panel1.noteName, config.panel1.noteText); return; } 
            if (args[0] == "2") { CreateNot(player); NoteCreateLink(player, config.panel2.noteSkinID, config.panel2.noteName, config.panel2.noteText); return; } 
            if (args[0] == "3") { CreateNot(player); NoteCreateLink(player, config.panel3.noteSkinID, config.panel3.noteName, config.panel3.noteText); return; } 
            if (args[0] == "4") { CreateNot(player); NoteCreateLink(player, config.panel4.noteSkinID, config.panel4.noteName, config.panel4.noteText); return; } 
            if (args[0] == "5") { CreateNot(player); NoteCreateLink(player, config.panel5.noteSkinID, config.panel5.noteName, config.panel5.noteText); return; } 
            if (args[0] == "6") { CreateNot(player); NoteCreateLink(player, config.panel6.noteSkinID, config.panel6.noteName, config.panel6.noteText); return; } 
            
        }

        [ConsoleCommand("not_cmd")]
        private void not_cmd(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player(); 
            var args = arg.Args;
            if (arg.Player() == null) return;

            if (args.Length < 1) return;
            if (args[0] == "show") { CreateNot(player); return; }
            if (args[0] == "close") { CloseNot(player); return; }
        }
        

        private void NoteCreateLink(BasePlayer player, ulong _skinId, string _noteName, string _noteText )
        {   
            if (config.otherSet.chatTextEnabled && config.otherSet.chatTextEnabled != null)
            {   
                player.SendConsoleCommand($"chat.add 0 1 \"{_noteText}\"");
                return;
            }

            Item _customNote = ItemManager.CreateByPartialName("note", 1 , _skinId);
            _customNote.name = _noteName;
            _customNote.text = _noteText;
            _customNote.MarkDirty();
            player.inventory.GiveItem(_customNote);
        }

        #endregion

        #region [Cui API]

        void OnWelcomePanelPageOpen(BasePlayer player, int tab, int page, string addon)
        {   
            if(addon == null) return;

            if (addon.ToLower().Contains("links"))
            {       
                layer = "wp_content";

                // 3.2 version
                if (WelcomePanel.Version.Major == 3)
                    layer = "WelcomePanel_content";

                // 4.0.9 version
                if (WelcomePanel.Version.Major == 4 && WelcomePanel.Version.Minor < 3)
                    layer = "content";

                ShowLinks_API(player);
            }
        }


        private void ShowLinks_API(BasePlayer player)
        {
            var _wipeCycleUI = new CuiElementContainer();
            //TITLE
            CUIClass.CreateText(ref _wipeCycleUI, "text_title", layer, "1 1 1 1", config.otherSet.topTitleText, 15, "0 0.84", "1 1", TextAnchor.MiddleCenter, $"robotocondensed-regular.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
            //PANEL 1 
            if (config.panel1.enabled)
            {
                CUIClass.CreatePanel(ref _wipeCycleUI, "div_panel1", layer, config.panel1.mainColor, _p["p1_Amin"], _p["p1_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel1_logo_panel", "div_panel1", config.panel1.secColor, _p["lp_Amin"], _p["lp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel1_text_panel", "div_panel1", config.panel1.secColor, _p["tp_Amin"], _p["tp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreateButton(ref _wipeCycleUI, "panel1_btn_panel", "div_panel1", config.panel1.btnColor, config.panel1.btnText, 13, _p["btn_Amin"], _p["btn_Amax"], $"create_note 1", "", "1 1 1 1", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf");
                        CUIClass.CreateText(ref _wipeCycleUI, "panel1_text", "panel1_text_panel", "1 1 1 1", config.panel1.textField, 8, "0.025 0", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
                        CUIClass.CreateImage(ref _wipeCycleUI, "panel1_logo_panel", Img($"{config.panel1.iconURL}"), "0.05 0.05", "0.95 0.95");
            }
            //PANEL 2
            if (config.panel2.enabled)
            {
                CUIClass.CreatePanel(ref _wipeCycleUI, "div_panel2", layer, config.panel2.mainColor, _p["p2_Amin"], _p["p2_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel2_logo_panel", "div_panel2", config.panel2.secColor, _p["lp_Amin"], _p["lp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel2_text_panel", "div_panel2", config.panel2.secColor, _p["tp_Amin"], _p["tp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreateButton(ref _wipeCycleUI, "panel2_btn_panel", "div_panel2", config.panel2.btnColor, config.panel2.btnText, 13, _p["btn_Amin"], _p["btn_Amax"], $"create_note 2", "", "1 1 1 1", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf");
                        CUIClass.CreateText(ref _wipeCycleUI, "panel2_text", "panel2_text_panel", "1 1 1 1", config.panel2.textField, 8, "0.025 0", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
                        CUIClass.CreateImage(ref _wipeCycleUI, "panel2_logo_panel", Img($"{config.panel2.iconURL}"), "0.05 0.05", "0.95 0.95");
            }
            //PANEL 3
            if (config.panel3.enabled)
            {
                CUIClass.CreatePanel(ref _wipeCycleUI, "div_panel3", layer, config.panel3.mainColor, _p["p3_Amin"], _p["p3_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel3_logo_panel", "div_panel3", config.panel3.secColor, _p["lp_Amin"], _p["lp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel3_text_panel", "div_panel3", config.panel3.secColor, _p["tp_Amin"], _p["tp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreateButton(ref _wipeCycleUI, "panel3_btn_panel", "div_panel3", config.panel3.btnColor, config.panel3.btnText, 13, _p["btn_Amin"], _p["btn_Amax"], $"create_note 3", "", "1 1 1 1", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf");
                        CUIClass.CreateText(ref _wipeCycleUI, "panel3_text", "panel3_text_panel", "1 1 1 1", config.panel3.textField, 8, "0.025 0", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
                        CUIClass.CreateImage(ref _wipeCycleUI, "panel3_logo_panel", Img($"{config.panel3.iconURL}"), "0.05 0.05", "0.95 0.95");
            }   
            //PANEL 4
            if (config.panel4.enabled)
            {
                CUIClass.CreatePanel(ref _wipeCycleUI, "div_panel4", layer, config.panel4.mainColor, _p["p4_Amin"], _p["p4_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel4_logo_panel", "div_panel4", config.panel4.secColor, _p["lp_Amin"], _p["lp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel4_text_panel", "div_panel4", config.panel4.secColor, _p["tp_Amin"], _p["tp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreateButton(ref _wipeCycleUI, "panel4_btn_panel", "div_panel4", config.panel4.btnColor, config.panel4.btnText, 13, _p["btn_Amin"], _p["btn_Amax"], $"create_note 4", "", "1 1 1 1", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf");
                        CUIClass.CreateText(ref _wipeCycleUI, "panel4_text", "panel4_text_panel", "1 1 1 1", config.panel4.textField, 8, "0.025 0", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
                        CUIClass.CreateImage(ref _wipeCycleUI, "panel4_logo_panel", Img($"{config.panel4.iconURL}"), "0.05 0.05", "0.95 0.95");
            }   
            //PANEL 5
            if (config.panel5.enabled)
            {
                CUIClass.CreatePanel(ref _wipeCycleUI, "div_panel5", layer, config.panel5.mainColor, _p["p5_Amin"], _p["p5_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel5_logo_panel", "div_panel5", config.panel5.secColor, _p["lp_Amin"], _p["lp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel5_text_panel", "div_panel5", config.panel5.secColor, _p["tp_Amin"], _p["tp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreateButton(ref _wipeCycleUI, "panel5_btn_panel", "div_panel5", config.panel5.btnColor, config.panel5.btnText, 13, _p["btn_Amin"], _p["btn_Amax"], $"create_note 5", "", "1 1 1 1", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf");
                        CUIClass.CreateText(ref _wipeCycleUI, "panel5_text", "panel5_text_panel", "1 1 1 1", config.panel5.textField, 8, "0.025 0", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
                        CUIClass.CreateImage(ref _wipeCycleUI, "panel5_logo_panel", Img($"{config.panel5.iconURL}"), "0.05 0.05", "0.95 0.95");
            }
            //PANEL 6
            if (config.panel6.enabled)
            {
                CUIClass.CreatePanel(ref _wipeCycleUI, "div_panel6", layer, config.panel6.mainColor, _p["p6_Amin"], _p["p6_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel6_logo_panel", "div_panel6", config.panel6.secColor, _p["lp_Amin"], _p["lp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreatePanel(ref _wipeCycleUI, "panel6_text_panel", "div_panel6", config.panel6.secColor, _p["tp_Amin"], _p["tp_Amax"], false, 0f, "assets/content/ui/uibackgroundblur.mat");
                    CUIClass.CreateButton(ref _wipeCycleUI, "panel6_btn_panel", "div_panel6", config.panel6.btnColor, config.panel6.btnText, 13, _p["btn_Amin"], _p["btn_Amax"], $"create_note 6", "", "1 1 1 1", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf");
                        CUIClass.CreateText(ref _wipeCycleUI, "panel6_text", "panel6_text_panel", "1 1 1 1", config.panel6.textField, 8, "0.025 0", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
                        CUIClass.CreateImage(ref _wipeCycleUI, "panel6_logo_panel", Img($"{config.panel6.iconURL}"), "0.05 0.05", "0.95 0.95");
            }
            //FOOTER
            CUIClass.CreateText(ref _wipeCycleUI, "text_footer", layer, "1 1 1 1", config.otherSet.footerText, 15, "0 0.0.5", "1 0.17", TextAnchor.MiddleCenter, $"robotocondensed-regular.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
            //end

            CloseLinks_API(player);
            CuiHelper.AddUi(player, _wipeCycleUI); 
        }    

        private void CloseLinks_API(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "main");
            for (int i = 1; i < 7; i++)
            {
                CuiHelper.DestroyUi(player, $"div_panel{i}");
            }
        }

        private void CreateNot(BasePlayer player)
        {   
            string notText = "Pop up window text is missing, please change it in config file.";
            if (config.otherSet.notePopMsg != null) notText = config.otherSet.notePopMsg;
            

            var _uiNotification = CUIClass.CreateOverlay("main_not", "0.19 0.19 0.19 0.90", "0 0", "1 1", true, 0.0f, $"assets/content/ui/uibackgroundblur.mat");
            CUIClass.CreatePanel(ref _uiNotification, "blurr2", "main_not", "0 0 0 0.95", "0 0", "1 1", false, 0f, "assets/content/ui/uibackgroundblur.mat");
            
            CUIClass.CreatePanel(ref _uiNotification, "main_panel", "main_not", "0.19 0.19 0.19 0.99", "0.4 0.4", "0.6 0.6", false, 0f, "assets/content/ui/uibackgroundblur.mat");
            CUIClass.CreateText(ref _uiNotification, "text_title", "main_panel", "1 1 1 1", $"{notText}", 15, "0.1 0", "0.9 0.8", TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", config.otherSet.fontOutlineColor, $"{config.otherSet.fontOutlineThickness} {config.otherSet.fontOutlineThickness}");
            CUIClass.CreateImage(ref _uiNotification, "main_panel", "https://rustlabs.com/img/items180/note.png", "0.4 0.60", "0.6 0.90");
            CUIClass.CreateButton(ref _uiNotification, "close_btn", "main_panel", "0.56 0.20 0.15 1.0", "✘", 11, "0.85 0.8", "0.97 0.95", "not_cmd close", "", "1 1 1 1", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf");       
            
            CloseNot(player);
            CuiHelper.AddUi(player, _uiNotification); 

            if (config.otherSet.closePanel && config.otherSet.closePanel != null)
                player.SendConsoleCommand($"welcomepanel_close");
           
        }

        private void CloseNot(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "main_not");
        }



        #endregion 

        #region [Cui Class]

        public class CUIClass
        {
            public static CuiElementContainer CreateOverlay(string _name, string _color, string _anchorMin, string _anchorMax, bool _cursorOn = false, float _fade = 0f, string _mat ="")
            {   

            
                var _element = new CuiElementContainer()
                {
                    {
                        new CuiPanel
                        {
                            Image = { Color = _color, Material = _mat, FadeIn = _fade},
                            RectTransform = { AnchorMin = _anchorMin, AnchorMax = _anchorMax },
                            CursorEnabled = _cursorOn
                        },
                        new CuiElement().Parent = "Overlay",
                        _name
                    }
                };
                return _element;
            }

            public static void CreatePanel(ref CuiElementContainer _container, string _name, string _parent, string _color, string _anchorMin, string _anchorMax, bool _cursorOn = false, float _fade = 0f, string _mat2 ="")
            {
                _container.Add(new CuiPanel
                {
                    Image = { Color = _color, Material = _mat2, FadeIn = _fade },
                    RectTransform = { AnchorMin = _anchorMin, AnchorMax = _anchorMax },
                    CursorEnabled = _cursorOn
                },
                _parent,
                _name);
            }

            public static void CreateImage(ref CuiElementContainer _container, string _parent, string _image, string _anchorMin, string _anchorMax, float _fade = 0f)
            {
                if (_image.StartsWith("http") || _image.StartsWith("www"))
                {
                    _container.Add(new CuiElement
                    {
                        Parent = _parent,
                        Components =
                        {
                            new CuiRawImageComponent { Url = _image, Sprite = "assets/content/textures/generic/fulltransparent.tga", FadeIn = _fade},
                            new CuiRectTransformComponent { AnchorMin = _anchorMin, AnchorMax = _anchorMax }
                        }
                    });
                }
                else
                {
                    _container.Add(new CuiElement
                    {
                        Parent = _parent,
                        Components =
                        {
                            new CuiRawImageComponent { Png = _image, Sprite = "assets/content/textures/generic/fulltransparent.tga", FadeIn = _fade},
                            new CuiRectTransformComponent { AnchorMin = _anchorMin, AnchorMax = _anchorMax }
                        }
                    });
                }
            }

            public static void CreateInput(ref CuiElementContainer _container, string _name, string _parent, string _color, int _size, string _anchorMin, string _anchorMax, string _font = "permanentmarker.ttf", string _command = "command.processinput", TextAnchor _align = TextAnchor.MiddleCenter)
            {
                _container.Add(new CuiElement
                {
                    Parent = _parent,
                    Name = _name,

                    Components =
                    {
                        new CuiInputFieldComponent
                        {

                            Text = "0",
                            CharsLimit = 11,
                            Color = _color,
                            IsPassword = false,
                            Command = _command,
                            Font = _font,
                            FontSize = _size,
                            Align = _align
                        },

                        new CuiRectTransformComponent
                        {
                            AnchorMin = _anchorMin,
                            AnchorMax = _anchorMax

                        }

                    },
                });
            }

            public static void CreateText(ref CuiElementContainer _container, string _name, string _parent, string _color, string _text, int _size, string _anchorMin, string _anchorMax, TextAnchor _align = TextAnchor.MiddleCenter, string _font = "robotocondensed-bold.ttf", string _outlineColor = "", string _outlineScale ="")
            {   
               

                _container.Add(new CuiElement
                {
                    Parent = _parent,
                    Name = _name,
                    Components =
                    {
                        new CuiTextComponent
                        {
                            Text = _text,
                            FontSize = _size,
                            Font = _font,
                            Align = _align,
                            Color = _color,
                            FadeIn = 0f,
                        },

                        new CuiOutlineComponent
                        {
                            
                            Color = _outlineColor,
                            Distance = _outlineScale
                            
                        },

                        new CuiRectTransformComponent
                        {
                             AnchorMin = _anchorMin,
                             AnchorMax = _anchorMax
                        }
                    },
                });
            }

            public static void CreateButton(ref CuiElementContainer _container, string _name, string _parent, string _color, string _text, int _size, string _anchorMin, string _anchorMax, string _command = "", string _close = "", string _textColor = "0.843 0.816 0.78 1", float _fade = 0f, TextAnchor _align = TextAnchor.MiddleCenter, string _font = "")
            {       
               
                _container.Add(new CuiButton
                {
                    Button = { Close = _close, Command = _command, Color = _color, Material = "assets/content/ui/uibackgroundblur-ingamemenu.mat", FadeIn = _fade},
                    RectTransform = { AnchorMin = _anchorMin, AnchorMax = _anchorMax },
                    Text = { Text = _text, FontSize = _size, Align = _align, Color = _textColor, Font = _font, FadeIn = _fade}
                },
                _parent,
                _name);
            }

        }
        #endregion

        #region [Config] 

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

            [JsonProperty(PropertyName = "Panel #1")]
            public Panel1 panel1 { get; set; }

            public class Panel1
            {   
                [JsonProperty("Enabled")]
                public bool enabled { get; set; }

                [JsonProperty("Text Field")]
                public string textField { get; set; }

                [JsonProperty("Icon Image")]
                public string iconURL { get; set; }

                [JsonProperty("Button Text")]
                public string btnText { get; set; }

                [JsonProperty("Note Text")]
                public string noteText { get; set; }

                [JsonProperty("Note Name")]
                public string noteName { get; set; }

                [JsonProperty("Note Skin ID / Icon")]
                public ulong noteSkinID { get; set; }

                [JsonProperty("Main Panel Color")]
                public string mainColor { get; set; }

                [JsonProperty("Secondary Panel Color")]
                public string secColor { get; set; }

                [JsonProperty("Button Color")]
                public string btnColor { get; set; }
            } 

            [JsonProperty(PropertyName = "Panel #2")]
            public Panel2 panel2 { get; set; }

            public class Panel2
            {   
                [JsonProperty("Enabled")]
                public bool enabled { get; set; }

                [JsonProperty("Text Field")]
                public string textField { get; set; }

                [JsonProperty("Icon Image")]
                public string iconURL { get; set; }

                [JsonProperty("Button Text")]
                public string btnText { get; set; }

                [JsonProperty("Note Text")]
                public string noteText { get; set; }

                [JsonProperty("Note Name")]
                public string noteName { get; set; }

                [JsonProperty("Note Skin ID / Icon")]
                public ulong noteSkinID { get; set; }

                [JsonProperty("Main Panel Color")]
                public string mainColor { get; set; }

                [JsonProperty("Secondary Panel Color")]
                public string secColor { get; set; }

                [JsonProperty("Button Color")]
                public string btnColor { get; set; }
            }  

            [JsonProperty(PropertyName = "Panel #3")]
            public Panel3 panel3 { get; set; }

            public class Panel3
            {   
                [JsonProperty("Enabled")]
                public bool enabled { get; set; }

                [JsonProperty("Text Field")]
                public string textField { get; set; }

                [JsonProperty("Icon Image")]
                public string iconURL { get; set; }

                [JsonProperty("Button Text")]
                public string btnText { get; set; }

                [JsonProperty("Note Text")]
                public string noteText { get; set; }

                [JsonProperty("Note Name")]
                public string noteName { get; set; }

                [JsonProperty("Note Skin ID / Icon")]
                public ulong noteSkinID { get; set; }

                [JsonProperty("Main Panel Color")]
                public string mainColor { get; set; }

                [JsonProperty("Secondary Panel Color")]
                public string secColor { get; set; }

                [JsonProperty("Button Color")]
                public string btnColor { get; set; }
            } 

            [JsonProperty(PropertyName = "Panel #4")]
            public Panel4 panel4 { get; set; }

            public class Panel4
            {   
                [JsonProperty("Enabled")]
                public bool enabled { get; set; }

                [JsonProperty("Text Field")]
                public string textField { get; set; }

                [JsonProperty("Icon Image")]
                public string iconURL { get; set; }

                [JsonProperty("Button Text")]
                public string btnText { get; set; }

                [JsonProperty("Note Text")]
                public string noteText { get; set; }

                [JsonProperty("Note Name")]
                public string noteName { get; set; }

                [JsonProperty("Note Skin ID / Icon")]
                public ulong noteSkinID { get; set; }

                [JsonProperty("Main Panel Color")]
                public string mainColor { get; set; }

                [JsonProperty("Secondary Panel Color")]
                public string secColor { get; set; }

                [JsonProperty("Button Color")]
                public string btnColor { get; set; }
            }  

            [JsonProperty(PropertyName = "Panel #5")]
            public Panel5 panel5 { get; set; }

            public class Panel5
            {   
                [JsonProperty("Enabled")]
                public bool enabled { get; set; }

                [JsonProperty("Text Field")]
                public string textField { get; set; }

                [JsonProperty("Icon Image")]
                public string iconURL { get; set; }

                [JsonProperty("Button Text")]
                public string btnText { get; set; }

                [JsonProperty("Note Text")]
                public string noteText { get; set; }

                [JsonProperty("Note Name")]
                public string noteName { get; set; }

                [JsonProperty("Note Skin ID / Icon")]
                public ulong noteSkinID { get; set; }

                [JsonProperty("Main Panel Color")]
                public string mainColor { get; set; }

                [JsonProperty("Secondary Panel Color")]
                public string secColor { get; set; }

                [JsonProperty("Button Color")]
                public string btnColor { get; set; }
            }  

            [JsonProperty(PropertyName = "Panel #6")]
            public Panel6 panel6 { get; set; }

            public class Panel6
            {   
                [JsonProperty("Enabled")]
                public bool enabled { get; set; }

                [JsonProperty("Text Field")]
                public string textField { get; set; }

                [JsonProperty("Icon Image")]
                public string iconURL { get; set; }

                [JsonProperty("Button Text")]
                public string btnText { get; set; }

                [JsonProperty("Note Text")]
                public string noteText { get; set; }

                [JsonProperty("Note Name")]
                public string noteName { get; set; }

                [JsonProperty("Note Skin ID / Icon")]
                public ulong noteSkinID { get; set; }

                [JsonProperty("Main Panel Color")]
                public string mainColor { get; set; }

                [JsonProperty("Secondary Panel Color")]
                public string secColor { get; set; }

                [JsonProperty("Button Color")]
                public string btnColor { get; set; }
            } 

            [JsonProperty(PropertyName = "Other")]
            public OtherSet otherSet { get; set; }

            public class OtherSet
            {       
                [JsonProperty("Send Chat Message instead of Note")]
                public bool chatTextEnabled { get; set; }

                [JsonProperty("Close WelcomePanel after clicking GET LINK button")]
                public bool closePanel { get; set; }
                
                [JsonProperty("Pop up window message")]
                public string notePopMsg { get; set; }
            
                [JsonProperty("Title Text")]
                public string topTitleText { get; set; }

                [JsonProperty("Footer Text")]
                public string footerText { get; set; }

                [JsonProperty("Font Outline Color")]
                public string fontOutlineColor { get; set; }

                [JsonProperty("Font Outline Thickness")]
                public string fontOutlineThickness { get; set; }
            }

            

            public static Configuration CreateConfig()
            {
                return new Configuration
                {   

                    panel1 = new WPSocialLinks.Configuration.Panel1
                    {   
                        enabled = true,
                        textField = "<size=22>DISCORD</size> \n <size=9>Link your discord with game account!</size>",
                        iconURL = "https://i.ibb.co/pRYmCBx/discordicon.png",
                        btnText = "GET LINK",
                        noteText = "\n \n discord.gg/invitelink \n \n you can copy paste link from here",
                        noteName = "DISCORD",
                        noteSkinID = 2391705198,
                        mainColor = "0.25 0.25 0.25 0.65",
                        secColor = "0.19 0.19 0.19 0.85",
                        btnColor = "0.16 0.34 0.49 0.80",
                    },

                    panel2 = new WPSocialLinks.Configuration.Panel2
                    {   
                        enabled = true,
                        textField = "<size=22>STEAM GROUP</size> \n <size=9>Join our steam group and get rewarded!</size>",
                        iconURL = "https://i.ibb.co/8c4sVfQ/steamicon.png",
                        btnText = "GET LINK",
                        noteText = "\n \n steamgroup.link \n \n you can copy paste link from here",
                        noteName = "STEAM GROUP",
                        noteSkinID = 2543322586,
                        mainColor = "0.25 0.25 0.25 0.65",
                        secColor = "0.19 0.19 0.19 0.85",
                        btnColor = "0.16 0.34 0.49 0.80",
                    },

                    panel3 = new WPSocialLinks.Configuration.Panel3
                    {   
                        enabled = true,
                        textField = "<size=22>TWITTER</size> \n <size=9>Follow us on twitter to stay updated!</size>",
                        iconURL = "https://i.ibb.co/GHyGbPy/twitter.png",
                        btnText = "GET LINK",
                        noteText = "\n \n twitter.link \n \n you can copy paste link from here",
                        noteName = "TWITTER",
                        noteSkinID = 2543321941,
                        mainColor = "0.25 0.25 0.25 0.65",
                        secColor = "0.19 0.19 0.19 0.85",
                        btnColor = "0.16 0.34 0.49 0.80",
                    },

                    panel4 = new WPSocialLinks.Configuration.Panel4
                    {   
                        enabled = true,
                        textField = "<size=22>YOUTUBE</size> \n <size=9>Check out our channel for fresh content!</size>",
                        iconURL = "https://i.ibb.co/mRY8Z3M/youtubeicon.png",
                        btnText = "GET LINK",
                        noteText = "\n \n youtube.link \n \n you can copy paste link from here",
                        noteName = "YOTUBE",
                        noteSkinID = 2543316628,
                        mainColor = "0.25 0.25 0.25 0.65",
                        secColor = "0.19 0.19 0.19 0.85",
                        btnColor = "0.16 0.34 0.49 0.80",
                    },

                    panel5 = new WPSocialLinks.Configuration.Panel5
                    {   
                        enabled = true,
                        textField = "<size=22>SHOP</size> \n <size=9>Donate and support our community!</size>",
                        iconURL = "https://i.ibb.co/x3ydpLw/shopicon.png",
                        btnText = "GET LINK",
                        noteText = "\n \n shop.link \n \n you can copy paste link from here",
                        noteName = "DONATE",
                        noteSkinID = 2543321437,
                        mainColor = "0.25 0.25 0.25 0.65",
                        secColor = "0.19 0.19 0.19 0.85",
                        btnColor = "0.16 0.34 0.49 0.80",
                    },

                    panel6 = new WPSocialLinks.Configuration.Panel6
                    {   
                        enabled = true,
                        textField = "<size=22>WEBSITE</size> \n <size=9>Checkout our website to find out more!</size>",
                        iconURL = "https://i.ibb.co/h98K7RT/websiteicon.png",
                        btnText = "GET LINK",
                        noteText = "\n \n website.link \n \n you can copy paste link from here",
                        noteName = "WEBSITE",
                        noteSkinID = 2543323101,
                        mainColor = "0.25 0.25 0.25 0.65",
                        secColor = "0.19 0.19 0.19 0.85",
                        btnColor = "0.16 0.34 0.49 0.80",
                    },

                    otherSet = new WPSocialLinks.Configuration.OtherSet
                    {   
                        chatTextEnabled = false,
                        closePanel = false,
                        notePopMsg = "You've got a note with url in your inventory.",
                        topTitleText = "You can find us here",
                        footerText = "Follow us on social media and claim your rewards!",
                        fontOutlineColor = "0 0 0 1",
                        fontOutlineThickness = "1",
                    },

                };
            }
        }

        #endregion

    }
}