using Oxide.Core;
using Oxide.Core.Plugins;
using UnityEngine;
using UnityEngine.Networking;
using Rust;
using System.Collections;
using UnityRandom = UnityEngine.Random;
using Facepunch;
using System.Collections.Generic;
using Oxide.Core.Libraries.Covalence;
using Newtonsoft.Json;
using System.Linq;
using System.IO;

namespace Oxide.Plugins
{
    [Info("SmartSignURL", "Mestre Pardal", "2.3.5")]
    [Description("Aplica imagens PNG/JPG em placas no Rust via URL, com sistema de limites, persistência e sincronização completa.")]
    public class SmartSignURL : RustPlugin
    {
        private const string PermissionUse = "smartsignurl.use";
        private const string PermissionAllSigns = "smartsignurl.all";
        private readonly string[] LimitPermissions = { "smartsignurl.limit.5", "smartsignurl.limit.10", "smartsignurl.limit.20", "smartsignurl.limit.50", "smartsignurl.limit.unlimited" };

        private const string SaveFile = "SmartSignURL_Data";
        private GameObject loaderObject;
        private ImageLoader loader;

        private List<SavedSign> savedSigns = new List<SavedSign>();
        private Dictionary<string, MonumentSignInfo> monumentSigns = new Dictionary<string, MonumentSignInfo>();

        // ✅ LISTA DE PREFABS DE PLACAS SUPORTADAS
        private readonly HashSet<string> ValidSignPrefabs = new HashSet<string>
        {
            "assets/prefabs/deployable/signs/sign.huge.wood.prefab",
            "assets/prefabs/deployable/signs/sign.large.wood.prefab", 
            "assets/prefabs/deployable/signs/sign.medium.wood.prefab",
            "assets/prefabs/deployable/signs/sign.small.wood.prefab",
            "assets/prefabs/deployable/signs/sign.pictureframe.landscape.prefab",
            "assets/prefabs/deployable/signs/sign.pictureframe.portrait.prefab",
            "assets/prefabs/deployable/signs/sign.pictureframe.tall.prefab",
            "assets/prefabs/deployable/signs/sign.pictureframe.xl.prefab",
            "assets/prefabs/deployable/signs/sign.pictureframe.xxl.prefab",
            "assets/prefabs/deployable/signs/sign.post.single.prefab",
            "assets/prefabs/deployable/signs/sign.post.double.prefab",
            "assets/prefabs/deployable/signs/sign.post.town.prefab",
            "assets/prefabs/deployable/signs/sign.post.town.roof.prefab",
            "assets/prefabs/deployable/signs/sign.hanging.banner.large.prefab",
            "assets/prefabs/deployable/signs/sign.hanging.ornate.prefab",
            "assets/prefabs/deployable/signs/sign.pole.banner.large.prefab",
			"assets/prefabs/misc/summer_dlc/photoframe/photoframe.large.prefab",
			"assets/prefabs/misc/summer_dlc/photoframe/photoframe.landscape.prefab",
			"assets/prefabs/misc/summer_dlc/photoframe/photoframe.portrait.prefab"
        };

        private class SavedSign
        {
            public Vector3 Position;
            public string ImageURL;
            public ulong OwnerId;
            public string PrefabName;
            public bool IsAdminSign;
            public string MonumentSignId;
            public ulong NetworkId; // ✅ MUDADO PARA ULONG PARA COMPATIBILIDADE
        }

        private class MonumentSignInfo
        {
            public string Id;
            public Vector3 Position;
            public string PrefabName;
        }

        private ConfigData config;
        private class ConfigData 
        {
            public bool RequireTCAuth = true;
            public string CommandApply = "sil";
            public string CommandClear = "imgclear";
            public string CommandClearAll = "imgclearall";
            public int MaxImageWidth = 4096;
            public int MaxImageHeight = 4096;
            public int MaxImageSizeMB = 2;
            public float SignDetectionRadius = 3f;
            public bool AllowJPG = true;
            public bool AllowPNG = true;
        }

        protected override void LoadDefaultConfig() { config = new ConfigData(); SaveConfig(); }
        protected override void LoadConfig() { base.LoadConfig(); config = Config.ReadObject<ConfigData>(); }
        protected override void SaveConfig() { Config.WriteObject(config); }

        void Init()
        {
            LoadConfig();
            permission.RegisterPermission("smartsignurl.ignoretc", this);
            permission.RegisterPermission("smartsignurl.ignorelimits", this);
            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionAllSigns, this);
            foreach (var perm in LimitPermissions)
                permission.RegisterPermission(perm, this);
        }

        void OnServerInitialized()
        {
            // ✅ LOADER MELHORADO
            loaderObject = new GameObject("SmartSignURL_ImageLoader");
            UnityEngine.Object.DontDestroyOnLoad(loaderObject);
            loader = loaderObject.AddComponent<ImageLoader>();
            loader.plugin = this;

            // ✅ CARREGA DADOS SALVOS
            LoadSavedData();

            Puts("[SmartSignURL] Aguardando 30 segundos para inicialização completa...");

            // ✅ TEMPO REDUZIDO E PROCESSO OTIMIZADO
            timer.Once(30f, () =>
            {
                CarregarPlacasMonumento();
                RestoreSavedImages();
                Puts($"[SmartSignURL] Inicialização completa! {savedSigns.Count} placas carregadas.");
            });
        }

        void Unload()
        {
            if (loaderObject != null)
            {
                UnityEngine.Object.Destroy(loaderObject);
            }
        }

        // ✅ FUNÇÃO MELHORADA PARA CARREGAR DADOS
        private void LoadSavedData()
        {
            try
            {
                savedSigns = Interface.Oxide.DataFileSystem.ReadObject<List<SavedSign>>(SaveFile) ?? new List<SavedSign>();
                Puts($"[SmartSignURL] Carregados {savedSigns.Count} registros de placas.");
            }
            catch (System.Exception ex)
            {
                PrintError($"Erro ao carregar dados salvos: {ex.Message}");
                savedSigns = new List<SavedSign>();
            }
        }

        private class MonumentAddonsData { public Dictionary<string, MonumentEntities> MonumentData { get; set; } }
        private class MonumentWrapper { public MonumentEntities First { get; set; } }
        private class MonumentEntities { public List<MonumentEntity> Entities { get; set; } }
        private class MonumentEntity { public string Id; public Position Position; public string PrefabName; }
        private class Position { public float x, y, z; }

        private void SaveData() 
        {
            try
            {
                Interface.Oxide.DataFileSystem.WriteObject(SaveFile, savedSigns);
            }
            catch (System.Exception ex)
            {
                PrintError($"Erro ao salvar dados: {ex.Message}");
            }
        }

        private void CarregarPlacasMonumento()
        {
            string path = Path.Combine(Interface.Oxide.DataDirectory, "monumentaddons/Default.json");
            if (!File.Exists(path)) 
            {
                Puts("[SmartSignURL] Arquivo MonumentAddons não encontrado, pulando placas de monumento.");
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                MonumentAddonsData data = JsonConvert.DeserializeObject<MonumentAddonsData>(json);
                int count = 0;

                foreach (var monument in data.MonumentData.Values)
                {
                    foreach (var entity in monument.Entities)
                    {
                        if (string.IsNullOrEmpty(entity.PrefabName) || !IsValidSignPrefab(entity.PrefabName)) continue;
                        
                        var id = entity.Id;
                        var pos = new Vector3(entity.Position.x, entity.Position.y, entity.Position.z);
                        var prefab = entity.PrefabName;
                        monumentSigns[id] = new MonumentSignInfo { Id = id, Position = pos, PrefabName = prefab };
                        count++;
                    }
                }

                Puts($"[SmartSignURL] Carregadas {count} placas de monumento.");
            }
            catch (System.Exception ex)
            {
                PrintError($"Erro ao carregar placas de monumento: {ex.Message}");
            }
        }

		private bool IsValidSignPrefab(string prefabName)
		{
			if (string.IsNullOrEmpty(prefabName)) return false;

			prefabName = prefabName.ToLower();

			return ValidSignPrefabs.Contains(prefabName)
				|| prefabName.Contains("sign")
				|| prefabName.Contains("photoframe");
		}

        private void RestoreSavedImages()
        {
            int restored = 0;
            var allSigns = UnityEngine.Object.FindObjectsOfType<Signage>();
            
            Puts($"[SmartSignURL] Encontradas {allSigns.Length} placas no servidor, restaurando imagens...");

            foreach (var signEntity in allSigns)
            {
                if (signEntity == null || signEntity.IsDestroyed) continue;

                SavedSign match = null;

                // ✅ PRIMEIRO: TENTAR MATCH POR NETWORK ID
                match = savedSigns.FirstOrDefault(s => s.NetworkId == (ulong)signEntity.net.ID.Value);

                // ✅ SEGUNDO: TENTAR MATCH POR MONUMENTO
                if (match == null)
                {
                    foreach (var mSign in monumentSigns.Values)
                    {
                        if (Vector3.Distance(signEntity.transform.position, mSign.Position) < 1f && 
                            signEntity.PrefabName == mSign.PrefabName)
                        {
                            match = savedSigns.FirstOrDefault(s => s.MonumentSignId == mSign.Id);
                            if (match != null) break;
                        }
                    }
                }

                // ✅ TERCEIRO: MATCH POR POSIÇÃO
                if (match == null)
                {
                    match = savedSigns.FirstOrDefault(s => Vector3.Distance(s.Position, signEntity.transform.position) < 1f);
                }

                if (match != null)
                {
                    // ✅ ATUALIZAR NETWORK ID PARA FUTURAS REFERÊNCIAS
                    match.NetworkId = (ulong)signEntity.net.ID.Value;
                    match.Position = signEntity.transform.position; // ✅ ATUALIZAR POSIÇÃO
                    
                    loader.StartCoroutine(loader.DownloadImage(null, signEntity, match.ImageURL, false));
                    restored++;
                }
            }

            SaveData(); // ✅ SALVAR NETWORK IDS ATUALIZADOS
            Puts($"[SmartSignURL] Restauradas {restored} imagens em placas.");
        }

        private int GetPlayerImageLimit(BasePlayer player)
        {
            if (permission.UserHasPermission(player.UserIDString, "smartsignurl.limit.unlimited") || 
                permission.UserHasPermission(player.UserIDString, "smartsignurl.ignorelimits") || 
                player.IsAdmin)
                return int.MaxValue;
            if (permission.UserHasPermission(player.UserIDString, "smartsignurl.limit.50")) return 50;
            if (permission.UserHasPermission(player.UserIDString, "smartsignurl.limit.20")) return 20;
            if (permission.UserHasPermission(player.UserIDString, "smartsignurl.limit.10")) return 10;
            if (permission.UserHasPermission(player.UserIDString, "smartsignurl.limit.5")) return 5;
            return 0;
        }

		object OnHammerHit(BasePlayer player, HitInfo info)
		{
			if (player == null || info?.HitEntity == null || !(info.HitEntity is Signage sign))
			{
				return null;
			}

			var match = savedSigns.FirstOrDefault(s => (s.NetworkId != 0 && s.NetworkId == sign.net.ID.Value) || (Vector3.Distance(s.Position, sign.transform.position) < 1f));

			if (match != null)
			{
				Puts("==================== [SmartSignURL DEBUG - INÍCIO] ====================");
				Puts($"Jogador '{player.displayName}' atingiu placa protegida. Iniciando investigação...");
				player.ChatMessage("<color=#ffaa00>Use <color=#00ffff>/imgclear</color> na placa para poder removê-la.</color>");

				timer.Once(0.2f, () =>
				{
					if (player == null || player.IsDestroyed)
					{
						Puts("[SmartSignURL DEBUG] Jogador desconectou antes da verificação. Abortando.");
						Puts("==================== [SmartSignURL DEBUG - FIM] ======================");
						return;
					}

					string signItemShortname = sign.ShortPrefabName;
					Puts($"[SmartSignURL DEBUG] Alvo da busca: Item com shortname = '{signItemShortname}'");
					Puts("[SmartSignURL DEBUG] --- Printando inventário do jogador AGORA ---");

					bool foundADuplicate = false;
					
					// --- INÍCIO DA CORREÇÃO ---
					ItemId duplicateItemId = default(ItemId); // AQUI ESTÁ A CORREÇÃO! Trocamos 'uint' por 'ItemId'.
					// --- FIM DA CORREÇÃO ---

					List<Item> allPlayerItems = new List<Item>();
					allPlayerItems.AddRange(player.inventory.containerMain.itemList);
					allPlayerItems.AddRange(player.inventory.containerBelt.itemList);

					foreach (Item item in allPlayerItems)
					{
						Puts($"[SmartSignURL DEBUG] > Encontrado no inventário: '{item.info.shortname}', Quantidade: {item.amount}, ID: {item.uid}");
						
						if (item.info.shortname == signItemShortname)
						{
							Puts($"[SmartSignURL DEBUG]     L-> ESTE É O ITEM DUPLICADO! MARCANDO PARA REMOÇÃO.");
							foundADuplicate = true;
							duplicateItemId = item.uid;
							break; 
						}
					}
					Puts("[SmartSignURL DEBUG] --- Fim do inventário ---");

					if (foundADuplicate)
					{
						Item itemToRemove = player.inventory.FindItemByUID(duplicateItemId);
						if (itemToRemove != null)
						{
							itemToRemove.Remove();
							Puts($"[SmartSignURL DEBUG] SUCESSO: Comando de remoção enviado para o item com ID {duplicateItemId}.");
							player.ChatMessage("<color=#00ff00>A placa com imagem está protegida.</color>");
						} else {
							 Puts($"[SmartSignURL DEBUG] FALHA CRÍTICA: Marcamos o item, mas não conseguimos achá-lo de novo para remover!");
						}
					}
					else
					{
						Puts($"[SmartSignURL DEBUG] FALHA: A busca terminou e o item alvo ('{signItemShortname}') não foi encontrado no inventário.");
					}
					Puts("==================== [SmartSignURL DEBUG - FIM] ======================");
				});
				
				return false;
			}

			return null;
		}
		
		// 2ª CAMADA: O GUARDIÃO FINAL CONTRA DESTRUIÇÃO
		object OnEntityKill(BaseNetworkable entity)
		{
			if (entity == null || !(entity is Signage sign))
				return null;

			var match = savedSigns.FirstOrDefault(s =>
				s.NetworkId != 0 && s.NetworkId == sign.net.ID.Value || 
				Vector3.Distance(s.Position, sign.transform.position) < 1f);

			if (match != null)
			{
				// Remove a imagem do FileStorage
				FileStorage.server.RemoveAllByEntity(sign.net.ID);

				// Remove dos dados salvos
				savedSigns.Remove(match);
				SaveData();

				Puts($"[SmartSignURL] Placa destruída naturalmente. Imagem removida.");
			}

			return null; // ✅ Deixa a destruição acontecer normalmente agora
		}


		object CanPickupEntity(BasePlayer player, BaseEntity entity)
		{
			if (player == null || !(entity is Signage sign))
				return null;

			var match = savedSigns.FirstOrDefault(s =>
				(s.NetworkId != 0 && s.NetworkId == sign.net.ID.Value) ||
				(Vector3.Distance(s.Position, sign.transform.position) < 1f));

			if (match != null)
			{
				player.ChatMessage("<color=#ffaa00>Use <color=#00ffff>/imgclear</color> na placa para poder removê-la.</color>");
				return false; // ✅ BLOQUEIA TOTALMENTE o pickup antes que qualquer item seja criado!
			}

			return null;
		}
		
        void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity is Signage sign)
            {
                // ✅ VERIFICAR SE EXISTE IMAGEM SALVA PARA ESTA POSIÇÃO
                timer.Once(2f, () =>
                {
                    if (sign == null || sign.IsDestroyed) return;

                    var match = savedSigns.FirstOrDefault(s => Vector3.Distance(s.Position, sign.transform.position) < 1f);
                    if (match != null)
                    {
                        match.NetworkId = sign.net.ID.Value;
                        loader.StartCoroutine(loader.DownloadImage(null, sign, match.ImageURL, false));
                        SaveData();
                    }
                });
            }
        }

		public class ImageLoader : MonoBehaviour
		{
			public SmartSignURL plugin;

			public IEnumerator DownloadImage(BasePlayer player, BaseEntity entity, string url, bool save)
			{
				if (entity == null || entity.IsDestroyed)
				{
					player?.ChatMessage("<color=#ff0000>Placa não encontrada ou foi destruída.</color>");
					yield break;
				}

				// ✅ VALIDAÇÃO DE URL
				if (string.IsNullOrEmpty(url) || (!url.StartsWith("http://") && !url.StartsWith("https://")))
				{
					player?.ChatMessage("<color=#ff0000>URL inválida. Use http:// ou https://</color>");
					yield break;
				}

				// ✅ VERIFICAR EXTENSÃO
				string urlLower = url.ToLower();
				bool isValidFormat = urlLower.Contains(".png") || urlLower.Contains(".jpg") || urlLower.Contains(".jpeg");

				if (!isValidFormat)
				{
					player?.ChatMessage("<color=#ffaa00>URL não parece conter PNG/JPG, tentando mesmo assim...</color>");
				}

				UnityWebRequest www = UnityWebRequest.Get(url);
				www.timeout = 30;
				www.downloadHandler = new DownloadHandlerBuffer();

				yield return www.SendWebRequest();

				if (www.result != UnityWebRequest.Result.Success)
				{
					player?.ChatMessage($"<color=#ff0000>Erro ao baixar imagem: {www.error}</color>");
					yield break;
				}

				byte[] data = www.downloadHandler.data;
				if (data == null || data.Length == 0)
				{
					player?.ChatMessage("<color=#ff0000>Imagem vazia ou inválida.</color>");
					yield break;
				}

				if (data.Length > plugin.config.MaxImageSizeMB * 1024 * 1024)
				{
					player?.ChatMessage($"<color=#ff0000>Imagem muito grande! Máximo: {plugin.config.MaxImageSizeMB}MB</color>");
					yield break;
				}

				// ✅ VERIFICAÇÃO DE FORMATO
				bool isPNG = data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
				bool isJPG = data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

				if (!isPNG && !isJPG)
				{
					player?.ChatMessage("<color=#ff0000>Formato de imagem não suportado. Use PNG ou JPG.</color>");
					yield break;
				}

				Texture2D texture = new Texture2D(2, 2);
				if (!texture.LoadImage(data))
				{
					player?.ChatMessage("<color=#ff0000>Falha ao carregar imagem. Arquivo corrompido?</color>");
					UnityEngine.Object.Destroy(texture);
					yield break;
				}

				// ✅ VERIFICAÇÃO DE DIMENSÕES
				if (texture.width > plugin.config.MaxImageWidth || texture.height > plugin.config.MaxImageHeight)
				{
					if (player != null && !player.IsAdmin && !plugin.permission.UserHasPermission(player.UserIDString, "smartsignurl.ignorelimits"))
					{
						player.ChatMessage($"<color=#ff0000>Dimensões muito grandes! Máximo: {plugin.config.MaxImageWidth}x{plugin.config.MaxImageHeight}</color>");
						UnityEngine.Object.Destroy(texture);
						yield break;
					}
				}

				// ✅ PIXEL ALEATÓRIO PARA EVITAR CACHE
				texture.SetPixel(texture.width - 1, texture.height - 1, new Color(UnityRandom.value, UnityRandom.value, UnityRandom.value));
				texture.Apply();

				byte[] pngData = texture.EncodeToPNG();
				UnityEngine.Object.Destroy(texture);

				if (pngData == null)
				{
					player?.ChatMessage("<color=#ff0000>Erro ao converter imagem para PNG.</color>");
					yield break;
				}

				// ✅ APLICAR IMAGEM
				bool applied = plugin.ApplyImageToEntity(entity, pngData);
				if (!applied)
				{
					player?.ChatMessage("<color=#ff0000>Erro ao aplicar imagem na entidade.</color>");
					yield break;
				}

				// ✅ SALVAR DADOS
				if (save && player != null)
				{
					Vector3 pos = entity.transform.position;
					string prefab = entity.PrefabName;
					ulong netId = entity.net.ID.Value;

					string monumentId = plugin.monumentSigns.FirstOrDefault(m =>
						Vector3.Distance(m.Value.Position, pos) < 1f &&
						m.Value.PrefabName == prefab).Key;

					var saved = plugin.savedSigns.FirstOrDefault(s =>
						s.NetworkId == netId ||
						Vector3.Distance(s.Position, pos) < 1f);

					if (saved == null)
					{
						plugin.savedSigns.Add(new SavedSign
						{
							Position = pos,
							ImageURL = url,
							OwnerId = player.userID,
							PrefabName = prefab,
							IsAdminSign = player.IsAdmin,
							MonumentSignId = monumentId,
							NetworkId = netId
						});
					}
					else
					{
						saved.ImageURL = url;
						saved.IsAdminSign = player.IsAdmin;
						saved.MonumentSignId = monumentId;
						saved.NetworkId = netId;
						saved.Position = pos;
					}

					plugin.SaveData();
				}

				if (player != null)
				{
					int count = plugin.savedSigns.Count(s => s.OwnerId == player.userID);
					int limit = plugin.GetPlayerImageLimit(player);
					player.ChatMessage($"<color=#00ff00>Imagem aplicada com sucesso! ({count}/{(limit == int.MaxValue ? "∞" : limit.ToString())} usadas)</color>");
				}
			}
		}

		private bool ApplyImageToEntity(BaseEntity entity, byte[] pngData)
		{
			if (entity == null || entity.IsDestroyed) return false;

			try
			{
				FileStorage.server.RemoveAllByEntity(entity.net.ID);

				if (entity is Signage signage)
				{
					signage.textureIDs[0] = FileStorage.server.Store(pngData, FileStorage.Type.png, signage.net.ID);
					signage.SendNetworkUpdate();
					signage.SendNetworkUpdateImmediate();
					return true;
				}

				// Suporte para PhotoFrames
				if (entity.ShortPrefabName.Contains("photoframe"))
				{
					var field = entity.GetType().GetField("textureIDs");
					if (field != null)
					{
						uint[] textures = (uint[])field.GetValue(entity);
						textures[0] = FileStorage.server.Store(pngData, FileStorage.Type.png, entity.net.ID);
						field.SetValue(entity, textures);
						entity.SendNetworkUpdate();
						entity.SendNetworkUpdateImmediate();
						return true;
					}
				}
			}
			catch (System.Exception ex)
			{
				PrintError($"Erro ao aplicar imagem: {ex.Message}");
			}

			return false;
		}

		[ChatCommand("sil")]
		private void CmdImgUrl(BasePlayer player, string command, string[] args)
		{
			if (!permission.UserHasPermission(player.UserIDString, PermissionUse))
			{
				player.ChatMessage("<color=#ff0000>Você não tem permissão para usar este comando.</color>");
				return;
			}

			if (args.Length < 1)
			{
				player.ChatMessage("<color=#ffaa00>Uso: /sil [URL da imagem PNG/JPG]</color>");
				player.ChatMessage("<color=#ffaa00>Exemplo: /sil https://i.imgur.com/exemplo.png</color>");
				return;
			}

			string url = string.Join(" ", args).Trim();

			// ✅ RAYCAST MELHORADO
			RaycastHit hit;
			if (!Physics.Raycast(player.eyes.HeadRay(), out hit, config.SignDetectionRadius))
			{
				player.ChatMessage($"<color=#ffaa00>Olhe para uma placa a menos de {config.SignDetectionRadius}m de distância.</color>");
				return;
			}

			BaseEntity entity = hit.GetEntity();

			if (entity == null || string.IsNullOrEmpty(entity.PrefabName) || !IsValidSignPrefab(entity.PrefabName))
			{
				player.ChatMessage("<color=#ffaa00>Isso não é uma placa suportada.</color>");
				return;
			}

			// ✅ Verifica se é Signage e respeita proteção
			if (entity is Signage signage && !signage.CanUpdateSign(player))
			{
				player.ChatMessage("<color=#ff0000>Você não pode alterar esta placa.</color>");
				return;
			}

			// ✅ Verifica acesso ao TC (se necessário)
			if (config.RequireTCAuth && !player.IsBuildingAuthed() && 
				!permission.UserHasPermission(player.UserIDString, PermissionAllSigns) && 
				!permission.UserHasPermission(player.UserIDString, "smartsignurl.ignoretc") && 
				!player.IsAdmin)
			{
				player.ChatMessage("<color=#ff0000>Você precisa ter acesso ao armário de ferramentas para alterar esta placa.</color>");
				return;
			}

			// ✅ Verificação de limite
			int currentCount = savedSigns.Count(s => s.OwnerId == player.userID);
			int limit = GetPlayerImageLimit(player);
			if (currentCount >= limit)
			{
				player.ChatMessage($"<color=#ff0000>Limite de imagens atingido ({limit}). Remova algumas primeiro.</color>");
				return;
			}

			player.ChatMessage("<color=#00ffff>Baixando e aplicando imagem...</color>");
			loader.StartCoroutine(loader.DownloadImage(player, entity, url, true));
		}

        [ChatCommand("imgclear")]
        private void CmdImgClear(BasePlayer player, string command, string[] args)
        {
            RaycastHit hit;
            if (!Physics.Raycast(player.eyes.HeadRay(), out hit, config.SignDetectionRadius))
            {
                player.ChatMessage($"<color=#ffaa00>Olhe para uma placa a menos de {config.SignDetectionRadius}m de distância.</color>");
                return;
            }

            Signage sign = hit.GetEntity() as Signage;
            if (sign == null)
            {
                player.ChatMessage("<color=#ffaa00>Isso não é uma placa válida.</color>");
                return;
            }

            // ✅ BUSCA MELHORADA
            var match = savedSigns.FirstOrDefault(s => 
                (s.NetworkId == sign.net.ID.Value || Vector3.Distance(s.Position, sign.transform.position) < 1f) && 
                (s.OwnerId == player.userID || player.IsAdmin));

            if (match == null)
            {
                player.ChatMessage("<color=#ff0000>Você não é o dono desta imagem ou ela não foi registrada pelo plugin.</color>");
                return;
            }

            // ✅ LIMPAR IMAGEM
            FileStorage.server.RemoveAllByEntity(sign.net.ID);
            sign.textureIDs[0] = 0;
            sign.SendNetworkUpdate();
            sign.SendNetworkUpdateImmediate();

            savedSigns.Remove(match);
            SaveData();

            int count = savedSigns.Count(s => s.OwnerId == player.userID);
            int limit = GetPlayerImageLimit(player);
            player.ChatMessage($"<color=#00ff00>Imagem removida! ({count}/{(limit == int.MaxValue ? "∞" : limit.ToString())} usadas)</color>");
        }

        [ChatCommand("imgclearall")]
        private void CmdImgClearAll(BasePlayer player, string command, string[] args)
        {
            if (!player.IsAdmin)
            {
                player.ChatMessage("<color=#ff0000>Apenas administradores podem usar este comando.</color>");
                return;
            }

            if (args.Length != 1 || !ulong.TryParse(args[0], out ulong targetId))
            {
                player.ChatMessage("<color=#ffaa00>Uso: /imgclearall [SteamID64]</color>");
                return;
            }

            var signsToRemove = savedSigns.Where(s => s.OwnerId == targetId).ToList();
            int count = 0;

            foreach (var signData in signsToRemove)
            {
                // ✅ BUSCA MELHORADA POR NETWORK ID E POSIÇÃO
                Signage sign = null;
                
                if (signData.NetworkId != 0UL)
                {
                    var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(signData.NetworkId));
                    sign = entity as Signage;
                }

                if (sign == null)
                {
                    sign = UnityEngine.Object.FindObjectsOfType<Signage>()
                        .FirstOrDefault(s => Vector3.Distance(s.transform.position, signData.Position) < 1f);
                }

                if (sign != null && !sign.IsDestroyed)
                {
                    FileStorage.server.RemoveAllByEntity(sign.net.ID);
                    sign.textureIDs[0] = 0;
                    sign.SendNetworkUpdate();
                    sign.SendNetworkUpdateImmediate();
                    count++;
                }
                
                savedSigns.Remove(signData);
            }

            SaveData();
            player.ChatMessage($"<color=#00ff00>Removidas {count} imagens do jogador {targetId}.</color>");
        }

        [ChatCommand("silinfo")]
        private void CmdInfo(BasePlayer player)
        {
            player.ChatMessage("<color=#00ffff>=== SmartSignURL v2.2.0 ===</color>");
            player.ChatMessage("<color=#ffff00>Comandos:</color>");
            player.ChatMessage("<color=#ffffff>• /sil [URL] - Aplicar imagem PNG/JPG</color>");
            player.ChatMessage("<color=#ffffff>• /imgclear - Remover imagem da placa</color>");
            player.ChatMessage("<color=#ffffff>• /imgclearall [SteamID] - Admin: limpar todas</color>");
            player.ChatMessage("<color=#ffffff>• /silinfo - Mostrar esta ajuda</color>");
            
            int count = savedSigns.Count(s => s.OwnerId == player.userID);
            int limit = GetPlayerImageLimit(player);
            player.ChatMessage($"<color=#ffff00>Suas imagens: {count}/{(limit == int.MaxValue ? "∞" : limit.ToString())}</color>");
            
            player.ChatMessage("<color=#ffff00>Configurações:</color>");
            player.ChatMessage($"<color=#ffffff>• Tamanho máximo: {config.MaxImageSizeMB}MB</color>");
            player.ChatMessage($"<color=#ffffff>• Dimensões máximas: {config.MaxImageWidth}x{config.MaxImageHeight}</color>");
            player.ChatMessage($"<color=#ffffff>• Distância detecção: {config.SignDetectionRadius}m</color>");
            player.ChatMessage($"<color=#ffffff>• Formatos: PNG, JPG/JPEG</color>");
        }
    }
}