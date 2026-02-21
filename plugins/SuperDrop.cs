using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Facepunch;

namespace Oxide.Plugins
{
    [Info("SuperDrop", "Mestre Pardal", "1.10.4")]
    [Description("SuperDrop (tier 1), MegaDrop (tier 2) e UltraDrop (tier 3) com conversão leve (event-driven), permissões por tier, cancel por botão direito e comandos pós-entrega.")]
    public class SuperDrop : RustPlugin
    {
        #region Config & Data

        private PluginConfig config;

        private readonly HashSet<BaseEntity> specialSupplySignals = new HashSet<BaseEntity>();
        private readonly Dictionary<ulong, double> lastDropTime = new Dictionary<ulong, double>();

        private readonly Dictionary<ulong, double> _cancelNextThrowUntil = new Dictionary<ulong, double>();
        private readonly HashSet<ulong> _cancelHold = new HashSet<ulong>();
        private readonly Dictionary<ulong, double> _lastCancelMsg = new Dictionary<ulong, double>();

        private readonly Dictionary<ulong, ulong> _signalOwner = new Dictionary<ulong, ulong>();

        private StoredData _data;

        private class StoredData
        {
            public HashSet<ulong> AutoEnabled = new HashSet<ulong>();
        }

        private void LoadData()
        {
            try { _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData(); }
            catch { _data = new StoredData(); }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private string Msg(string key, BasePlayer player = null) => lang.GetMessage(key, this, player?.UserIDString);

        private static readonly string PERM_SUPER_CANON = "superdrop.use";
        private static readonly string PERM_MEGA_CANON  = "superdrop.mega";
        private static readonly string PERM_ULTRA_CANON = "superdrop.ultra";

        private static readonly string PERM_MEGA_LEGACY  = "megadrop.use";
        private static readonly string PERM_ULTRA_LEGACY = "ultradrop.use";

        public class PluginConfig
        {
            public int RequiredAmount = 10;
            public ulong RewardSkinID = 3537089187;
            public float HackSeconds = 60f;
            public float SignalToCrateSeconds = 5f;
            public float DropCooldownSeconds = 20f;

            public List<CustomLootItem> Loot = new List<CustomLootItem>();
            public List<string> ConsoleCommandsAfterDelay = new List<string>();

            public MegaTierConfig Mega = new MegaTierConfig();
            public UltraTierConfig Ultra = new UltraTierConfig();

            public string PermissionSuper = "superdrop.use";
            public string PermissionMega  = "superdrop.mega";
            public string PermissionUltra = "superdrop.ultra";

            public bool DefaultAutoEnabled = true;
        }

        public class MegaTierConfig
        {
            public int RequiredAmountFromSuper = 10;
            public ulong SkinID = 3545811120;
            public float HackSeconds = 90f;
            public float SignalToCrateSeconds = 5f;
            public List<CustomLootItem> Loot = new List<CustomLootItem>();
            public List<string> ConsoleCommandsAfterDelay = new List<string>();
        }

        public class UltraTierConfig
        {
            public int RequiredAmountFromMega = 10;
            public ulong SkinID = 3546461842;
            public float HackSeconds = 120f;
            public float SignalToCrateSeconds = 5f;
            public List<CustomLootItem> Loot = new List<CustomLootItem>();
            public List<string> ConsoleCommandsAfterDelay = new List<string>();
        }

        public class CustomLootItem
        {
            public string DisplayName;
            public string ShortName;
            public ulong Skin;
            public int MinAmount;
            public int MaxAmount;
            public float Chance;
        }

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig
            {
                RequiredAmount = 10,
                RewardSkinID = 3537089187,
                HackSeconds = 60f,
                SignalToCrateSeconds = 5f,
                DropCooldownSeconds = 20f,
                Loot = new List<CustomLootItem>
                {
                    new CustomLootItem { DisplayName = "Flare for EASY", ShortName = "flare", Skin = 2888602635, MinAmount = 1, MaxAmount = 1, Chance = 1f },
                    new CustomLootItem { DisplayName = "Epic Loot", ShortName = "ducttape", Skin = 3352780003, MinAmount = 75, MaxAmount = 175, Chance = 0.8f }
                },
                ConsoleCommandsAfterDelay = new List<string>(),
                Mega = new MegaTierConfig
                {
                    RequiredAmountFromSuper = 10,
                    SkinID = 3545811120,
                    HackSeconds = 90f,
                    SignalToCrateSeconds = 5f,
                    Loot = new List<CustomLootItem>
                    {
                        new CustomLootItem { DisplayName = "Mega Components", ShortName = "metalpipe", Skin = 0, MinAmount = 15, MaxAmount = 30, Chance = 1f },
                        new CustomLootItem { DisplayName = "HQM Stack", ShortName = "metal.refined", Skin = 0, MinAmount = 50, MaxAmount = 100, Chance = 0.9f },
                        new CustomLootItem { DisplayName = "C4 Gift", ShortName = "explosive.timed", Skin = 0, MinAmount = 1, MaxAmount = 2, Chance = 0.4f }
                    },
                    ConsoleCommandsAfterDelay = new List<string>()
                },
                Ultra = new UltraTierConfig
                {
                    RequiredAmountFromMega = 10,
                    SkinID = 3546461842,
                    HackSeconds = 120f,
                    SignalToCrateSeconds = 5f,
                    Loot = new List<CustomLootItem>
                    {
                        new CustomLootItem { DisplayName = "Rocket Pack", ShortName = "ammo.rocket.basic", Skin = 0, MinAmount = 2, MaxAmount = 4, Chance = 0.8f },
                        new CustomLootItem { DisplayName = "Explosives", ShortName = "explosives", Skin = 0, MinAmount = 20, MaxAmount = 40, Chance = 1f },
                        new CustomLootItem { DisplayName = "MLRS Rocket", ShortName = "ammo.rocket.mlrs", Skin = 0, MinAmount = 1, MaxAmount = 2, Chance = 0.35f }
                    },
                    ConsoleCommandsAfterDelay = new List<string>()
                },
                PermissionSuper = "superdrop.use",
                PermissionMega  = "superdrop.mega",
                PermissionUltra = "superdrop.ultra",
                DefaultAutoEnabled = true
            };
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            config = Config.ReadObject<PluginConfig>();
            if (config == null) { LoadDefaultConfig(); return; }

            bool changed = false;
            if (config.Mega == null)  { config.Mega  = new MegaTierConfig();  changed = true; }
            if (config.Ultra == null) { config.Ultra = new UltraTierConfig(); changed = true; }
            if (config.ConsoleCommandsAfterDelay == null) { config.ConsoleCommandsAfterDelay = new List<string>(); changed = true; }
            if (config.Mega.ConsoleCommandsAfterDelay == null) { config.Mega.ConsoleCommandsAfterDelay = new List<string>(); changed = true; }
            if (config.Ultra.ConsoleCommandsAfterDelay == null){ config.Ultra.ConsoleCommandsAfterDelay = new List<string>(); changed = true; }

            config.PermissionSuper = NormalizePerm(config.PermissionSuper, PERM_SUPER_CANON, ref changed);
            config.PermissionMega  = NormalizePerm(config.PermissionMega,  PERM_MEGA_CANON,  ref changed);
            config.PermissionUltra = NormalizePerm(config.PermissionUltra, PERM_ULTRA_CANON, ref changed);

            if (changed) SaveConfig();
        }

        private string NormalizePerm(string perm, string fallback, ref bool changed)
        {
            if (string.IsNullOrWhiteSpace(perm)) { changed = true; return fallback; }
            perm = perm.Trim();
            if (!perm.StartsWith("superdrop.")) { changed = true; return fallback; }
            return perm;
        }

        protected override void SaveConfig() => Config.WriteObject(config);

        #endregion

        #region Messages

        private void LoadDefaultMessages()
        {
            // PT-BR
            var pt = new Dictionary<string, string>
            {
                ["AutoConvert"]         = "<color=#001c71>[SUPERDROP]</color> {0} <color=#ffff00>Sinalizadores</color> = <color=#001c71>1 SuperDrop Signal</color>",
                ["SuperDropActivated"]  = "<color=#001c71>[SUPERDROP]</color> Lançado! Caixa chega em <color=#00ffff>{0}s...</color>",
                ["SuperDropDelivered"]  = "<color=#001c71>[SUPERDROP]</color> <color=#00FF88>Entregue!</color> <color=#FFFFFF>Hack:</color> <color=#00ffff>{0}s...</color>",
                ["SuperDropGlobal"]     = "<color=#001c71>[SUPERDROP]</color> entregue em <color=#FFD54F>{0}</color>.",
                ["AutoConvertMega"]     = "<color=#ff00ff>[MEGADROP]</color> {0} <color=#001c71>SuperDrops</color> = <color=#ff00ff>1 MegaDrop Signal</color></b>!",
                ["MegaDropActivated"]   = "<color=#ff00ff>[MEGADROP]</color> Lançado! Caixa chega em <color=#00ffff>{0}s...</color>",
                ["MegaDropDelivered"]   = "<color=#ff00ff>[MEGADROP]</color> <color=#00FF88>Entregue!</color> <color=#FFFFFF>Hack:</color> <color=#00ffff>{0}s...</color>",
                ["MegaDropGlobal"]      = "<color=#ff00ff>[MEGADROP]</color> entregue em <color=#FFD54F>{0}</color>.",
                ["AutoConvertUltra"]    = "<color=#cc0000>[ULTRADROP]</color> {0} <color=#ff00ff>MegaDrops</color> = <color=#cc0000>1 UltraDrop Signal</color></b>!",
                ["UltraDropActivated"]  = "<color=#cc0000>[ULTRADROP]</color> Lançado! Caixa chega em <color=#00ffff>{0}s...</color>",
                ["UltraDropDelivered"]  = "<color=#cc0000>[ULTRADROP]</color> <color=#00FF88>Entregue!</color> <color=#FFFFFF>Hack:</color> <color=#00ffff>{0}s...</color>",
                ["UltraDropGlobal"]     = "<color=#cc0000>[ULTRADROP]</color> entregue em <color=#FFD54F>{0}</color>.",
                ["Cooldown"]            = "<color=#FF5252>Cooldown:</color> aguarde <b>{0}s</b> para lançar outro drop.",
                ["AdminOnly"]           = "<color=#FF5555>Apenas administradores podem usar este comando!</color>",
                ["GaveSignals"]         = "<color=#00FF88>Você recebeu</color> <b>{0}</b> <color=#00FF88>sinalizadores!</color>",
                ["ErrorGiveSignals"]    = "<color=#FF5555>Erro ao criar os sinalizadores!</color>",
                ["InfoHeader"]          = "<color=#00FFFF>═╣</color> <b>SuperDrop - Informações</b> <color=#00FFFF>╠═</color>",
                ["InfoRequired"]        = "<color=#FFD54F>• Sinalizadores necessários:</color> <b>{0}</b>",
                ["InfoDeliveryTime"]    = "<color=#FFD54F>• Tempo para entrega:</color> <b>{0}s</b>",
                ["InfoHackTime"]        = "<color=#FFD54F>• Tempo de hack:</color> <b>{0}s</b>",
                ["InfoItemsCount"]      = "<color=#FFD54F>• Itens na caixa:</color> <b>{0}</b>",
                ["HelpHeader"]          = "<color=#00FFFF>═╣</color> <b>SuperDrop v1.10.4</b> <color=#00FFFF>╠═</color>",
                ["HelpCommandsTitle"]   = "<color=#FFFF00>Comandos:</color>",
                ["HelpCommandInfo"]     = "<color=#FFFFFF>• /superdrop info</color> <color=#AAAAAA>– Informações do plugin</color>",
                ["HelpCommandGive"]     = "<color=#FFFFFF>• /superdrop give [qtd]</color> <color=#AAAAAA>– Admin: dar sinalizadores</color>",
                ["HelpUsageTitle"]      = "<color=#FFFF00>Como usar:</color>",
                ["HelpUsageCollect"]    = "<color=#FFFFFF>• Colete <b>{0}</b> sinalizadores normais</color>",
                ["HelpUsageAutoConvert"]= "<color=#FFFFFF>• Conversão automática <i>(não mistura skins desconhecidas)</i></color>",
                ["HelpUsageThrow"]      = "<color=#FFFFFF>• Lance o <b>Super/Mega/Ultra Signal</b> onde quiser a caixa</color>",
                ["HelpUsageWait"]       = "<color=#FFFFFF>• Aguarde o tempo de entrega/hack conforme o tier</color>",
                ["InvalidCommand"]      = "<color=#FF5555>Comando inválido!</color> <color=#AAAAAA>Use</color> <b>/superdrop</b> <color=#AAAAAA>para ajuda.</color>",
                ["PermissionRequired"]  = "<color=#AAAAAA>Permissão necessária:</color> <b>{0}</b>",
                ["ThrowBlockedBuilding"]= "<color=#FF5555>Você não pode lançar aqui (área de construção)!</color>",
                ["NoTierPermission"]    = "<color=#FF5555>Sem permissão</color> para <b>{0}</b> <color=#AAAAAA>({1})</color>.",
                ["ErrorCreateCrate"]    = "<color=#FF5555>Falha ao criar a caixa hackeável.</color>",
                ["ErrorNoHackableComponent"] = "<color=#FF5555>Componente <b>HackableLockedCrate</b> não encontrado.</color>",
                ["WarningItemNotFound"] = "<color=#FFA000>Item não encontrado:</color> <b>{0}</b>",
                ["WarningInsertItemFail"]= "<color=#FFA000>Falha ao inserir item:</color> <b>{0}</b>",
                ["ThrowCanceled"]       = "<color=#AAAAAA>Lançamento cancelado.</color>",
                ["MixedSkinsAbort"]     = "Conversão ignorada: há um sinalizador com skin desconhecida.",
                ["AutoStatusOn"]        = "Auto-conversão está <color=#00ff88>LIGADA</color> para você.",
                ["AutoStatusOff"]       = "Auto-conversão está <color=#ff5555>DESLIGADA</color> para você.",
                ["AutoToggledOn"]       = "Auto-conversão <color=#00ff88>ativada</color>.",
                ["AutoToggledOff"]      = "Auto-conversão <color=#ff5555>desativada</color>.",
                ["ManualConvertDone"]   = "Conversão manual concluída (se possível).",
                ["NothingToConvert"]    = "Nada para converter."
            };

            // EN (default)
            var en = new Dictionary<string, string>
            {
                ["AutoConvert"]         = "<color=#001c71>[SUPERDROP]</color> {0} <color=#ffff00>Supply Signals</color> = <color=#001c71>1 SuperDrop Signal</color>",
                ["SuperDropActivated"]  = "<color=#001c71>[SUPERDROP]</color> Launched! Crate arrives in <color=#00ffff>{0}s...</color>",
                ["SuperDropDelivered"]  = "<color=#001c71>[SUPERDROP]</color> <color=#00FF88>Delivered!</color> <color=#FFFFFF>Hack:</color> <color=#00ffff>{0}s...</color>",
                ["SuperDropGlobal"]     = "<color=#001c71>[SUPERDROP]</color> delivered at <color=#FFD54F>{0}</color>.",
                ["AutoConvertMega"]     = "<color=#ff00ff>[MEGADROP]</color> {0} <color=#001c71>SuperDrops</color> = <color=#ff00ff>1 MegaDrop Signal</color></b>!",
                ["MegaDropActivated"]   = "<color=#ff00ff>[MEGADROP]</color> Launched! Crate arrives in <color=#00ffff>{0}s...</color>",
                ["MegaDropDelivered"]   = "<color=#ff00ff>[MEGADROP]</color> <color=#00FF88>Delivered!</color> <color=#FFFFFF>Hack:</color> <color=#00ffff>{0}s...</color>",
                ["MegaDropGlobal"]      = "<color=#ff00ff>[MEGADROP]</color> delivered at <color=#FFD54F>{0}</color>.",
                ["AutoConvertUltra"]    = "<color=#cc0000>[ULTRADROP]</color> {0} <color=#ff00ff>MegaDrops</color> = <color=#cc0000>1 UltraDrop Signal</color></b>!",
                ["UltraDropActivated"]  = "<color=#cc0000>[ULTRADROP]</color> Launched! Crate arrives in <color=#00ffff>{0}s...</color>",
                ["UltraDropDelivered"]  = "<color=#cc0000>[ULTRADROP]</color> <color=#00FF88>Delivered!</color> <color=#FFFFFF>Hack:</color> <color=#00ffff>{0}s...</color>",
                ["UltraDropGlobal"]     = "<color=#cc0000>[ULTRADROP]</color> delivered at <color=#FFD54F>{0}</color>.",
                ["Cooldown"]            = "<color=#FF5252>Cooldown:</color> wait <b>{0}s</b> before throwing another drop.",
                ["AdminOnly"]           = "<color=#FF5555>Admins only can use this command!</color>",
                ["GaveSignals"]         = "<color=#00FF88>You received</color> <b>{0}</b> <color=#00FF88>supply signals!</color>",
                ["ErrorGiveSignals"]    = "<color=#FF5555>Error creating the supply signals!</color>",
                ["InfoHeader"]          = "<color=#00FFFF>═╣</color> <b>SuperDrop - Information</b> <color=#00FFFF>╠═</color>",
                ["InfoRequired"]        = "<color=#FFD54F>• Signals required:</color> <b>{0}</b>",
                ["InfoDeliveryTime"]    = "<color=#FFD54F>• Delivery time:</color> <b>{0}s</b>",
                ["InfoHackTime"]        = "<color=#FFD54F>• Hack time:</color> <b>{0}s</b>",
                ["InfoItemsCount"]      = "<color=#FFD54F>• Items in crate:</color> <b>{0}</b>",
                ["HelpHeader"]          = "<color=#00FFFF>═╣</color> <b>SuperDrop v1.10.4</b> <color=#00FFFF>╠═</color>",
                ["HelpCommandsTitle"]   = "<color=#FFFF00>Commands:</color>",
                ["HelpCommandInfo"]     = "<color=#FFFFFF>• /superdrop info</color> <color=#AAAAAA>– Plugin information</color>",
                ["HelpCommandGive"]     = "<color=#FFFFFF>• /superdrop give [qty]</color> <color=#AAAAAA>– Admin: give supply signals</color>",
                ["HelpUsageTitle"]      = "<color=#FFFF00>How to use:</color>",
                ["HelpUsageCollect"]    = "<color=#FFFFFF>• Collect <b>{0}</b> normal supply signals</color>",
                ["HelpUsageAutoConvert"]= "<color=#FFFFFF>• Auto-conversion <i>(doesn't mix unknown skins)</i></color>",
                ["HelpUsageThrow"]      = "<color=#FFFFFF>• Throw the <b>Super/Mega/Ultra Signal</b> where you want the crate</color>",
                ["HelpUsageWait"]       = "<color=#FFFFFF>• Wait for delivery/hack time according to tier</color>",
                ["InvalidCommand"]      = "<color=#FF5555>Invalid command!</color> <color=#AAAAAA>Use</color> <b>/superdrop</b> <color=#AAAAAA>for help.</color>",
                ["PermissionRequired"]  = "<color=#AAAAAA>Required permission:</color> <b>{0}</b>",
                ["ThrowBlockedBuilding"]= "<color=#FF5555>You can't throw here (building area)!</color>",
                ["NoTierPermission"]    = "<color=#FF5555>No permission</color> to use <b>{0}</b> <color=#AAAAAA>({1})</color>.",
                ["ErrorCreateCrate"]    = "<color=#FF5555>Failed to create the hackable crate.</color>",
                ["ErrorNoHackableComponent"] = "<color=#FF5555>Component <b>HackableLockedCrate</b> not found.</color>",
                ["WarningItemNotFound"] = "<color=#FFA000>Item not found:</color> <b>{0}</b>",
                ["WarningInsertItemFail"]= "<color=#FFA000>Failed to insert item:</color> <b>{0}</b>",
                ["ThrowCanceled"]       = "<color=#AAAAAA>Throw canceled.</color>",
                ["MixedSkinsAbort"]     = "Conversion skipped: unknown skinned signal present.",
                ["AutoStatusOn"]        = "Auto-convert is <color=#00ff88>ON</color> for you.",
                ["AutoStatusOff"]       = "Auto-convert is <color=#ff5555>OFF</color> for you.",
                ["AutoToggledOn"]       = "Auto-convert <color=#00ff88>enabled</color>.",
                ["AutoToggledOff"]      = "Auto-convert <color=#ff5555>disabled</color>.",
                ["ManualConvertDone"]   = "Manual conversion done (if possible).",
                ["NothingToConvert"]    = "Nothing to convert."
            };

            lang.RegisterMessages(pt, this, "pt-BR");
            lang.RegisterMessages(en, this, "en");
        }

        #endregion

        #region Hooks & Perm Infra

        private void Init()
        {
            LoadData();
            LoadDefaultMessages();

            bool changed = false;
            config.PermissionSuper = NormalizePerm(config.PermissionSuper, PERM_SUPER_CANON, ref changed);
            if (config.PermissionMega == PERM_MEGA_LEGACY)  { config.PermissionMega  = PERM_MEGA_CANON;  changed = true; }
            if (config.PermissionUltra == PERM_ULTRA_LEGACY){ config.PermissionUltra = PERM_ULTRA_CANON; changed = true; }
            config.PermissionMega  = NormalizePerm(config.PermissionMega,  PERM_MEGA_CANON,  ref changed);
            config.PermissionUltra = NormalizePerm(config.PermissionUltra, PERM_ULTRA_CANON, ref changed);
            if (changed) SaveConfig();

            EnsurePerm(PERM_SUPER_CANON);
            EnsurePerm(PERM_MEGA_CANON);
            EnsurePerm(PERM_ULTRA_CANON);

            EnsurePerm(config.PermissionSuper);
            EnsurePerm(config.PermissionMega);
            EnsurePerm(config.PermissionUltra);
        }

        private void OnServerInitialized()
        {
            if (!config.DefaultAutoEnabled) return;
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (!_data.AutoEnabled.Contains(p.userID))
                    _data.AutoEnabled.Add(p.userID);
            }
            SaveData();
        }

        private void OnPlayerConnected(BasePlayer bp)
        {
            if (bp == null) return;
            if (config.DefaultAutoEnabled && !_data.AutoEnabled.Contains(bp.userID))
            {
                _data.AutoEnabled.Add(bp.userID);
                SaveData();
            }
        }

        private void EnsurePerm(string perm)
        {
            if (string.IsNullOrWhiteSpace(perm)) return;
            perm = perm.Trim();
            if (!permission.PermissionExists(perm))
                permission.RegisterPermission(perm, this);
        }

        private bool HasAnyPerm(BasePlayer p, params string[] perms)
        {
            if (p == null) return false;
            foreach (var perm in perms)
            {
                if (string.IsNullOrWhiteSpace(perm)) continue;
                if (permission.UserHasPermission(p.UserIDString, perm)) return true;
            }
            return false;
        }

        private bool IsEligible(BasePlayer p)
        {
            if (p == null || !p.IsConnected) return false;
            if (!permission.UserHasPermission(p.UserIDString, config.PermissionSuper)) return false;
            return true;
        }

        #endregion

        #region Inventory Events & Conversion (pooled, event-driven)

        private BasePlayer OwnerOf(Item item)
        {
            if (item == null) return null;
            var p = item.parent?.playerOwner ?? (item.parent?.entityOwner as BasePlayer);
            if (p != null) return p;
            try { return item.GetOwnerPlayer(); } catch { return null; }
        }

        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (item?.info?.shortname != "supply.signal") return;
            var p = container?.playerOwner ?? (container?.entityOwner as BasePlayer);
            if (p == null || !p.IsConnected) return;
            TryAutoConvertFor(p);
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            if (item?.info?.shortname != "supply.signal") return;
            var p = container?.playerOwner ?? (container?.entityOwner as BasePlayer);
            if (p == null || !p.IsConnected) return;
            TryAutoConvertFor(p);
        }

        private void OnItemAmountChanged(Item item, int oldAmount)
        {
            if (item?.info?.shortname != "supply.signal") return;
            var p = OwnerOf(item);
            if (p == null || !p.IsConnected) return;
            TryAutoConvertFor(p);
        }

        private void OnItemStacked(Item item, Item target)
        {
            if (item?.info?.shortname != "supply.signal" && target?.info?.shortname != "supply.signal") return;
            var p = OwnerOf(target) ?? OwnerOf(item);
            if (p == null || !p.IsConnected) return;
            TryAutoConvertFor(p);
        }

        private void OnItemSplit(Item item, Item newItem)
        {
            if (item?.info?.shortname != "supply.signal" && newItem?.info?.shortname != "supply.signal") return;
            var p = OwnerOf(item) ?? OwnerOf(newItem);
            if (p == null || !p.IsConnected) return;
            TryAutoConvertFor(p);
        }

        private void OnGiveItem(BasePlayer player, Item item)
        {
            if (player == null || item?.info?.shortname != "supply.signal") return;
            if (!player.IsConnected) return;
            TryAutoConvertFor(player);
        }

        private void TryAutoConvertFor(BasePlayer player, bool manual = false)
        {
            if (player == null || !player.IsConnected) return;
            if (!manual && !_data.AutoEnabled.Contains(player.userID)) return;

            var supplyDef = ItemManager.FindItemDefinition("supply.signal");
            if (supplyDef == null) return;

            int normal = 0;
            WithPlayerItems(player, items =>
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it?.info?.shortname != "supply.signal") continue;
                    if (it.skin == 0) normal += it.amount;
                }
            });

            bool did = false;

            if (normal >= config.RequiredAmount && HasAnyPerm(player, config.PermissionSuper, PERM_SUPER_CANON))
            {
                int batches = normal / config.RequiredAmount;
                did |= ConvertNormalToSuperBatch_Pooled(player, supplyDef, batches);
            }

            int superCount, megaCount;
            RecountSuperMega(player, out superCount, out megaCount);

            if (superCount >= config.Mega.RequiredAmountFromSuper && HasAnyPerm(player, config.PermissionMega, PERM_MEGA_CANON, PERM_MEGA_LEGACY))
            {
                int batches = superCount / config.Mega.RequiredAmountFromSuper;
                did |= ConvertSuperToMegaBatch_Pooled(player, supplyDef, batches);
            }

            RecountSuperMega(player, out superCount, out megaCount);

            if (megaCount >= config.Ultra.RequiredAmountFromMega && HasAnyPerm(player, config.PermissionUltra, PERM_ULTRA_CANON, PERM_ULTRA_LEGACY))
            {
                int batches = megaCount / config.Ultra.RequiredAmountFromMega;
                did |= ConvertMegaToUltraBatch_Pooled(player, supplyDef, batches);
            }

            if (manual && !did)
                player.ChatMessage(Msg("NothingToConvert", player));
        }

        private void RecountSuperMega(BasePlayer player, out int super, out int mega)
        {
            int tmpSuper = 0;
            int tmpMega = 0;

            WithPlayerItems(player, items =>
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it?.info?.shortname != "supply.signal") continue;
                    ulong s = (ulong)it.skin;
                    if (s == config.RewardSkinID) tmpSuper += it.amount;
                    else if (s == config.Mega.SkinID) tmpMega += it.amount;
                }
            });

            super = tmpSuper;
            mega = tmpMega;
        }

        private void WithPlayerItems(BasePlayer player, System.Action<List<Item>> action)
        {
            var list = Pool.Get<List<Item>>();
            try
            {
                list.Clear();
                var inv = player?.inventory;
                if (inv?.containerMain != null) list.AddRange(inv.containerMain.itemList);
                if (inv?.containerBelt != null) list.AddRange(inv.containerBelt.itemList);
                action(list);
            }
            finally
            {
                Pool.Free(ref list);
            }
        }

        private bool ConvertNormalToSuperBatch_Pooled(BasePlayer player, ItemDefinition supplyDef, int batches)
        {
            if (batches <= 0) return false;
            int toTake = batches * config.RequiredAmount;

            WithPlayerItems(player, items =>
            {
                for (int i = 0; i < items.Count && toTake > 0; i++)
                {
                    var it = items[i];
                    if (it?.info?.shortname != "supply.signal" || it.skin != 0) continue;
                    int take = Mathf.Min(it.amount, toTake);
                    it.UseItem(take);
                    toTake -= take;
                }
            });
            if (toTake > 0) return false;

            for (int i = 0; i < batches; i++)
            {
                var sp = ItemManager.Create(supplyDef, 1, config.RewardSkinID);
                if (sp == null) continue;
                sp.name = "SuperDrop Signal";
                player.GiveItem(sp);
            }
            player.ChatMessage(string.Format(Msg("AutoConvert", player), config.RequiredAmount * batches));
            return true;
        }

        private bool ConvertSuperToMegaBatch_Pooled(BasePlayer player, ItemDefinition supplyDef, int batches)
        {
            if (batches <= 0) return false;
            int req = config.Mega.RequiredAmountFromSuper;
            int toTake = batches * req;

            WithPlayerItems(player, items =>
            {
                for (int i = 0; i < items.Count && toTake > 0; i++)
                {
                    var it = items[i];
                    if (it?.info?.shortname != "supply.signal") continue;
                    if ((ulong)it.skin != config.RewardSkinID) continue; // só Super
                    int take = Mathf.Min(it.amount, toTake);
                    it.UseItem(take);
                    toTake -= take;
                }
            });
            if (toTake > 0) return false;

            for (int i = 0; i < batches; i++)
            {
                var mega = ItemManager.Create(supplyDef, 1, config.Mega.SkinID);
                if (mega == null) continue;
                mega.name = "MegaDrop Signal";
                player.GiveItem(mega);
            }
            player.ChatMessage(string.Format(Msg("AutoConvertMega", player), req * batches));
            return true;
        }

        private bool ConvertMegaToUltraBatch_Pooled(BasePlayer player, ItemDefinition supplyDef, int batches)
        {
            if (batches <= 0) return false;
            int req = config.Ultra.RequiredAmountFromMega;
            int toTake = batches * req;

            WithPlayerItems(player, items =>
            {
                for (int i = 0; i < items.Count && toTake > 0; i++)
                {
                    var it = items[i];
                    if (it?.info?.shortname != "supply.signal") continue;
                    if ((ulong)it.skin != config.Mega.SkinID) continue;
                    int take = Mathf.Min(it.amount, toTake);
                    it.UseItem(take);
                    toTake -= take;
                }
            });
            if (toTake > 0) return false;

            for (int i = 0; i < batches; i++)
            {
                var ultra = ItemManager.Create(supplyDef, 1, config.Ultra.SkinID);
                if (ultra == null) continue;
                ultra.name = "UltraDrop Signal";
                player.GiveItem(ultra);
            }
            player.ChatMessage(string.Format(Msg("AutoConvertUltra", player), req * batches));
            return true;
        }

        #endregion

        #region Throw Hooks (lançamento especial, + comandos pós-entrega)

        private object CanThrowExplosive(BasePlayer player, Item item)
        {
            if (player == null || item?.info?.shortname != "supply.signal") return null;

            ulong skin = (ulong)item.skin;
            bool isSuper  = skin == config.RewardSkinID;
            bool isMega   = skin == config.Mega.SkinID;
            bool isUltra  = skin == config.Ultra.SkinID;
            bool isSpecial = isSuper || isMega || isUltra;

            if (!isSpecial) return null;

            if (_cancelHold.Contains(player.userID))
            {
                _cancelNextThrowUntil[player.userID] = Time.realtimeSinceStartup + 1.00f;
                return false;
            }

            if (_cancelNextThrowUntil.TryGetValue(player.userID, out var until))
            {
                if (Time.realtimeSinceStartup <= until)
                {
                    _cancelNextThrowUntil.Remove(player.userID);
                    return false;
                }
                _cancelNextThrowUntil.Remove(player.userID);
            }

            if (isSuper && !HasAnyPerm(player, config.PermissionSuper))
            { player.ChatMessage(string.Format(Msg("NoTierPermission", player), "SuperDrop", config.PermissionSuper)); return false; }
            if (isMega && !HasAnyPerm(player, config.PermissionMega))
            { player.ChatMessage(string.Format(Msg("NoTierPermission", player), "MegaDrop",  config.PermissionMega));  return false; }
            if (isUltra && !HasAnyPerm(player, config.PermissionUltra))
            { player.ChatMessage(string.Format(Msg("NoTierPermission", player), "UltraDrop", config.PermissionUltra)); return false; }

            if (IsInBuildingBlockArea(player.transform.position))
            { player.ChatMessage(Msg("ThrowBlockedBuilding", player)); return false; }

            double remaining = GetCooldownRemaining(player.userID);
            if (remaining > 0)
            { player.ChatMessage(string.Format(Msg("Cooldown", player), Mathf.CeilToInt((float)remaining))); return false; }

            return null;
        }

        private object OnExplosiveThrown(BasePlayer player, SupplySignal entity, ThrownWeapon thrown)
        {
            var signal = thrown?.GetItem();
            if (signal == null || signal.info.shortname != "supply.signal")
                return null;

            ulong skin = (ulong)signal.skin;

            if (_cancelNextThrowUntil.TryGetValue(player.userID, out var until) &&
                Time.realtimeSinceStartup <= until)
            {
                _cancelNextThrowUntil.Remove(player.userID);

                if (entity != null && !entity.IsDestroyed)
                {
                    entity.EntityToCreate = null;
                    SafeKill(entity);
                }
                player.ChatMessage(Msg("ThrowCanceled", player));
                RefundSignal(player, skin);
                return true;
            }

            bool isSuper = skin == config.RewardSkinID;
            bool isMega  = skin == config.Mega.SkinID;
            bool isUltra = skin == config.Ultra.SkinID;
            if (!isSuper && !isMega && !isUltra)
                return null;

            if (entity?.net?.ID.Value > 0u)
                _signalOwner[entity.net.ID.Value] = player.userID;

            double remaining = GetCooldownRemaining(player.userID);
            if (remaining > 0)
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.EntityToCreate = null;
                    SafeKill(entity);
                }
                player.ChatMessage(string.Format(Msg("Cooldown", player), Mathf.CeilToInt((float)remaining)));
                RefundSignal(player, skin);
                return true;
            }

            if (IsInBuildingBlockArea(entity.transform.position))
            {
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.EntityToCreate = null;
                    SafeKill(entity);
                }
                player.ChatMessage(Msg("ThrowBlockedBuilding", player));
                RefundSignal(player, skin);
                return true;
            }

            entity.EntityToCreate = null;
            specialSupplySignals.Add(entity);

            lastDropTime[player.userID] = Time.realtimeSinceStartup;

            float waitSeconds = isSuper ? config.SignalToCrateSeconds
                               : (isMega  ? config.Mega.SignalToCrateSeconds
                                          : config.Ultra.SignalToCrateSeconds);

            timer.Once(waitSeconds, () =>
            {
                if (entity == null || entity.IsDestroyed) return;

                var pos = entity.transform.position;

                ulong ownerId = entity.OwnerID != 0 ? entity.OwnerID : 0UL;
                if (ownerId == 0 && entity?.net?.ID.Value > 0u)
                    _signalOwner.TryGetValue(entity.net.ID.Value, out ownerId);

                string ownerName = ownerId != 0 ? (BasePlayer.FindByID(ownerId)?.displayName ?? ownerId.ToString()) : "UNKNOWN";

                if (isSuper) ExecuteConsoleCommands(config.ConsoleCommandsAfterDelay, ownerId, ownerName, pos, "super");
                else if (isMega) ExecuteConsoleCommands(config.Mega.ConsoleCommandsAfterDelay, ownerId, ownerName, pos, "mega");
                else ExecuteConsoleCommands(config.Ultra.ConsoleCommandsAfterDelay, ownerId, ownerName, pos, "ultra");

                if      (isSuper) SpawnHackableCrate_Super(pos, BasePlayer.FindByID(ownerId));
                else if (isMega)  SpawnHackableCrate_Mega(pos,  BasePlayer.FindByID(ownerId));
                else              SpawnHackableCrate_Ultra(pos, BasePlayer.FindByID(ownerId));

                timer.Once(0.25f, () =>
                {
                    if (entity != null && !entity.IsDestroyed)
                    {
                        SafeKill(entity);
                        specialSupplySignals.Remove(entity);
                    }
                });
            });

            if (player != null)
            {
                var key = isSuper ? "SuperDropActivated" : isMega ? "MegaDropActivated" : "UltraDropActivated";
                player.ChatMessage(string.Format(Msg(key, player), waitSeconds));
            }

            return null;
        }

        private double GetCooldownRemaining(ulong userId)
        {
            if (!lastDropTime.TryGetValue(userId, out var last)) return 0d;
            double elapsed   = Time.realtimeSinceStartup - last;
            double remaining = config.DropCooldownSeconds - elapsed;
            return remaining > 0d ? remaining : 0d;
        }

        private void OnCargoPlaneSignaled(CargoPlane cargoPlane, SupplySignal supplySignal)
        {
            if (supplySignal == null) return;

            ulong skin = supplySignal.skinID;
            if (skin == config.RewardSkinID || skin == config.Mega.SkinID || skin == config.Ultra.SkinID)
            {
                NextTick(() => SafeKill(cargoPlane as BaseEntity)); // sem avião para tiers especiais
            }
        }

        private void OnEntityKill(BaseNetworkable ent)
        {
            var sig = ent as SupplySignal;
            if (sig != null)
            {
                specialSupplySignals.Remove(sig);
                if (sig?.net?.ID.Value > 0u) _signalOwner.Remove(sig.net.ID.Value);
            }
        }

        private void OnEntityDestroyed(BaseNetworkable ent)
        {
            var sig = ent as SupplySignal;
            if (sig != null)
            {
                specialSupplySignals.Remove(sig);
                if (sig?.net?.ID.Value > 0u) _signalOwner.Remove(sig.net.ID.Value);
            }
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null || input == null) return;

            if (input.WasJustPressed(BUTTON.FIRE_SECONDARY) || input.IsDown(BUTTON.FIRE_SECONDARY))
            {
                var active = player.GetActiveItem();
                if (IsSpecialSignal(active))
                {
                    _cancelNextThrowUntil[player.userID] = Time.realtimeSinceStartup + 1.25f;
                    player.UpdateActiveItem(default(ItemId));
                    player.SendNetworkUpdateImmediate();

                    double now = Time.realtimeSinceStartup;
                    if (!_lastCancelMsg.TryGetValue(player.userID, out var last) || now - last > 0.75)
                    {
                        _lastCancelMsg[player.userID] = now;
                        player.ChatMessage(Msg("ThrowCanceled", player));
                    }

                    _cancelHold.Add(player.userID);

                    timer.Once(0.30f, () =>
                    {
                        if (player != null && player.IsConnected)
                            _cancelHold.Remove(player.userID);
                    });

                    return;
                }
            }

            if (input.WasJustReleased(BUTTON.FIRE_SECONDARY))
            {
                _cancelHold.Remove(player.userID);
            }
        }

        private void OnActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            if (player == null) return;
            _cancelHold.Remove(player.userID);
        }

        #endregion

        #region Spawns

        private void SpawnHackableCrate_Super(Vector3 position, BasePlayer player)
        {
            var prefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
            var entity = GameManager.server.CreateEntity(prefab, position);
            if (entity == null) { PrintError(Msg("ErrorCreateCrate")); return; }

            var hackable = entity.GetComponent<HackableLockedCrate>();
            if (hackable == null) { PrintError(Msg("ErrorNoHackableComponent")); SafeKill(entity); return; }

			hackable.hackSeconds = config.HackSeconds;

            if (hackable.inventory != null)
            {
                int minSlots = Mathf.Max(18, (config.Loot != null ? config.Loot.Count + 4 : 0));
                if (hackable.inventory.capacity < minSlots)
                    hackable.inventory.capacity = minSlots;
            }

            entity.Spawn();
            hackable.SetWasDropped();

            timer.Once(0.5f, () =>
            {
                if (hackable == null || hackable.IsDestroyed) return;
                hackable.inventory.Clear();

                foreach (var itemDef in config.Loot)
                {
                    if (UnityEngine.Random.value > itemDef.Chance) continue;

                    int amount = UnityEngine.Random.Range(itemDef.MinAmount, itemDef.MaxAmount + 1);
                    var def = ItemManager.FindItemDefinition(itemDef.ShortName);
                    if (def == null) { PrintWarning(string.Format(Msg("WarningItemNotFound"), itemDef.ShortName)); continue; }

                    var item = ItemManager.Create(def, amount, itemDef.Skin);
                    if (item == null) continue;

                    if (!string.IsNullOrEmpty(itemDef.DisplayName))
                    {
                        item.name = itemDef.DisplayName;
                        item.MarkDirty();
                    }

                    if (!hackable.inventory.Insert(item))
                    {
                        PrintWarning(string.Format(Msg("WarningInsertItemFail"), itemDef.DisplayName ?? itemDef.ShortName));
                        item.Remove();
                    }
                }

                hackable.SendNetworkUpdate();
            });

            Effect.server.Run("assets/bundled/prefabs/fx/explosions/explosion_01.prefab", position + Vector3.up * 2f);

            if (player != null && !player.IsDestroyed)
                player.ChatMessage(string.Format(Msg("SuperDropDelivered"), config.HackSeconds));

            PrintToChat(string.Format(Msg("SuperDropGlobal"), GetGridReference(position)));
        }

        private void SpawnHackableCrate_Mega(Vector3 position, BasePlayer player)
        {
            var prefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
            var entity = GameManager.server.CreateEntity(prefab, position);
            if (entity == null) { PrintError(Msg("ErrorCreateCrate")); return; }

            var hackable = entity.GetComponent<HackableLockedCrate>();
            if (hackable == null) { PrintError(Msg("ErrorNoHackableComponent")); SafeKill(entity); return; }

			hackable.hackSeconds = config.Mega.HackSeconds;

            if (hackable.inventory != null)
            {
                int minSlots = Mathf.Max(18, (config.Mega.Loot != null ? config.Mega.Loot.Count + 4 : 0));
                if (hackable.inventory.capacity < minSlots)
                    hackable.inventory.capacity = minSlots;
            }

            entity.Spawn();
            hackable.SetWasDropped();

            timer.Once(0.5f, () =>
            {
                if (hackable == null || hackable.IsDestroyed) return;
                hackable.inventory.Clear();

                foreach (var itemDef in config.Mega.Loot)
                {
                    if (UnityEngine.Random.value > itemDef.Chance) continue;

                    int amount = UnityEngine.Random.Range(itemDef.MinAmount, itemDef.MaxAmount + 1);
                    var def = ItemManager.FindItemDefinition(itemDef.ShortName);
                    if (def == null) { PrintWarning(string.Format(Msg("WarningItemNotFound"), itemDef.ShortName)); continue; }

                    var item = ItemManager.Create(def, amount, itemDef.Skin);
                    if (item == null) continue;

                    if (!string.IsNullOrEmpty(itemDef.DisplayName))
                    {
                        item.name = itemDef.DisplayName;
                        item.MarkDirty();
                    }

                    if (!hackable.inventory.Insert(item))
                    {
                        PrintWarning(string.Format(Msg("WarningInsertItemFail"), itemDef.DisplayName ?? itemDef.ShortName));
                        item.Remove();
                    }
                }

                hackable.SendNetworkUpdate();
            });

            Effect.server.Run("assets/bundled/prefabs/fx/explosions/explosion_01.prefab", position + Vector3.up * 2f);

            if (player != null && !player.IsDestroyed)
                player.ChatMessage(string.Format(Msg("MegaDropDelivered"), config.Mega.HackSeconds));

            PrintToChat(string.Format(Msg("MegaDropGlobal"), GetGridReference(position)));
        }

        private void SpawnHackableCrate_Ultra(Vector3 position, BasePlayer player)
        {
            var prefab = "assets/prefabs/deployable/chinooklockedcrate/codelockedhackablecrate.prefab";
            var entity = GameManager.server.CreateEntity(prefab, position);
            if (entity == null) { PrintError(Msg("ErrorCreateCrate")); return; }

            var hackable = entity.GetComponent<HackableLockedCrate>();
            if (hackable == null) { PrintError(Msg("ErrorNoHackableComponent")); SafeKill(entity); return; }

			hackable.hackSeconds = config.Ultra.HackSeconds;

            if (hackable.inventory != null)
            {
                int minSlots = Mathf.Max(18, (config.Ultra.Loot != null ? config.Ultra.Loot.Count + 4 : 0));
                if (hackable.inventory.capacity < minSlots)
                    hackable.inventory.capacity = minSlots;
            }

            entity.Spawn();
            hackable.SetWasDropped();

            timer.Once(0.5f, () =>
            {
                if (hackable == null || hackable.IsDestroyed) return;
                hackable.inventory.Clear();

                foreach (var itemDef in config.Ultra.Loot)
                {
                    if (UnityEngine.Random.value > itemDef.Chance) continue;

                    int amount = UnityEngine.Random.Range(itemDef.MinAmount, itemDef.MaxAmount + 1);
                    var def = ItemManager.FindItemDefinition(itemDef.ShortName);
                    if (def == null) { PrintWarning(string.Format(Msg("WarningItemNotFound"), itemDef.ShortName)); continue; }

                    var item = ItemManager.Create(def, amount, itemDef.Skin);
                    if (item == null) continue;

                    if (!string.IsNullOrEmpty(itemDef.DisplayName))
                    {
                        item.name = itemDef.DisplayName;
                        item.MarkDirty();
                    }

                    if (!hackable.inventory.Insert(item))
                    {
                        PrintWarning(string.Format(Msg("WarningInsertItemFail"), itemDef.DisplayName ?? itemDef.ShortName));
                        item.Remove();
                    }
                }

                hackable.SendNetworkUpdate();
            });

            Effect.server.Run("assets/bundled/prefabs/fx/explosions/explosion_01.prefab", position + Vector3.up * 2f);

            if (player != null && !player.IsDestroyed)
                player.ChatMessage(string.Format(Msg("UltraDropDelivered"), config.Ultra.HackSeconds));

            PrintToChat(string.Format(Msg("UltraDropGlobal"), GetGridReference(position)));
        }

        #endregion

        #region Utils & Commands

        private void SafeKill(BaseEntity ent)
        {
            if (ent == null) return;
            if (!ent.IsDestroyed)
            {
                try { ent.Kill(); } catch { /* noop */ }
            }
        }

        private bool IsSpecialSignal(Item item)
        {
            if (item == null || item.info?.shortname != "supply.signal") return false;
            ulong s = (ulong)item.skin;
            return s == config.RewardSkinID || s == config.Mega.SkinID || s == config.Ultra.SkinID;
        }

        private string GetGridReference(Vector3 position)
        {
            try { return MapHelper.PositionToString(position); }
            catch { return $"{Mathf.RoundToInt(position.x)},{Mathf.RoundToInt(position.z)}"; }
        }

        private bool IsInBuildingBlockArea(Vector3 position)
        {
            if (Physics.Raycast(position + Vector3.up * 1f, Vector3.down, out RaycastHit hit, 5f, LayerMask.GetMask("Construction")))
            {
                var block = hit.GetEntity() as BuildingBlock;
                if (block != null && !block.IsDestroyed) return true;
            }
            return false;
        }

        private void RefundSignal(BasePlayer player, ulong skin)
        {
            var def = ItemManager.FindItemDefinition("supply.signal");
            var refund = ItemManager.Create(def, 1, skin);
            if (refund != null)
            {
                if      (skin == config.Ultra.SkinID) refund.name = "UltraDrop Signal";
                else if (skin == config.Mega.SkinID)  refund.name = "MegaDrop Signal";
                else if (skin == config.RewardSkinID) refund.name = "SuperDrop Signal";
                player.GiveItem(refund);
            }
        }

        // Executa comandos com placeholders
        private void ExecuteConsoleCommands(List<string> cmds, ulong userId, string displayName, Vector3 pos, string tier)
        {
            if (cmds == null || cmds.Count == 0) return;

            string grid = GetGridReference(pos);
            string x = pos.x.ToString("0.##", CultureInfo.InvariantCulture);
            string y = pos.y.ToString("0.##", CultureInfo.InvariantCulture);
            string z = pos.z.ToString("0.##", CultureInfo.InvariantCulture);

            string steamid = userId.ToString();
            string name = !string.IsNullOrEmpty(displayName) ? displayName : steamid;

            foreach (var raw in cmds)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;

                string cmd = raw;
                var rx = RegexOptions.IgnoreCase;

                cmd = Regex.Replace(cmd, "\\{steamid\\}", steamid, rx);
                cmd = Regex.Replace(cmd, "\\{userid\\}",  steamid, rx);
                cmd = Regex.Replace(cmd, "\\{uuid\\}",    steamid, rx);
                cmd = Regex.Replace(cmd, "\\{uid\\}",     steamid, rx);
                cmd = Regex.Replace(cmd, "\\{playerid\\}",steamid, rx);
                cmd = Regex.Replace(cmd, "\\{id\\}",      steamid, rx);

                cmd = Regex.Replace(cmd, "\\{name\\}",    name,    rx);
                cmd = Regex.Replace(cmd, "\\{x\\}",       x,       rx);
                cmd = Regex.Replace(cmd, "\\{y\\}",       y,       rx);
                cmd = Regex.Replace(cmd, "\\{z\\}",       z,       rx);
                cmd = Regex.Replace(cmd, "\\{grid\\}",    grid,    rx);
                cmd = Regex.Replace(cmd, "\\{tier\\}",    tier,    rx);
                cmd = Regex.Replace(cmd, "\\{serverTime\\}", System.DateTime.Now.ToString("HH:mm:ss"), rx);

                try { ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), cmd); }
                catch (System.Exception ex) { PrintWarning($"Falha ao executar comando '{cmd}': {ex.Message}"); }
            }
        }

        [ChatCommand("superdrop")]
        private void CmdSuperDrop(BasePlayer player, string command, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                player.ChatMessage(Msg("HelpHeader", player));
                player.ChatMessage(Msg("HelpCommandsTitle", player));
                player.ChatMessage(Msg("HelpCommandInfo", player));
                player.ChatMessage(Msg("HelpCommandGive", player));
                player.ChatMessage("");
                player.ChatMessage(Msg("HelpUsageTitle", player));
                player.ChatMessage(string.Format(Msg("HelpUsageCollect", player), config.RequiredAmount));
                player.ChatMessage(Msg("HelpUsageAutoConvert", player));
                player.ChatMessage(Msg("HelpUsageThrow", player));
                player.ChatMessage(Msg("HelpUsageWait", player));
                player.ChatMessage("<color=#aaaaaa>Admin extra:</color> /superdrop setsuper <id>, /superdrop setmega <id>, /superdrop setultra <id>, /superdrop dump, /superdrop perms");
                player.ChatMessage(string.Format(Msg("PermissionRequired", player), config.PermissionSuper));
                player.ChatMessage(string.Format(Msg("PermissionRequired", player), config.PermissionMega));
                player.ChatMessage(string.Format(Msg("PermissionRequired", player), config.PermissionUltra));
                player.ChatMessage("<color=#aaaaaa>Auto:</color> /superdrop auto on|off|toggle|status  •  <color=#aaaaaa>Manual:</color> /superdrop convert");
                return;
            }

            switch (args[0].ToLower())
            {
                case "info":
                    ShowInfo(player);
                    break;

                case "give":
                    if (!player.IsAdmin) { player.ChatMessage(Msg("AdminOnly", player)); return; }
                    GiveSignals(player, args);
                    break;

                case "setsuper":
                    if (!player.IsAdmin) { player.ChatMessage(Msg("AdminOnly", player)); return; }
                    if (args.Length < 2 || !ulong.TryParse(args[1], out var superId))
                    { player.ChatMessage("<color=#ff5555>Uso:</color> /superdrop setsuper <skinId>"); return; }
                    config.RewardSkinID = superId; SaveConfig();
                    player.ChatMessage($"SuperDrop skinID atualizado para <color=#ffff00>{superId}</color> e salvo.");
                    break;

                case "setmega":
                    if (!player.IsAdmin) { player.ChatMessage(Msg("AdminOnly", player)); return; }
                    if (args.Length < 2 || !ulong.TryParse(args[1], out var megaId))
                    { player.ChatMessage("<color=#ff5555>Uso:</color> /superdrop setmega <skinId>"); return; }
                    config.Mega.SkinID = megaId; SaveConfig();
                    player.ChatMessage($"MegaDrop skinID atualizado para <color=#ffff00>{megaId}</color> e salvo.");
                    break;

                case "setultra":
                    if (!player.IsAdmin) { player.ChatMessage(Msg("AdminOnly", player)); return; }
                    if (args.Length < 2 || !ulong.TryParse(args[1], out var ultraId))
                    { player.ChatMessage("<color=#ff5555>Uso:</color> /superdrop setultra <skinId>"); return; }
                    config.Ultra.SkinID = ultraId; SaveConfig();
                    player.ChatMessage($"UltraDrop skinID atualizado para <color=#ffff00>{ultraId}</color> e salvo.");
                    break;

                case "dump":
                    if (!player.IsAdmin) { player.ChatMessage(Msg("AdminOnly", player)); return; }
                    DumpSupplySkins(player);
                    break;

                case "perms":
                    if (!player.IsAdmin) { player.ChatMessage(Msg("AdminOnly", player)); return; }

                    EnsurePerm(PERM_SUPER_CANON); EnsurePerm(config.PermissionSuper);
                    EnsurePerm(PERM_MEGA_CANON);  EnsurePerm(config.PermissionMega);  EnsurePerm(PERM_MEGA_LEGACY);
                    EnsurePerm(PERM_ULTRA_CANON); EnsurePerm(config.PermissionUltra); EnsurePerm(PERM_ULTRA_LEGACY);

                    player.ChatMessage("<color=#00ffff>=== Permissões registradas ===</color>");
                    player.ChatMessage($"Super: <color=#ffff00>{PERM_SUPER_CANON}</color>");
                    player.ChatMessage($"Mega : <color=#ffff00>{PERM_MEGA_CANON}</color> (legado: {PERM_MEGA_LEGACY})");
                    player.ChatMessage($"Ultra: <color=#ffff00>{PERM_ULTRA_CANON}</color> (legado: {PERM_ULTRA_LEGACY})");
                    player.ChatMessage("<color=#aaaaaa>Dica:</color> oxide.permission grant group default superdrop.use");
                    break;

                case "auto":
                {
                    if (_data.AutoEnabled.Contains(player.userID))
                    {
                        if (args.Length > 1 && (args[1].Equals("off", System.StringComparison.OrdinalIgnoreCase) || args[1] == "0" || args[1].Equals("false", System.StringComparison.OrdinalIgnoreCase)))
                        { _data.AutoEnabled.Remove(player.userID); SaveData(); player.ChatMessage(Msg("AutoToggledOff", player)); }
                        else if (args.Length > 1 && args[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
                        { player.ChatMessage(Msg("AutoStatusOn", player)); }
                        else if (args.Length > 1 && args[1].Equals("toggle", System.StringComparison.OrdinalIgnoreCase))
                        { _data.AutoEnabled.Remove(player.userID); SaveData(); player.ChatMessage(Msg("AutoToggledOff", player)); }
                        else { player.ChatMessage(Msg("AutoStatusOn", player)); }
                    }
                    else
                    {
                        if (args.Length > 1 && args[1].Equals("status", System.StringComparison.OrdinalIgnoreCase))
                        { player.ChatMessage(Msg("AutoStatusOff", player)); }
                        else
                        { _data.AutoEnabled.Add(player.userID); SaveData(); player.ChatMessage(Msg("AutoToggledOn", player)); }
                    }
                    break;
                }

                case "convert":
                {
                    TryAutoConvertFor(player, manual: true);
                    player.ChatMessage(Msg("ManualConvertDone", player));
                    break;
                }

                default:
                    player.ChatMessage(Msg("InvalidCommand", player));
                    break;
            }
        }

        private void ShowInfo(BasePlayer player)
        {
            player.ChatMessage(Msg("InfoHeader", player));

            player.ChatMessage("<color=#00ffff>=== SuperDrop ===</color>");
            player.ChatMessage(string.Format(Msg("InfoRequired", player), config.RequiredAmount));
            player.ChatMessage(string.Format(Msg("InfoDeliveryTime", player), config.SignalToCrateSeconds));
            player.ChatMessage(string.Format(Msg("InfoHackTime", player), config.HackSeconds));
            player.ChatMessage(string.Format(Msg("InfoItemsCount", player), config.Loot.Count));
            player.ChatMessage(string.Format(Msg("PermissionRequired", player), config.PermissionSuper));

            player.ChatMessage("<color=#00ffff>=== MegaDrop ===</color>");
            player.ChatMessage($"<color=#ffff00>SuperDrops necessários: {config.Mega.RequiredAmountFromSuper}</color>");
            player.ChatMessage($"<color=#ffff00>Tempo para entrega: {config.Mega.SignalToCrateSeconds}s</color>");
            player.ChatMessage($"<color=#ffff00>Tempo de hack: {config.Mega.HackSeconds}s</color>");
            player.ChatMessage($"<color=#ffff00>Itens na caixa: {config.Mega.Loot.Count}</color>");
            player.ChatMessage(string.Format(Msg("PermissionRequired", player), config.PermissionMega));

            player.ChatMessage("<color=#00ffff>=== UltraDrop ===</color>");
            player.ChatMessage($"<color=#ffff00>MegaDrops necessários: {config.Ultra.RequiredAmountFromMega}</color>");
            player.ChatMessage($"<color=#ffff00>Tempo para entrega: {config.Ultra.SignalToCrateSeconds}s</color>");
            player.ChatMessage($"<color=#ffff00>Tempo de hack: {config.Ultra.HackSeconds}s</color>");
            player.ChatMessage($"<color=#ffff00>Itens na caixa: {config.Ultra.Loot.Count}</color>");
            player.ChatMessage(string.Format(Msg("PermissionRequired", player), config.PermissionUltra));
        }

        private void GiveSignals(BasePlayer player, string[] args)
        {
            int amount = config.RequiredAmount;
            if (args.Length > 1 && int.TryParse(args[1], out int customAmount))
                amount = customAmount;

            var supplyDef = ItemManager.FindItemDefinition("supply.signal");
            var item = ItemManager.Create(supplyDef, amount);
            if (item != null)
            {
                player.GiveItem(item);
                player.ChatMessage(string.Format(Msg("GaveSignals", player), amount));
            }
            else player.ChatMessage(Msg("ErrorGiveSignals"));
        }

        private void DumpSupplySkins(BasePlayer player)
        {
            var map = new Dictionary<ulong, int>();
            WithPlayerItems(player, items =>
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (it.info.shortname != "supply.signal") continue;
                    ulong skin = (ulong)it.skin;
                    if (!map.ContainsKey(skin)) map[skin] = 0;
                    map[skin] += it.amount;
                }
            });

            player.ChatMessage("<color=#00ffff>=== Supply Signals por SkinID ===</color>");
            if (map.Count == 0)
            {
                player.ChatMessage("Nenhum supply.signal no inventário.");
                return;
            }

            foreach (var kv in map)
            {
                string label =
                    kv.Key == 0 ? "NORMAL" :
                    kv.Key == config.RewardSkinID ? "SUPERDROP (config)" :
                    kv.Key == config.Mega.SkinID ? "MEGADROP (config)" :
                    kv.Key == config.Ultra.SkinID ? "ULTRADROP (config)" :
                    "DESCONHECIDO";
                player.ChatMessage($"skin <color=#ffff00>{kv.Key}</color> → <color=#ffffff>{kv.Value}</color> ({label})");
            }
        }

        #endregion
    }
}
