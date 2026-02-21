using Oxide.Core.Plugins;
using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("CustomItens", "MestrePardal", "1.0.2")]
    [Description("Cria itens custom via CustomItemDefinitions (CID) com cadastro por config.")]
    public class CustomItens : RustPlugin
    {
        [PluginReference] private Plugin CustomItemDefinitions;

        private const string PERM_GIVE = "customitens.give";
        private bool _registered;

        #region Config

        private PluginConfig _config;

        private class PluginConfig
        {
            public bool Debug = false;
            public bool SkipIfShortnameExists = true;
            public string GiveCommand = "citem";
            public List<CustomItemEntry> Items = new List<CustomItemEntry>();
        }

        private class CustomItemEntry
        {
            public string Key;
            public string Shortname;
            public int ItemId;
            public string ParentShortname;
            public string Name;
            public string Description;
            public ulong SkinId;
            public int? MaxStackSize;
            public string Category;
            public bool AddServerOwnershipLine = false;
            public string ServerOwnershipText = "MASTERS";
            public List<TooltipLine> Tooltip = new List<TooltipLine>();
        }

        private class TooltipLine
        {
            public string Label;
            public string Text;
        }

		protected override void LoadDefaultConfig()
		{
			_config = new PluginConfig
			{
				Debug = false,
				SkipIfShortnameExists = true,
				GiveCommand = "citem",
				Items = new List<CustomItemEntry>
				{
					new CustomItemEntry
					{
						Key = "uranium",
						Shortname = "uranium",
						ItemId = 950000001,
						ParentShortname = "coal",
						Name = "Uranium",
						Description = "Uma pedra rara utilizada para fabricar itens específicos na mesa de mistura.\n\nRecurso exclusivo do servidor Masters.",
						SkinId = 3608420824UL,
						MaxStackSize = 1,
						Category = "Resources",
						Tooltip = new List<TooltipLine>
						{
							new TooltipLine { Label = "RARIDADE", Text = "Épico" },
							new TooltipLine { Label = "CLASSE", Text = "Mineral" },
							new TooltipLine { Label = "SERVER", Text = "MASTERS" }
						}
					},
					new CustomItemEntry
					{
						Key = "plutonium",
						Shortname = "plutonium",
						ItemId = 950000002,
						ParentShortname = "coal",
						Name = "Plutonium",
						Description = "Uma pedra extremamente poderosa usada na fabricação de itens avançados.\n\nRecurso exclusivo do servidor Masters.",
						SkinId = 3608420103UL,
						MaxStackSize = 1,
						Category = "Resources",
						Tooltip = new List<TooltipLine>
						{
							new TooltipLine { Label = "RARIDADE", Text = "Épico" },
							new TooltipLine { Label = "CLASSE", Text = "Mineral" },
							new TooltipLine { Label = "SERVER", Text = "MASTERS" }
						}
					}
				}
			};

			SaveConfig();
		}

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<PluginConfig>();
                if (_config == null) throw new Exception("Config is null");
            }
            catch
            {
                PrintError("Config inválida! Gerando uma nova.");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Oxide Hooks

        private void Init()
        {
            permission.RegisterPermission(PERM_GIVE, this);

            if (!string.IsNullOrEmpty(_config?.GiveCommand))
                cmd.AddChatCommand(_config.GiveCommand, this, nameof(CmdGive));
        }

        private void OnServerInitialized(bool initial)
        {
            CheckCID();
            TryRegisterAll();
        }

        private void OnCIDLoaded(Plugin library = null)
        {
            CustomItemDefinitions ??= library;
            TryRegisterAll();
        }

        #endregion

        #region CID

        private void CheckCID()
        {
            if (CustomItemDefinitions == null)
                return;

            if (CustomItemDefinitions.Version.Major < 2)
                throw new Exception("CustomItemDefinitions está desatualizado. Use versão 2.* ou superior.");
        }

        private void TryRegisterAll()
        {
            if (_registered) return;

            if (CustomItemDefinitions == null)
            {
                if (_config != null && _config.Debug)
                    Puts("CID ainda não carregou. Aguardando OnCIDLoaded...");
                return;
            }

            CheckCID();

            if (_config?.Items == null || _config.Items.Count == 0)
            {
                PrintWarning("Nenhum item cadastrado na config.");
                _registered = true;
                return;
            }

            foreach (var entry in _config.Items)
            {
                try
                {
                    RegisterOne(entry);
                }
                catch (Exception ex)
                {
                    PrintError($"Falha ao registrar item Key='{entry?.Key}': {ex.Message}");
                }
            }

            _registered = true;
            Puts($"CustomItens: registro concluído. Itens na config: {_config.Items.Count}");
        }

        private void RegisterOne(CustomItemEntry e)
        {
            if (e == null) throw new Exception("Entry null");

            if (string.IsNullOrWhiteSpace(e.Key))
                throw new Exception("Key é obrigatória (ex: 'hammer_masters')");

            if (string.IsNullOrWhiteSpace(e.Shortname))
                throw new Exception($"Key '{e.Key}': Shortname é obrigatório");

            if (e.ItemId == 0)
                throw new Exception($"Key '{e.Key}': ItemId é obrigatório e deve ser fixo (não use 0)");

            if (string.IsNullOrWhiteSpace(e.ParentShortname))
                throw new Exception($"Key '{e.Key}': ParentShortname é obrigatório (item vanilla base)");

            var existing = ItemManager.FindItemDefinition(e.Shortname);
            if (existing != null)
            {
                var msg = $"Shortname '{e.Shortname}' já existe no servidor (itemid={existing.itemid}).";
                if (_config.SkipIfShortnameExists)
                {
                    PrintWarning(msg + " Pulando registro.");
                    return;
                }
                throw new Exception(msg);
            }

            var parent = ItemManager.FindItemDefinition(e.ParentShortname);
            if (parent == null)
                throw new Exception($"Key '{e.Key}': ParentShortname '{e.ParentShortname}' não encontrado (item vanilla?)");

            var parentMods = parent.itemMods != null ? (ItemMod[])parent.itemMods.Clone() : Array.Empty<ItemMod>();

            var ownerships = new List<(Translate.Phrase label, Translate.Phrase text)>();

            if (e.Tooltip != null)
            {
                foreach (var t in e.Tooltip)
                {
                    if (t == null) continue;
                    if (string.IsNullOrWhiteSpace(t.Label) || string.IsNullOrWhiteSpace(t.Text)) continue;
                    ownerships.Add((new Translate.Phrase(null, t.Label), new Translate.Phrase(null, t.Text)));
                }
            }

            if (e.AddServerOwnershipLine && !string.IsNullOrWhiteSpace(e.ServerOwnershipText))
            {
                ownerships.Add((new Translate.Phrase(null, "SERVER"), new Translate.Phrase(null, e.ServerOwnershipText)));
            }

            var cat = ParseCategory(e.Category);

            EnsureLangForItem(e.Shortname, e.Name, e.Description);

            CustomItemDefinitions.Call<ItemDefinition>("Register", new
            {
                shortname = e.Shortname,
                itemId = e.ItemId,
                parentItemId = parent.itemid,

                maxStackSize = e.MaxStackSize,
                category = cat,

                defaultName = string.IsNullOrWhiteSpace(e.Name) ? null : e.Name,
                defaultDescription = string.IsNullOrWhiteSpace(e.Description) ? null : e.Description,

                defaultSkinId = e.SkinId,
                staticOwnerships = ownerships,
                itemMods = parentMods
            }, this);

            if (_config.Debug)
                Puts($"Registrado: key={e.Key} shortname={e.Shortname} itemId={e.ItemId} parent={e.ParentShortname} skin={e.SkinId}");
        }

        private ItemCategory? ParseCategory(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            if (Enum.TryParse(raw, true, out ItemCategory cat))
                return cat;

            PrintWarning($"Category inválida na config: '{raw}'. Usando categoria do parent (null).");
            return null;
        }

        #endregion

        #region Lang

        private void EnsureLangForItem(string shortname, string name, string desc)
        {
            var dict = new Dictionary<string, string>();

            if (!string.IsNullOrWhiteSpace(name))
                dict[shortname] = name;

            if (!string.IsNullOrWhiteSpace(desc))
                dict[$"{shortname}.desc"] = desc;

            if (dict.Count == 0) return;

            // ✅ registra em variações que o Rust pode pedir (pt-BR vs pt-br)
            lang.RegisterMessages(dict, this, "en");
            lang.RegisterMessages(dict, this, "pt-BR");
            lang.RegisterMessages(dict, this, "pt");
            lang.RegisterMessages(dict, this, "pt-br");
        }

        #endregion

        #region Command

        private void CmdGive(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;

            if (!permission.UserHasPermission(player.UserIDString, PERM_GIVE))
            {
                player.ChatMessage("Sem permissão.");
                return;
            }

            if (args == null || args.Length < 2 || !args[0].Equals("give", StringComparison.OrdinalIgnoreCase))
            {
                player.ChatMessage($"Use: /{_config.GiveCommand} give <key> [quantidade]");
                return;
            }

            var key = args[1];
            var amount = 1;

            if (args.Length >= 3 && !int.TryParse(args[2], out amount))
                amount = 1;

            if (amount < 1) amount = 1;

            var entry = FindByKey(key);
            if (entry == null)
            {
                player.ChatMessage($"Key '{key}' não existe na config.");
                return;
            }

            var def = ItemManager.FindItemDefinition(entry.Shortname);
            if (def == null)
            {
                player.ChatMessage("Item não registrado. Verifique se o CID carregou e se não houve conflito de shortname.");
                return;
            }

            var item = ItemManager.Create(def, amount, entry.SkinId);
            if (item == null)
            {
                player.ChatMessage("Falha ao criar item.");
                return;
            }

            if (!player.inventory.GiveItem(item))
                item.Drop(player.transform.position + (player.eyes.BodyForward() * 0.5f), player.eyes.BodyForward());

            player.ChatMessage($"Recebeu: {entry.Name ?? def.displayName.english} x{amount}");
        }

        private CustomItemEntry FindByKey(string key)
        {
            if (_config?.Items == null) return null;
            foreach (var e in _config.Items)
            {
                if (e == null) continue;
                if (string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            return null;
        }

        #endregion
    }
}
