using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Rust;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("MergeDrops", "Mestre Pardal", "1.5.4")]
    [Description("Agrupa automaticamente itens dropados próximos quando atingem certa quantidade e continua agrupando até lotar, com buscas otimizadas.")]
    public class MergeDrops : RustPlugin
    {
        #region Config

        private PluginConfig config;

        public class PluginConfig
        {
            public float MergeRadius = 2f;
            public float BagLifetimeSeconds = 600f;
            public int MaxItemsPerBag = 48;
            public int MinItemsToTriggerMerge = 4;

            // Somente bolsas sem dono (OwnerID == 0) podem ser alvo do merge
            public bool OnlyMergeUnownedBags = true;

            // Faz checagens extras para containers válidos
            public bool StrictContainerChecks = true;

            // Delay após o drop para processar (segundos)
            public float ProcessDelay = 2f;
        }

        // Gera e grava a config padrão quando o arquivo não existe
        protected override void LoadDefaultConfig()
        {
            PrintWarning("Criando config padrão de MergeDrops...");
            config = new PluginConfig();
            SaveConfig();
        }

        // Lê a config; se falhar, recria o padrão
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null)
                {
                    PrintWarning("Config nula, recriando padrão...");
                    LoadDefaultConfig();
                }
            }
            catch (Exception e)
            {
                PrintWarning($"Falha ao ler config: {e.Message}. Recriando padrão...");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(config, true);

        #endregion

        #region Const / State

        private const string LOOT_BAG_PREFAB = "assets/prefabs/misc/item drop/item_drop.prefab";
        private const string PERM_ADMIN = "mergedrops.admin";

        private readonly HashSet<Timer> activeTimers = new HashSet<Timer>();

        // Usa o mesmo tipo do teu build (NetworkableId.Value = ulong)
        private readonly HashSet<ulong> processingNetIds = new HashSet<ulong>();

        private readonly List<BaseEntity> tmpEntities = new List<BaseEntity>(128);

        #endregion

        #region Hooks

        void Init()
        {
            permission.RegisterPermission(PERM_ADMIN, this);
        }

        void Unload()
        {
            foreach (var t in activeTimers.ToArray())
            {
                try { t?.Destroy(); } catch { /* ignore */ }
            }
            activeTimers.Clear();
            processingNetIds.Clear();
        }

        void OnEntityKill(BaseNetworkable ent)
        {
            try
            {
                if (ent?.net != null)
                {
                    ulong nid = ent.net.ID.Value;
                    if (nid != 0UL) processingNetIds.Remove(nid);
                }
            }
            catch { /* ignore */ }
        }

        // Chamado quando um item vira entidade no mundo
        private void OnItemDropped(Item item, BaseEntity ent)
        {
            if (item == null || ent == null || ent.IsDestroyed || ent.net == null) return;

            ulong nid = ent.net.ID.Value;
            if (!processingNetIds.Add(nid)) return; // já está processando

            var delay = Mathf.Max(0.05f, config.ProcessDelay);
            var timerRef = timer.Once(delay, () =>
            {
                try
                {
                    // (1) Valida de novo
                    if (ent == null || ent.IsDestroyed || item == null)
                        return;

                    ProcessItemDrop(item, ent);
                }
                catch (Exception ex)
                {
                    PrintError($"Erro ao processar item dropado: {ex}");
                }
                finally
                {
                    if (ent?.net != null) processingNetIds.Remove(ent.net.ID.Value);

                    // Limpa timers mortos do set
                    foreach (var t in activeTimers.ToArray())
                        if (t == null || t.Destroyed) activeTimers.Remove(t);
                }
            });

            if (timerRef != null) activeTimers.Add(timerRef);
        }

        #endregion

        #region Core

        private void ProcessItemDrop(Item item, BaseEntity ent)
        {
            var pos = ent.transform.position;

            // 1) Encontra bolsas próximas
            var nearbyBags = GetNearbyBags(pos, config.MergeRadius);

            if (nearbyBags.Count == 0)
            {
                // 2) Se não tem bolsa por perto, checa quantos itens soltos existem
                var dropped = GetNearbyLooseItems(pos, config.MergeRadius);
                if (dropped.Count < config.MinItemsToTriggerMerge)
                    return;

                // Cria uma bolsa e puxa tudo pra dentro
                CreateLootBagAndSuckItems(dropped, pos);
                // O item atual será sugado acima (se estiver na lista); se não, tenta mover:
                TryMoveItemToClosestNewBag(item, ent, pos);
                return;
            }

            // 3) Tenta mover pra uma bolsa existente
            foreach (var bag in nearbyBags)
            {
                if (!IsValidMergeTarget(bag)) continue;
                if (bag.inventory.itemList.Count >= config.MaxItemsPerBag) continue;

                if (item.MoveToContainer(bag.inventory))
                {
                    SafeKill(ent);
                    return;
                }
            }

            // 4) Se não coube, cria nova bolsa
            var newBag = CreateLootBag(pos);
            if (newBag != null && item.MoveToContainer(newBag.inventory))
                SafeKill(ent);
        }

        private List<DroppedItemContainer> GetNearbyBags(Vector3 pos, float radius)
        {
            var result = new List<DroppedItemContainer>(32);
            try
            {
                tmpEntities.Clear();
                Vis.Entities(pos, radius, tmpEntities);

                foreach (var be in tmpEntities)
                {
                    if (be == null || be.IsDestroyed) continue;
                    var bag = be as DroppedItemContainer;
                    if (bag == null) continue;

                    if (!bag.IsValid() || bag.IsDestroyed) continue;
                    if (config.OnlyMergeUnownedBags && bag.OwnerID != 0) continue;
                    if (config.StrictContainerChecks && bag.inventory == null) continue;

                    result.Add(bag);
                }
            }
            catch (Exception ex)
            {
                PrintError($"Erro ao buscar bolsas próximas: {ex}");
            }
            finally
            {
                tmpEntities.Clear();
            }
            return result;
        }

        private List<DroppedItem> GetNearbyLooseItems(Vector3 pos, float radius)
        {
            var result = new List<DroppedItem>(32);
            try
            {
                tmpEntities.Clear();
                Vis.Entities(pos, radius, tmpEntities);

                foreach (var be in tmpEntities)
                {
                    if (be == null || be.IsDestroyed) continue;
                    var dropped = be as DroppedItem;
                    if (dropped == null) continue;
                    if (!dropped.IsValid() || dropped.IsDestroyed) continue;
                    if (dropped.item == null) continue;
                    result.Add(dropped);
                }
            }
            catch (Exception ex)
            {
                PrintError($"Erro ao buscar itens próximos: {ex}");
            }
            finally
            {
                tmpEntities.Clear();
            }
            return result;
        }

        private void CreateLootBagAndSuckItems(List<DroppedItem> items, Vector3 pos)
        {
            var bag = CreateLootBag(pos);
            if (bag == null) return;

            foreach (var di in items)
            {
                try
                {
                    if (di == null || di.IsDestroyed) continue;
                    var it = di.item;
                    if (it == null) continue;

                    if (bag.inventory.itemList.Count >= config.MaxItemsPerBag)
                        break;

                    if (it.MoveToContainer(bag.inventory))
                        SafeKill(di);
                }
                catch (Exception ex)
                {
                    PrintError($"Erro ao mover item para bolsa: {ex}");
                }
            }
        }

        private DroppedItemContainer CreateLootBag(Vector3 pos)
        {
            try
            {
                var bag = GameManager.server.CreateEntity(LOOT_BAG_PREFAB, pos + Vector3.up * 0.2f) as DroppedItemContainer;
                if (bag == null)
                {
                    PrintError("Falha ao criar loot bag.");
                    return null;
                }

                // Inicializa container
                bag.inventory = new ItemContainer
                {
                    capacity = Mathf.Max(1, config.MaxItemsPerBag),
                    maxStackSize = int.MaxValue,
                    isServer = true,
                    entityOwner = bag
                };
                bag.inventory.ServerInitialize(null, bag.inventory.capacity);
                bag.inventory.GiveUID();

                bag.OwnerID = 0;
                bag.Spawn();

                var life = Mathf.Max(60f, config.BagLifetimeSeconds);
                var lifeTimer = timer.Once(life, () =>
                {
                    try { SafeKill(bag); }
                    finally
                    {
                        foreach (var t in activeTimers.ToArray())
                            if (t == null || t.Destroyed) activeTimers.Remove(t);
                    }
                });
                if (lifeTimer != null) activeTimers.Add(lifeTimer);

                return bag;
            }
            catch (Exception ex)
            {
                PrintError($"Erro ao criar loot bag: {ex}");
                return null;
            }
        }

        private void TryMoveItemToClosestNewBag(Item item, BaseEntity ent, Vector3 pos)
        {
            var bags = GetNearbyBags(pos, 0.8f); // bem pertinho da que foi criada
            foreach (var b in bags)
            {
                if (!IsValidMergeTarget(b)) continue;
                if (item.MoveToContainer(b.inventory))
                {
                    SafeKill(ent);
                    break;
                }
            }
        }

        private bool IsValidMergeTarget(DroppedItemContainer bag)
        {
            if (bag == null || bag.IsDestroyed) return false;
            if (config.OnlyMergeUnownedBags && bag.OwnerID != 0) return false;
            if (config.StrictContainerChecks && bag.inventory == null) return false;
            return true;
        }

        private void SafeKill(BaseEntity e)
        {
            if (e == null) return;
            try
            {
                if (!e.IsDestroyed) e.Kill();
            }
            catch (Exception ex)
            {
                PrintError($"Erro ao destruir entidade {e.ShortPrefabName}: {ex}");
            }
        }

        #endregion

        #region Chat Commands

        [Command("cleardrops")]
        private void CmdClearDrops(IPlayer icaller, string cmd, string[] args)
        {
            var bp = icaller?.Object as BasePlayer;
            if (bp != null && !permission.UserHasPermission(bp.UserIDString, PERM_ADMIN))
            {
                bp.ChatMessage("Você não tem permissão para usar este comando.");
                return;
            }

            int removed = 0;
            try
            {
                tmpEntities.Clear();
                Vis.Entities(Vector3.zero, float.MaxValue, tmpEntities); // varredura única
                foreach (var be in tmpEntities)
                {
                    var bag = be as DroppedItemContainer;
                    if (bag == null || bag.IsDestroyed) continue;
                    if (config.OnlyMergeUnownedBags && bag.OwnerID != 0) continue;

                    SafeKill(bag);
                    removed++;
                }
            }
            catch (Exception ex)
            {
                PrintError($"Erro ao limpar drops: {ex}");
            }
            finally
            {
                tmpEntities.Clear();
            }

            icaller?.Reply($"<color=#00ffff>MergeDrops:</color> Limpou <color=yellow>{removed}</color> bolsas dropadas.");
        }

        [Command("mergeinfo")]
        private void CmdInfo(IPlayer icaller, string cmd, string[] args)
        {
            icaller?.Reply($"<color=#00ffff>=== MergeDrops v1.5.4 ===</color>");
            icaller?.Reply($"<color=yellow>• Agrupa quando há {config.MinItemsToTriggerMerge}+ itens no chão</color>");
            icaller?.Reply($"<color=yellow>• Raio de agrupamento: {config.MergeRadius}m</color>");
            icaller?.Reply($"<color=yellow>• Máximo por bolsa: {config.MaxItemsPerBag} itens</color>");
            icaller?.Reply($"<color=yellow>• Bolsas duram: {Mathf.RoundToInt(config.BagLifetimeSeconds/60f)} minutos</color>");
            icaller?.Reply($"<color=yellow>• /cleardrops (perm: {PERM_ADMIN})</color>");
            icaller?.Reply($"<color=cyan>• Timers ativos: {activeTimers.Count}</color>");
            icaller?.Reply($"<color=cyan>• Entidades em processamento: {processingNetIds.Count}</color>");
        }

        #endregion
    }
}
