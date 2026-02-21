using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Plugins;
using UnityEngine;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Network;

namespace Oxide.Plugins
{
    [Info("WPKits", "David", "1.3.23")]
    public class WPKits : RustPlugin
    {   
        [PluginReference] Plugin ImageLibrary, WelcomePanel, Kits, WipeCountdown, Notifications;

        void OnWelcomePanelPageOpen(BasePlayer player, int tab, int page, string addon)
        {   
            if (addon != null && addon.ToLower() == "kits") 
            {   
                ShowKits(player);
            }
        }

        void OnServerInitialized()
        {   
            LoadData();

            if (ImageLibrary != null)
                ImageLibrary.Call("AddImage","https://rustplugins.net/products/welcomepanel/new/file-find.png", "https://rustplugins.net/products/welcomepanel/new/file-find.png");
            
            if (!addonsData.ContainsKey("addons"))
            {
                addonsData.Add("addons", new AddonsData());
                addonsData["addons"].RustKits_claimCommand = "kit";
                addonsData["addons"].RustKits_currencyName = "$";
                SaveData();
            }
        }

        private string Img(string url)
        {
            if (ImageLibrary != null) 
            {   
                if (!(bool) ImageLibrary.Call("HasImage", url))
                    return url;
                else
                    return (string) ImageLibrary?.Call("GetImage", url);
            }
            else return url;
        }

        private void SetImageList (string imagedata)
        {   
            if (ImageLibrary == null) 
            {
                Puts("ImageLibrary not found!");
                return;
            }

            if(Kits != null) 
            {
                string[] allKits = (string[]) Kits.Call("GetAllKits");
                foreach (string kit in allKits)
                {   
                    string url = (string) Kits.Call("GetKitImage", kit);
                    bool saved = (bool) ImageLibrary.Call("HasImage", url);
                    if (!saved)
                        ImageLibrary.Call("AddImage", url, url);
                }
                Puts("Downloading Kits Images...");
            }
        }

        private Dictionary<string, string> wp_kits_colors = new Dictionary<string, string>()
        {   
            //buttons
            {"claim", "0.38 0.51 0.16 0.85"},
            {"cooldown", "0.80 0.25 0.16 0.85"},
            {"noPerms", "0.14 0.14 0.14 0.9"},
            {"noUses", "0.14 0.14 0.14 0.6"},
            {"back", "0 0 0 0.6"},
            //panels
            {"main_panel", "0 0 0 0.5"},
            {"secondary_panel", "0 0 0 0.5"},
            {"inventory", "0 0 0 0.6"}
        };

        //kits import grid
        string[] anchors = {
            "0 1", "0 0.8", "0 0.6", "0 0.4", "0 0.2", 
            "0.33 1", "0.33 0.8", "0.33 0.6", "0.33 0.4", "0.33 0.2",
            "0.66 1", "0.66 0.8", "0.66 0.6", "0.66 0.4", "0.66 0.2",
        };

        private string[] anchors2 = {
    
            "0.105 0.7-0.480 0.95",
            "0.520 0.7-0.895 0.95",
            "0.105 0.4-0.480 0.65",
            "0.520 0.4-0.895 0.65",
            "0.105 0.10-0.480 0.35",
            "0.520 0.10-0.895 0.35",
        };

        string[] mainA = {
    
            "0.01 0.75-0.165 0.98", "0.175 0.75-0.330 0.98", "0.340 0.75-0.495 0.98", "0.505 0.75-0.660 0.98", "0.670 0.75-0.825 0.98", "0.835 0.75-0.99 0.98",
            "0.01 0.505-0.165 0.735", "0.175 0.505-0.330 0.735", "0.340 0.505-0.495 0.735", "0.505 0.505-0.660 0.735", "0.670 0.505-0.825 0.735", "0.835 0.505-0.99 0.735",
            "0.01 0.265-0.165 0.495", "0.175 0.265-0.330 0.495", "0.340 0.265-0.495 0.495", "0.505 0.265-0.660 0.495", "0.670 0.265-0.825 0.495", "0.835 0.265-0.99 0.495",
            "0.01 0.02-0.165 0.250", "0.175 0.02-0.330 0.250", "0.340 0.02-0.495 0.250", "0.505 0.02-0.660 0.250", "0.670 0.02-0.825 0.250", "0.835 0.02-0.99 0.250",
        };

        string[] clothA = {
    
            "0.01 0.04-0.14 0.96", "0.15 0.04-0.28 0.96", "0.29 0.04-0.42 0.96", "0.43 0.04-0.56 0.96", "0.57 0.04-0.70 0.96", "0.71 0.04-0.845 0.96", "0.855 0.04-0.99 0.96"
        };

        string[] beltA = {
    
            "0.01 0.04-0.165 0.94", "0.175 0.04-0.330 0.94", "0.340 0.04-0.495 0.94", "0.505 0.04-0.660 0.94", "0.670 0.04-0.825 0.94", "0.835 0.04-0.99 0.94",
        };


        private void ShowKits(BasePlayer player, int page = 0)
        {   
            
            var ui =  new CuiElementContainer();
            var kitList = addonsData["addons"].importedKits;
            if (kitList == null || kitList.Count == 0) 
            {
                CUIClass.CreateText(ref ui, "errortext", "wp_content", "1 1 1 0.6", "Your list with imported kits is empty. \nUse chat command <color=#fffff>/import_kits</color> to add kits.", 18, "0.0 0.00", "1 1", TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", 0.0f);  
                CuiHelper.AddUi(player, ui); 
                return;
            }
            int startingIndex = page * 6;
            for (var i = 0; i < 6; i++)
            {   

                if (startingIndex + i >= kitList.Count) break;

                string[] _anchors = anchors2[i].Split('-');
                string kitname = kitList[startingIndex + i];
                string kitDescr = (string) Kits.Call("GetKitDescription", kitname);

                if (!(bool) Kits.Call("IsKit", kitname)) 
                {   
                    CUIClass.CreateText(ref ui, $"kitpanel_{i}", "wp_content", "1 1 1 0.6", $"Kit <b>'{kitname}'</b> does not exist. Head over to 'oxide/data/WPKitsData.json' and delete this kit from the list. Next time before you delete kit, make you un-import it first.", 10, _anchors[0], _anchors[1], TextAnchor.MiddleCenter, $"robotocondensed-regular.ttf", 0.0f);  
                    continue;
                }

                if (kitDescr.Length > 60) kitDescr = kitDescr.Substring(0, 60) + "...";

                string usesleft = gl("uses_title_unlimited");
                if ((int) Kits.Call("GetKitMaxUses", kitname) != 0)
                    usesleft = $"{(int) Kits.Call("GetKitMaxUses", kitname) - (int) Kits.Call("GetPlayerKitUses", player.userID.Get(), kitname)}";

                string cooldown = gl("cooldown_title_none");
                if ((int) Kits.Call<int>("GetKitCooldown", kitname) != 0)
                    cooldown = $"{TimeFormated(Convert.ToDouble(Kits.Call<int>("GetKitCooldown", kitname)))}";

                CUIClass.CreatePanel(ref ui, $"kitpanel_{i}", "wp_content", config.ui.colors["Secondary Panel"], _anchors[0], _anchors[1], false, 0.0f, 0f, "assets/icons/iconmaterial.mat", "0 0", "0 0");
                    //logo
                    CUIClass.CreatePanel(ref ui, $"kit_icon_panel_{i}", $"kitpanel_{i}", config.ui.colors["Main Panel"], "0.03 0.33", "0.32 0.94", false, 0.0f, 0f, "assets/icons/iconmaterial.mat");
                        CUIClass.CreateImage(ref ui, "img", $"kit_icon_panel_{i}", $"{Img((string)Kits.Call("GetKitImage", kitList[startingIndex + i]))}", "0.05 0.05", "0.95 0.95", 0.2f);
                    //title
                    CUIClass.CreatePanel(ref ui, $"kit_title_panel_{i}", $"kitpanel_{i}",  config.ui.colors["Main Panel"], "0.34 0.33", "0.97 0.94", false, 0.0f, 0f, "assets/icons/iconmaterial.mat");
                        CUIClass.CreateText(ref ui, "kitname", $"kit_title_panel_{i}", "1 1 1 0.6", gl("kit_title_format")
                                                                                                    .Replace("{kit_title}", kitname)
                                                                                                    .Replace("{kit_description}", kitDescr)
                                                                                                    .Replace("{kit_cooldown}", cooldown)
                                                                                                    .Replace("{kit_usesleft}", usesleft), 11, "0.05 0.00", "0.95 1", TextAnchor.MiddleLeft, $"robotocondensed-regular.ttf", 0.0f);  
                    
                    //view button
                    CUIClass.CreateButton(ref ui, $"kit_viewbtn_{i}", $"kitpanel_{i}",  config.ui.colors["Main Panel"],  gl("view_button"), 12, "0.03 0.06", $"0.32 0.28", $"wp_kits_viewkit {kitname.Replace(" ", "%")} {page}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
                    
                    string[] btn_props = Button_Cooldown_Text(player, kitname).Split('%');
                    CUIClass.CreateButton(ref ui, $"kit_claimbtn_{i}", $"kitpanel_{i}",  btn_props[1], btn_props[0], 12, "0.34 0.06", $"0.965 0.28", $"wp_kits_try_claim {kitname.Replace(" ", "%")} 0 {i}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
            }

            for (var i = 0; i < 6; i++)
                CuiHelper.DestroyUi(player, $"kitpanel_{i}");

            CuiHelper.DestroyUi(player, "wpkits_btn_next"); 
            CuiHelper.DestroyUi(player, "wpkits_btn_back"); 
            
            if (page > 0) {
                CUIClass.CreateButton(ref ui, "wpkits_btn_back", "wp_content", config.ui.prevPage.color, config.ui.prevPage.text, 11, config.ui.prevPage.anchorMin, config.ui.prevPage.anchorMax, $"wp_kits_page {page - 1}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
            }
           
            
            if ((double) page + 1 <  ((double) kitList.Count / 6)) {
                //next page button
                CUIClass.CreateButton(ref ui, "wpkits_btn_next", "wp_content", config.ui.nextPage.color, config.ui.nextPage.text, 11, config.ui.nextPage.anchorMin, config.ui.nextPage.anchorMax, $"wp_kits_page {page + 1}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
            }

            CuiHelper.AddUi(player, ui); 
        }

        private void ViewKit(BasePlayer player, string kitname, int page = 0)
        {
            var ui =  new CuiElementContainer();

            JObject kitObj = (JObject) Kits.Call("GetKitObject", $"{kitname}");
            if (kitObj == null)
            {
                CUIClass.CreateText(ref ui, "errortext", "wp_content", "1 1 1 0.6", $"Kit '{kitname}' does not exist.", 18, "0.0 0.00", "1 1", TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", 0.0f);  
                CuiHelper.AddUi(player, ui); 
                return;
            }

            var belt = kitObj.SelectToken("BeltItems");
            var main = kitObj.SelectToken("MainItems");
            var wear = kitObj.SelectToken("WearItems");

            CUIClass.CreatePanel(ref ui, "wpkits_viewkit_inventory", "wp_content", "0 0 0 0.0", "0.4 0", "1 1", false, 0.0f, 0f, "assets/icons/iconmaterial.mat");
                //main
                CUIClass.CreatePanel(ref ui, $"wpkits_viewkit_inventory_main", "wpkits_viewkit_inventory", "0 0 0 0.0", "0.14 0.45", "1 0.99", false, 0.0f, 0f, "assets/icons/iconmaterial.mat");
                for (int i = 0; i < mainA.Count(); i++)
                {   
                    string[] splitA = mainA[i].Split('-');
                    CUIClass.CreatePanel(ref ui, $"wpkits_imain_item{i}", "wpkits_viewkit_inventory_main", config.ui.colors["Inventory Item Box"], splitA[0], splitA[1], false, 0.2f, 0f, "assets/icons/iconmaterial.mat"); 
                    
                    try{
                        var item = main[i];
                        string shortname = $"{item.SelectToken("Shortname")}";
                        var iDef = ItemManager.FindItemDefinition(shortname);

                        ui.Add(new CuiElement
                        {
                            Parent = $"wpkits_imain_item{i}",
                            Name = $"{shortname}_{i}",
                            Components ={ new CuiImageComponent { ItemId = iDef.itemid, SkinId = (ulong) item.SelectToken("SkinID"), FadeIn = 0.2f}, new CuiRectTransformComponent {AnchorMin = "0.13 0.13", AnchorMax = "0.87 0.87"}},
                            FadeOut = 0f
                        });
                        CUIClass.CreateText(ref ui, "amount", $"wpkits_imain_item{i}", "1 1 1 0.6", $"x{item.SelectToken("Amount")}", 11, "0.05 0.00", "0.95 1", TextAnchor.LowerRight, $"robotocondensed-bold.ttf", 0.0f);  
                    
                    }catch{

                    }
                }
            
                CUIClass.CreatePanel(ref ui, "wpkits_viewkit_inventory_belt", "wpkits_viewkit_inventory", "0 0 0 0.0", "0.14 0.28", "1 0.42", false, 0.0f, 0f, "assets/icons/iconmaterial.mat");
                for (int i = 0; i < beltA.Count(); i++)
                {   
                    string[] splitA = beltA[i].Split('-');
                    CUIClass.CreatePanel(ref ui, $"wpkits_imain_item{i}", "wpkits_viewkit_inventory_belt", config.ui.colors["Inventory Item Box"], splitA[0], splitA[1], false, 0.2f, 0f, "assets/icons/iconmaterial.mat"); 
                    //string shortname = $"{item.SelectToken("Shortname")}";
                    //string amount = $"{item.SelectToken("Amount")}";
                    
                    try{
                        var item = belt[i];
                        string shortname = $"{item.SelectToken("Shortname")}";
                        var iDef = ItemManager.FindItemDefinition(shortname);

                        ui.Add(new CuiElement
                        {
                            Parent = $"wpkits_imain_item{i}",
                            Name = $"{shortname}_{i}",
                            Components ={ new CuiImageComponent { ItemId = iDef.itemid, SkinId = (ulong) item.SelectToken("SkinID"), FadeIn = 0.2f}, new CuiRectTransformComponent {AnchorMin = "0.13 0.13", AnchorMax = "0.87 0.87"}},
                            FadeOut = 0f
                        });
                        CUIClass.CreateText(ref ui, "amount", $"wpkits_imain_item{i}", "1 1 1 0.6", $"x{item.SelectToken("Amount")}", 11, "0.05 0.00", "0.95 1", TextAnchor.LowerRight, $"robotocondensed-bold.ttf", 0.0f);  
                    
                    }catch{

                    }
                }

                CUIClass.CreatePanel(ref ui, $"wpkits_viewkit_inventory_wear", "wpkits_viewkit_inventory", "0 0 0 0.0", "0.14 0.13", "1 0.25", false, 0.0f, 0f, "assets/icons/iconmaterial.mat");
                for (int i = 0; i < clothA.Count(); i++)
                {   
                    string[] splitA = clothA[i].Split('-');
                    CUIClass.CreatePanel(ref ui, $"wpkits_imain_item{i}", "wpkits_viewkit_inventory_wear", config.ui.colors["Inventory Item Box"], splitA[0], splitA[1], false, 0.2f, 0f, "assets/icons/iconmaterial.mat"); 
                    //string shortname = $"{item.SelectToken("Shortname")}";
                    //string amount = $"{item.SelectToken("Amount")}";
                    
                    try{
                        var item = wear[i];
                        string shortname = $"{item.SelectToken("Shortname")}";
                        var iDef = ItemManager.FindItemDefinition(shortname);

                        ui.Add(new CuiElement
                        {
                            Parent = $"wpkits_imain_item{i}",
                            Name = $"{shortname}_{i}",
                            Components ={ new CuiImageComponent { ItemId = iDef.itemid, SkinId = (ulong) item.SelectToken("SkinID"), FadeIn = 0.2f}, new CuiRectTransformComponent {AnchorMin = "0.13 0.13", AnchorMax = "0.87 0.87"}},
                            FadeOut = 0f
                        });
                        CUIClass.CreateText(ref ui, "amount", $"wpkits_imain_item{i}", "1 1 1 0.6", $"x{item.SelectToken("Amount")}", 11, "0.05 0.00", "0.95 1", TextAnchor.LowerRight, $"robotocondensed-bold.ttf", 0.0f);  
                    
                    }catch{

                    }
                }
            
            
            
            CUIClass.CreatePanel(ref ui, "wpkits_viewkit_info", "wp_content", "0 0 0 0.0", "0.0 0", "0.4 1", false, 0.0f, 0f, "assets/icons/iconmaterial.mat");
                CUIClass.CreateText(ref ui, "kit_title", "wpkits_viewkit_info", "1 1 1 0.8", gl("kit_title_format_view").Replace("{kit_title}", kitname), 38, "0.05 0.00", "1 0.97", TextAnchor.UpperLeft, $"robotocondensed-bold.ttf", 0.0f);  
                
                string cooldown = gl("cooldown_title_none");
                if ((int) Kits.Call<int>("GetKitCooldown", kitname) != 0)
                    cooldown = $"{TimeFormated(Convert.ToDouble(Kits.Call<int>("GetKitCooldown", kitname)))}";
                CUIClass.CreateText(ref ui, "kit_cooldown", "wpkits_viewkit_info", "1 1 1 0.35",gl("cooldown_title").Replace("{kit_cooldown}", cooldown), 13, "0.055 0.00", "0.95 0.86", TextAnchor.UpperLeft, $"robotocondensed-bold.ttf", 0.0f);  
                
                string usesleft = gl("uses_title_unlimited");
                if ((int) Kits.Call<int>("GetKitMaxUses", kitname) != 0)
                    usesleft = $"{(int) Kits.Call("GetKitMaxUses", kitname) - (int) Kits.Call("GetPlayerKitUses", player.userID.Get(), kitname)}";
                CUIClass.CreateText(ref ui, "kit_usesleft", "wpkits_viewkit_info", "1 1 1 0.35",gl("uses_title").Replace("{kit_uses}", usesleft), 13, "0.055 0.00", "0.95 0.82", TextAnchor.UpperLeft, $"robotocondensed-bold.ttf", 0.0f);  
                
                string cost = gl("price_title_free");
                if(Convert.ToInt32(kitObj.SelectToken("Cost")) != 0)
                    cost = gl("price_title").Replace("{kit_price}",$"{kitObj.SelectToken("Cost")}").Replace("{kit_currency}", addonsData["addons"].RustKits_currencyName); //$"PRICE <color=#ffffffcd>{kitObj.SelectToken("Cost")} {addonsData["addons"].RustKits_currencyName}</color>";
                CUIClass.CreateText(ref ui, "kit_cost", "wpkits_viewkit_info", "1 1 1 0.35", cost, 13, "0.055 0.00", "0.95 0.78", TextAnchor.UpperLeft, $"robotocondensed-bold.ttf", 0.0f);  
                
                string perm = gl("perms_title_no");
                if ((string)kitObj.SelectToken("RequiredPermission") != "")
                    perm = gl("perms_title_yes");              
                CUIClass.CreateText(ref ui, "kit_perm", "wpkits_viewkit_info", "1 1 1 0.35", perm, 13, "0.055 0.00", "0.95 0.54", TextAnchor.UpperLeft, $"robotocondensed-bold.ttf", 0.0f);  

                CUIClass.CreateText(ref ui, "kit_description", "wpkits_viewkit_info", "1 1 1 0.45",(string) Kits.Call("GetKitDescription", kitname), 12, "0.05 0.00", "1 0.45", TextAnchor.UpperLeft, $"robotocondensed-bold.ttf", 0.0f);  
                
                CUIClass.CreateButton(ref ui, "wpkits_btn_back", $"wpkits_viewkit_info", config.ui.colors["Back Button"], gl("back_button"), 11, "0.055 0.15", "0.40 0.225", $"wp_kits_page {page}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
                
                string[] btn_props = Button_Cooldown_Text(player, kitname).Split('%');
                CUIClass.CreateButton(ref ui, "wpkits_btn_claim", $"wpkits_viewkit_info", btn_props[1],  btn_props[0], 11, "0.45 0.15", "0.85 0.225", $"wp_kits_try_claim {kitname.Replace(" ", "%")} 1 0", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");

            for (var i = 0; i < 6; i++)
                CuiHelper.DestroyUi(player, $"kitpanel_{i}");

            CuiHelper.DestroyUi(player, "wpkits_btn_next"); 
            CuiHelper.DestroyUi(player, "wpkits_btn_back"); 
            CuiHelper.DestroyUi(player, "wpkits_viewkit_inventory"); 
            CuiHelper.DestroyUi(player, "wpkits_viewkit_info"); 
            CuiHelper.AddUi(player, ui); 
        }

        private void KitsImportCui(BasePlayer player, int page = 0)
        {   
            string[] allKits = (string[]) Kits.Call("GetAllKits");
            int windowHeight = 0;
            string text = "";
            foreach (var item in allKits)
            {
                windowHeight += 18;
                text += item + "\n";
            }
            var ui =  new CuiElementContainer();


            CUIClass.CreatePanel(ref ui, "wpkits_background", "Overlay", "0 0 0 0.8", "0 0", "1 1", true, 0.0f, 0f, "assets/content/ui/uibackgroundblur.mat", "0 0", "0 0");
            CUIClass.PullFromAssets(ref ui, "overlay", "wpkits_background", "0 0 0 1", "assets/content/ui/ui.background.transparent.radial.psd", 0.0f, 0f, "0 0", "1 1");
            
            CUIClass.CreatePanel(ref ui, "wpkits_main", "wpkits_background", "0.11 0.11 0.11 1", "0.5 0.5", "0.5 0.5", false, 0.1f, 0f, "assets/content/ui/uibackgroundblur.mat", $"-300 -170", "300 170");
            CUIClass.CreatePanel(ref ui, "wpkits_title", "wpkits_main", "0.07 0.07 0.07 1", "0 1", "1 1.1", false, 0.1f, 0f, "assets/content/ui/uibackgroundblur.mat");
                CUIClass.CreateText(ref ui, "titletext", "wpkits_title", "1 1 1 0.6", "IMPORT KITS", 15, "0.02 0.00", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", 0.0f);  
                CUIClass.CreateButton(ref ui, "wpkits_closeimportbtn", "wpkits_title", "0.80 0.25 0.16 0.85", "", 11, "0.945 0.16", $"0.992 0.82", $"wp_kits_closeImportMenu", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
                CUIClass.PullFromAssets(ref ui, "wpkits_closeimportbtn_icon", "wpkits_closeimportbtn", "1 1 1 0.8", "assets/icons/vote_down.png", 0.1f, 0f, "0 0", "1 1");
            
            
            if (page > 0) 
                CUIClass.CreateButton(ref ui, "wpkits_main_btn_back", "wpkits_main", "0.11 0.11 0.11 1", "< BACK", 11, "0 -0.1", $"0.15 -0.02", $"wp_kits_importpage {page - 1}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
            

            if (page < allKits.Length / 14) 
                CUIClass.CreateButton(ref ui, "wpkits_main_btn_back", "wpkits_main", "0.11 0.11 0.11 1", "NEXT >", 11, "0.85 -0.1", $"1 -0.02", $"wp_kits_importpage {page + 1}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
            

            CuiHelper.DestroyUi(player, "wpkits_background"); 
            
            CuiHelper.AddUi(player, ui); 

            int startingindex = page * 14;
            
            for (int i = 0; i < 15; i++)
            {   
                if (startingindex + i > allKits.Length - 1) break;

                Kit_FI(player, allKits[startingindex + i], i, addonsData["addons"].importedKits.Contains(allKits[startingindex + i]));
            }
        }

        private void Kit_FI(BasePlayer player, string kitname, int index, bool alreadyImported = false)
        {   
            var ui2 =  new CuiElementContainer();

            string topOffset = "0";
            string leftOffset = "0";
            string btnColor = "0.38 0.51 0.16 0.85";
            string btnText = "IMPORT KIT";

            if (index == 0 || index == 5 || index == 10) 
                topOffset = "-3";

            if (index < 5) 
                leftOffset = "3";

            if (alreadyImported)
            {
                btnColor = "0.80 0.25 0.16 0.85";
                btnText = "<size=10>ALREADY IMPORTED</size>";
            }
            
            CUIClass.CreatePanel(ref ui2, $"wpkits_kit_FI{index}", "wpkits_main", "0 0 0 0.8", anchors[index], anchors[index], false, 0.0f, 0f, "assets/icons/iconmaterial.mat", $"{leftOffset} -65", $"195 {topOffset}");
                CUIClass.CreateImage(ref ui2, "img", $"wpkits_kit_FI{index}", $"{Img((string)Kits.Call("GetKitImage", kitname))}", "0.02 0.12", "0.27 0.95", 0.5f);
                CUIClass.CreateText(ref ui2, "kitname", $"wpkits_kit_FI{index}", "1 1 1 0.6", kitname, 15, "0.30 0.5", "1 1", TextAnchor.MiddleLeft, $"robotocondensed-bold.ttf", 0.0f);  
                CUIClass.CreateButton(ref ui2, $"wpkits_btnimport_{index}", $"wpkits_kit_FI{index}", btnColor, btnText, 11, "0.29 0.08", "0.775 0.45", $"wp_kits_import {kitname.Replace(" ", "%")} {index}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
                CUIClass.CreateButton(ref ui2, $"wpkits_btn_viewkit", $"wpkits_kit_FI{index}", "0.11 0.11 0.11 1", "", 11, "0.80 0.08", "0.97 0.45", $"wp_kits_lookupkit {kitname.Replace(" ", "%")}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
                    CUIClass.CreateImage(ref ui2, "img", "wpkits_btn_viewkit", Img("https://rustplugins.net/products/welcomepanel/new/file-find.png"), "0.20 0.20", "0.80 0.80", 0.0f);

            CuiHelper.DestroyUi(player, $"wpkits_kit_FI{index}");
            CuiHelper.AddUi(player, ui2); 
        }

        private void PlayFx(BasePlayer player, string fx = "assets/prefabs/deployable/locker/sound/equip_zipper.prefab")
        {
            if (player == null) return;
            if (fx == null) return;

            var EffectInstance = new Effect();
            EffectInstance.Init(Effect.Type.Generic, player, 0, Vector3.up, Vector3.zero);
            EffectInstance.pooledstringid = StringPool.Get(fx);
            NetWrite netWrite = Net.sv.StartWrite();
			netWrite.PacketID(Message.Type.Effect);
			EffectInstance.WriteToStream(netWrite);
			netWrite.Send(new SendInfo(player.net.connection));
			EffectInstance.Clear();
        }

        private string TimeFormated(double cooldown) 
        {
            TimeSpan cooldownTS = TimeSpan.FromSeconds(cooldown); 
            string cooldownFormated = string.Format("{0:D1} DAYS {1:D1} HOURS", cooldownTS.Days, cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
            
            //bellow one minute
            if (cooldown < 59) {
                cooldownFormated = string.Format("{2:D1} SECONDS", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            }

            //if 1 minute
            if (cooldown < 119) {

                if (cooldownTS.Seconds == 0) {
                    cooldownFormated = string.Format("{1:D1}MINUTE", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                    return cooldownFormated;
                }
                       
                cooldownFormated = string.Format("{1:D1}MINUTE {2:D1} SECONDS", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            }

            
            //bellow 10 minutes
            if (cooldown < 559) {

                if (cooldownTS.Seconds == 0) {
                    cooldownFormated = string.Format("{1:D1}MINUTES", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                    return cooldownFormated;
                }
                       
                cooldownFormated = string.Format("{1:D1}MINUTES {2:D1} SECONDS", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            }
            
            //bellow one hour
            if (cooldown < 3599) {
                
                cooldownFormated = string.Format("{1:D1} MINUTES", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            }

            //if 1 hour
            if (cooldown < 7199)
            {
                if (cooldownTS.Minutes == 0) {
                    cooldownFormated = string.Format("{0:D1} HOUR", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                    return cooldownFormated;
                }

                cooldownFormated = string.Format("{0:D1} HOUR {1:D1} MINUTES", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            }

            //above one hour
            if (cooldown > 3599 && cooldown < 86399) {

                if (cooldownTS.Minutes == 0) {
                    cooldownFormated = string.Format("{0:D1} HOURS", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                    return cooldownFormated;
                }

                cooldownFormated = string.Format("{0:D1} HOURS {1:D1} MINUTES", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            }

            //bellow one day
            if (cooldown < 86399) {

                if (cooldownTS.Minutes == 0) {
                    cooldownFormated = string.Format("{0:D1} HOURS", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                    return cooldownFormated;
                }

                cooldownFormated = string.Format("{0:D1}HOURS {1:D1}MINUTES", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            } 

            if (cooldownTS.Days == 1)
            {
                string.Format("{0:D1}DAY {1:D1}HOURS", cooldownTS.Days, cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                return cooldownFormated;
            }

            string.Format("{0:D1}DAY {1:D1}HOURS", cooldownTS.Days, cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
            return cooldownFormated;
        }

        private string Button_Cooldown_Text(BasePlayer player, string kitname)
        {
            var kit = (JObject) Kits.Call("GetKitObject", kitname);
            int cost = Convert.ToInt32(kit.SelectToken("Cost"));
            string perm = (string) kit.SelectToken("RequiredPermission");
            int kitUseLimit = Convert.ToInt32(Kits?.CallHook("GetKitMaxUses", kitname));
            int kitTimesUsed = Convert.ToInt32(Kits?.CallHook("GetPlayerKitUses", player.userID.Get(), kitname));
             
            if (IsKitOnCd(player, kitname))
            {   
                return gl("on_cooldown_button").Replace("{kit_cooldown}", $"{GetCurrentKitCd(player, kitname)}") + $"%{config.ui.colors["Cooldown Button"]}";
            }   
            else
            {   
                if (perm != "" && perm != null)
                {
                    if (!permission.UserHasPermission(player.UserIDString, perm)) 
                        return gl("no_perms_button") + $"%{config.ui.colors["No Permissions Button"]}";
                }
                
                if (kitUseLimit != 0)
                {   
                    if (kitUseLimit - kitTimesUsed < 1)
                        return gl("no_uses_button") + $"%{config.ui.colors["No Uses Left Button"]}";
                }
                
                if (cost != 0)
                {
                        return gl("paid_kit_button").Replace("{kit_price}",$"{cost}").Replace("{kit_currency}",$"{addonsData["addons"].RustKits_currencyName}") + $"%{config.ui.colors["Claim Button"]}";
                }
                
                return gl("claim_button") + $"%{config.ui.colors["Claim Button"]}";
            }
        }

        private string KitUsesLeft(BasePlayer player, string _kitName) 
        {   
            int _kitMaxUses = Convert.ToInt32(Kits?.CallHook("GetKitMaxUses", _kitName));
            int _kitTimesUsed = Convert.ToInt32(Kits?.CallHook("GetPlayerKitUses", player.userID.Get(), _kitName));
            string _kitUsesLeft = Convert.ToString(_kitMaxUses - _kitTimesUsed);
            return _kitUsesLeft;
        }

        private bool IsKitOnCd(BasePlayer player, string _kitName) 
        {
            double _currentCd = Convert.ToDouble(Kits?.CallHook("GetPlayerKitCooldown", player.userID.Get(), _kitName));
            if ( _currentCd == 0)
            {
                return false;
            }
            return true;
        }

        private string GetKitCd(string _kitName) 
        {
            int _kitCdInt = Convert.ToInt32(Kits?.Call<int>("GetKitCooldown", _kitName)); 
        
            TimeSpan cooldownTS = TimeSpan.FromSeconds(_kitCdInt); 
            string cooldownFormated = string.Format("{0:D1}D {1:D2}:{2:D2}:{3:D2}", cooldownTS.Days, cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
            if (_kitCdInt < 86400) cooldownFormated = string.Format("{0:D2}:{1:D2}:{2:D2}", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
            
            string _kitCd = $"{cooldownFormated}";
            return _kitCd; 
        }

        private string GetCurrentKitCd(BasePlayer player, string _kitName) 
        { 
            int _currentCdInt = Convert.ToInt32(Kits?.CallHook("GetPlayerKitCooldown", player.userID.Get(), _kitName));
            
            TimeSpan cooldownTS = TimeSpan.FromSeconds(_currentCdInt); 
            string cooldownFormated = string.Format("{0:D1}D {1:D2}:{2:D2}:{3:D2}", cooldownTS.Days, cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
            if (_currentCdInt < 86400) cooldownFormated = string.Format("{0:D2}:{1:D2}:{2:D2}", cooldownTS.Hours, cooldownTS.Minutes, cooldownTS.Seconds);
                        
            string _currentCd = $"{cooldownFormated}";
            return _currentCd; 
        }

        [ConsoleCommand("wp_kits_try_claim")]
        private void wp_kits_try_claim(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (player == null) return;
            if (args.Length != 3) return;

            string kitname = args[0].Replace("%", " ");

            if ((bool) Kits.Call("TryClaimKit", player, kitname, false))
            {   

                string[] btn_props = Button_Cooldown_Text(player, kitname).Split('%');
                var ui =  new CuiElementContainer();

                if (args[1] == "0") {
                    CUIClass.CreateButton(ref ui, $"kit_claimbtn_{args[2]}", $"kitpanel_{args[2]}",  btn_props[1], btn_props[0], 12, "0.34 0.06", $"0.965 0.28", $"wp_kits_try_claim {kitname} {args[2]}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
                    CuiHelper.DestroyUi(player, $"kit_claimbtn_{args[2]}"); 
                }
                else {
                    CUIClass.CreateButton(ref ui, "wpkits_btn_claim", $"wpkits_viewkit_info", btn_props[1],  btn_props[0], 11, "0.45 0.15", "0.85 0.225", $"wp_kits_try_claim {kitname} {args[2]}", "", "1 1 1 0.7", 0f, TextAnchor.MiddleCenter, $"robotocondensed-bold.ttf", "assets/icons/iconmaterial.mat");
                    CuiHelper.DestroyUi(player, "wpkits_btn_claim"); 
                }
                CuiHelper.AddUi(player, ui); 

                if (config.ui.closeAfterClaim) {
                    CuiHelper.DestroyUi(player, "background"); 
                    CuiHelper.DestroyUi(player, "WelcomePanel_background"); 
                }

                /* if (Notifications != null)
                    Notifications.Call("Run",
                        player,
                        5,
                        gl("notification_kitclaim_success").Replace("{kitname}", kitname),
                        gl("notification_success_stripe_color"),
                        Img((string)Kits.Call("GetKitImage", kitname)),
                        true
                    ); */

                PlayFx(player, "assets/prefabs/deployable/locker/sound/equip_zipper.prefab");
                return;
            }

            PlayFx(player, "assets/bundled/prefabs/fx/notice/item.select.fx.prefab");
        }

        [ConsoleCommand("wp_kits_viewkit")]
        private void wp_kits_viewkit(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (player == null) return;
            if (args.Length != 2) return;

            string kitname = args[0].Replace("%", " ");
            ViewKit(player, kitname, Convert.ToInt32(args[1]));
        }

        [ConsoleCommand("wp_kits_page")]
        private void wp_kits_page(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (player == null) return;
            if (args.Length != 1) {
                Puts("Import command error, passed arguments are wrong");
                return;
            }

            
            CuiHelper.DestroyUi(player, "wpkits_viewkit_inventory"); 
            CuiHelper.DestroyUi(player, "wpkits_viewkit_info"); 
            ShowKits(player, Convert.ToInt32(args[0]));

        }

        [ConsoleCommand("wp_kits_importpage")]
        private void wp_kits_importpage(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (player == null) return;
            if (!player.IsAdmin) return;
            if (args.Length != 1) {
                Puts("Import command error, passed arguments are wrong");
                return;
            }

           KitsImportCui(player, Convert.ToInt32(args[0]));

        }

        [ConsoleCommand("wp_kits_lookupkit")]
        private void wp_kits_lookupkit(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (player == null) return;
            if (!player.IsAdmin) return;
            if (args.Length != 1) {
                Puts("Import command error, passed arguments are wrong");
                return;
            }

            string kitname = args[0].Replace("%", " ");
            CuiHelper.DestroyUi(player, "wpkits_background"); 
            string[] _args = {"edit", kitname};
            Kits.Call("cmdKit", player, addonsData["addons"].RustKits_claimCommand, _args);

        }

        [ConsoleCommand("wp_kits_closeImportMenu")]
        private void wp_kits_closeImportMenu(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            if (player == null) return;
            CuiHelper.DestroyUi(player, "wpkits_background"); 

        }

        [ConsoleCommand("wp_kits_import")]
        private void wp_kits_import(ConsoleSystem.Arg arg)
        {
            var player = arg?.Player();
            var args = arg.Args;
            if (player == null) return;
            if (!player.IsAdmin) return;
            if (args.Length != 2) {
                Puts("Import command error, passed arguments are wrong");
                return;
            }

            string kitname = args[0].Replace("%", " ");

            if (addonsData["addons"].importedKits == null) {
                Puts("Kits data error.");
                return;
            }

            if (addonsData["addons"].importedKits.Contains(kitname)) {
                Puts($"Removing {kitname} kit from imported list.");
                addonsData["addons"].importedKits.Remove(kitname);
                Kit_FI(player, kitname, Convert.ToInt32(args[1]), false);
                
            }
            else
            {
                Puts($"Importing {kitname} kit into addon data.");
                addonsData["addons"].importedKits.Add(kitname);
                Kit_FI(player, kitname, Convert.ToInt32(args[1]), true);
            }
            SaveData();
            Puts("Success, data saved. Please reload plugin to reflect changes.");

        }

        [ChatCommand("import_kits")]
        private void import_kits(BasePlayer player)
        {
            if (player == null) return;
            if (!player.IsAdmin) return;
            KitsImportCui(player);
        }

        public class CUIClass
        {
            public static CuiElementContainer CreateOverlay(string _name, string _color, string _anchorMin, string _anchorMax, bool _cursorOn = false, float _fade = 0f, string _mat = "")
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

            public static void CreatePanel(ref CuiElementContainer _container, string _name, string _parent, string _color, string _anchorMin, string _anchorMax, bool _cursorOn = false, float _fadeIn = 0f, float _fadeOut = 0f, string _mat2 = "", string _OffsetMin = "", string _OffsetMax = "", bool keyboard = false)
            {
                _container.Add(new CuiPanel
                {
                    Image = { Color = _color, Material = _mat2, FadeIn = _fadeIn },
                    RectTransform = { AnchorMin = _anchorMin, AnchorMax = _anchorMax, OffsetMin = _OffsetMin, OffsetMax = _OffsetMax },
                    FadeOut = _fadeOut,
                    CursorEnabled = _cursorOn,
                    KeyboardEnabled = keyboard
                },
                _parent,
                _name);
            }

            public static void CreateImage(ref CuiElementContainer _container, string _name, string _parent, string _image, string _anchorMin, string _anchorMax, float _fadeIn = 0f, float _fadeOut = 0f, string _OffsetMin = "", string _OffsetMax = "")
            {
                if (_image.StartsWith("http") || _image.StartsWith("www"))
                {
                    _container.Add(new CuiElement
                    {
                        Name = _name,
                        Parent = _parent,
                        FadeOut = _fadeOut,
                        Components =
                        {
                            new CuiRawImageComponent { Url = _image, Sprite = "assets/content/textures/generic/fulltransparent.tga", FadeIn = _fadeIn},
                            new CuiRectTransformComponent { AnchorMin = _anchorMin, AnchorMax = _anchorMax, OffsetMin = _OffsetMin, OffsetMax = _OffsetMax }
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
                            new CuiRawImageComponent { Png = _image, Sprite = "assets/content/textures/generic/fulltransparent.tga", FadeIn = _fadeIn},
                            new CuiRectTransformComponent { AnchorMin = _anchorMin, AnchorMax = _anchorMax }
                        }
                    });
                }
            }

            public static void PullFromAssets(ref CuiElementContainer _container, string _name, string _parent, string _color, string _sprite, float _fadeIn = 0f, float _fadeOut = 0f, string _anchorMin = "0 0", string _anchorMax = "1 1", string _material = "assets/icons/iconmaterial.mat")
            {
                //assets/content/textures/generic/fulltransparent.tga MAT
                _container.Add(new CuiElement
                {
                    Parent = _parent,
                    Name = _name,
                    Components =
                            {
                                new CuiImageComponent { Material = _material, Sprite = _sprite, Color = _color, FadeIn = _fadeIn},
                                new CuiRectTransformComponent {AnchorMin = _anchorMin, AnchorMax = _anchorMax}
                            },
                    FadeOut = _fadeOut
                });
            }

            public static void CreateInput(ref CuiElementContainer _container, string _name, string _parent, string _color, int _size, string _anchorMin, string _anchorMax, string _defaultText, string _font = "permanentmarker.ttf", string _command = "command.processinput", TextAnchor _align = TextAnchor.MiddleCenter, int _charsLimit = 200)
            {
                _container.Add(new CuiElement
                {
                    Parent = _parent,
                    Name = _name,

                    Components =
                    {
                        new CuiInputFieldComponent
                        {

                            Text = _defaultText,
                            CharsLimit = _charsLimit,
                            Color = _color,
                            IsPassword = false,
                            Command = _command,
                            Font = _font,
                            FontSize = _size,
                            Align = _align,
                            ReadOnly = true,
                        },

                        new CuiRectTransformComponent
                        {
                            AnchorMin = _anchorMin,
                            AnchorMax = _anchorMax

                        }

                    },
                });
            }

            public static void CreateText(ref CuiElementContainer _container, string _name, string _parent, string _color, string _text, int _size, string _anchorMin, string _anchorMax, TextAnchor _align = TextAnchor.MiddleCenter, string _font = "robotocondensed-bold.ttf", float _fadeIn = 0f, float _fadeOut = 0f, string _outlineColor = "0 0 0 0", string _outlineScale = "0 0")
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
                            FadeIn = _fadeIn,
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
                    FadeOut = _fadeOut
                });
            }

            public static void CreateButton(ref CuiElementContainer _container, string _name, string _parent, string _color, string _text, int _size, string _anchorMin, string _anchorMax, string _command = "", string _close = "", string _textColor = "0.843 0.816 0.78 1", float _fade = 1f, TextAnchor _align = TextAnchor.MiddleCenter, string _font = "", string _material = "assets/content/ui/uibackgroundblur-ingamemenu.mat")
            {

                _container.Add(new CuiButton
                {
                    Button = { Close = _close, Command = _command, Color = _color, Material = _material, FadeIn = _fade },
                    RectTransform = { AnchorMin = _anchorMin, AnchorMax = _anchorMax },
                    Text = { Text = _text, FontSize = _size, Align = _align, Color = _textColor, Font = _font, FadeIn = _fade }
                },
                _parent,
                _name);
            }
        }
        private void SaveData()
        {
            if (addonsData != null)
            Interface.Oxide.DataFileSystem.WriteObject($"WPKitsData", addonsData);
        }
        
        private Dictionary<string, AddonsData> addonsData;
        
        private class AddonsData
        {    
            public string RustKits_claimCommand;
            public string RustKits_currencyName;
            public List<string> importedKits = new List<string>{};  
        }
        
        private void LoadData()
        {
            if (Interface.Oxide.DataFileSystem.ExistsDatafile($"WPKitsData"))
            {
                addonsData = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, AddonsData>>($"WPKitsData");
            }
            else
            {
                addonsData = new Dictionary<string, AddonsData>();            
                SaveData();
            }
        }
        
        private Configuration config;
        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<Configuration>();
            SaveConfig();
        }
        
        protected override void LoadDefaultConfig() => config = Configuration.CreateConfig();
        
        protected override void SaveConfig() => Config.WriteObject(config);     
        
        class Configuration
        {   
            [JsonProperty(PropertyName = "Additional UI Options")]
            public Ui ui { get; set; }

            public class Ui
            {   
                [JsonProperty("Close WelcomePanel after claiming kit")]
                public bool closeAfterClaim { get; set; }

                [JsonProperty("Kits UI Colors")]
                public Dictionary<string, string> colors { get; set; }

                [JsonProperty("Previous Page Button")]
                public PageBtn prevPage { get; set; }

                [JsonProperty("Next Page Button")]
                public PageBtn nextPage { get; set; }

                public class PageBtn 
                {
                    [JsonProperty("Button Color")]
                    public string color { get; set; }

                    [JsonProperty("Button Text")]
                    public string text { get; set; }

                    [JsonProperty("Anchor Min")]
                    public string anchorMin { get; set; }

                    [JsonProperty("Anchor Max")]
                    public string anchorMax { get; set; }

                    [JsonProperty("Image")]
                    public string image { get; set; }
                }
            } 
        
            public static Configuration CreateConfig()
            {
                return new Configuration
                {   
                    ui = new WPKits.Configuration.Ui 
                    {   
                        closeAfterClaim = false,
                        colors = new Dictionary<string, string>()
                        {   
                            {"Claim Button", "0.38 0.51 0.16 0.85"},
                            {"Cooldown Button", "0.80 0.25 0.16 0.85"},
                            {"No Permissions Button", "0.14 0.14 0.14 0.9"},
                            {"No Uses Left Button", "0.14 0.14 0.14 0.6"},
                            {"Back Button", "0 0 0 0.6"},
                            {"Main Panel", "0 0 0 0.5"},
                            {"Secondary Panel", "0 0 0 0.5"},
                            {"Inventory Item Box", "0 0 0 0.6"}
                        },
                        prevPage = new WPKits.Configuration.Ui.PageBtn
                        {
                            color = "0 0 0 0",
                            text = "<size=60>‹</size>",
                            anchorMin = "0.03 0.43",
                            anchorMax = "0.07 0.62",
                            image = ""
                        },
                        nextPage = new WPKits.Configuration.Ui.PageBtn
                        {
                            color = "0 0 0 0",
                            text = "<size=60>›</size>",
                            anchorMin = "0.93 0.43",
                            anchorMax = "0.97 0.62",
                            image = ""
                        }
                    }
                };
            }
        }
    
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {   
                //buttons
                ["claim_button"] = "CLAIM KIT",
                ["claim_button_2"] = "CLAIM KIT",
                ["view_button"] = "VIEW",
                ["back_button"] = "BACK",
                ["no_perms_button"] = "NO PERMISSIONS",
                ["no_uses_button"] = "NO USES LEFT",
                ["on_cooldown_button"] = "{kit_cooldown}",
                ["paid_kit_button"] = "{kit_price} {kit_currency}",
                //first title
                ["kit_title_format"] = "<b><size=16>{kit_title}</size></b>\n{kit_description}",
                //second title
                ["kit_title_format_view"] = "{kit_title}",
                //cooldown 
                ["cooldown_title"] = "COOLDOWN ON USE <color=#ffffffcd>{kit_cooldown}</color>",
                ["cooldown_title_none"] = "<color=#ffffffcd>NONE</color>",
                //uses
                ["uses_title"] = "USES LEFT <color=#ffffffcd>{kit_uses}</color>",
                ["uses_title_unlimited"] = "<color=#ffffffcd>UNLIMITED</color>",
                //price
                ["price_title"] = "PRICE <color=#ffffffcd>{kit_price} {kit_currency}</color>",
                ["price_title_free"] =  $"PRICE <color=#ffffffcd>FREE</color>",
                //perms
                ["perms_title_yes"] = "REQUIRES VIP <color=#ffffffcd>YES</color>",
                ["perms_title_no"] = "REQUIRES VIP <color=#ffffffcd>NO</color>",
                //notifications
                ["notification_kitclaim_success"] = "<size=14>You successfuly claimed <b>{kitname}</b>.</size>",
                ["notification_success_stripe_color"] = "0.482 0.675 0.251 1.00",
            }, this);
        }

        private string gl(string message) => lang.GetMessage(message, this);


    }
}