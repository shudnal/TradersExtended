using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TradersExtended
{
    /// <summary>Preflight and rollback for local inventory mutations made by a trade.</summary>
    internal static class TradeInventory
    {
        internal sealed class Removal
        {
            internal ItemDrop.ItemData Item;
            internal int Amount;
        }

        internal sealed class Snapshot : IDisposable
        {
            private readonly Inventory inventory;
            private readonly List<ItemDrop.ItemData> items;
            private readonly int[] stacks;
            private readonly Vector2i[] positions;
            private bool committed;

            internal Snapshot(Inventory inventory)
            {
                this.inventory = inventory;
                items = inventory.GetAllItems().ToList();
                stacks = items.Select(item => item.m_stack).ToArray();
                positions = items.Select(item => item.m_gridPos).ToArray();
            }

            internal void Commit() => committed = true;

            public void Dispose()
            {
                if (committed)
                    return;
                // Keep the original ItemData identities: equipment and other mods can reference them.
                inventory.m_inventory.Clear();
                inventory.m_inventory.AddRange(items);
                for (int index = 0; index < items.Count; index++)
                {
                    items[index].m_stack = stacks[index];
                    items[index].m_gridPos = positions[index];
                }
                inventory.Changed();
            }
        }

        internal static string PrefabName(ItemDrop.ItemData item)
        {
            return item?.m_dropPrefab != null ? Utils.GetPrefabName(item.m_dropPrefab) : string.Empty;
        }

        internal static bool MatchesCurrency(ItemDrop.ItemData item, ItemDrop currency)
        {
            return item?.m_shared != null && currency != null && item.m_worldLevel >= Game.m_worldLevel &&
                   string.Equals(PrefabName(item), Utils.GetPrefabName(currency.gameObject), StringComparison.Ordinal);
        }

        internal static int CountCurrency(Inventory inventory, ItemDrop currency)
        {
            return inventory == null || currency == null ? 0 :
                TradeAmounts.ClampBalance(inventory.GetAllItems().Where(item => MatchesCurrency(item, currency))
                    .Sum(item => (long)Math.Max(item.m_stack, 0)));
        }

        internal static int Capacity(Inventory inventory, ItemDrop.ItemData item, int quality, int worldLevel)
        {
            if (inventory == null || item?.m_shared == null || item.m_shared.m_maxStackSize <= 0)
                return 0;

            int maximumStack = item.m_shared.m_maxStackSize;
            long free = (long)Math.Max(inventory.GetEmptySlots(), 0) * maximumStack;
            if (maximumStack > 1)
                foreach (ItemDrop.ItemData existing in inventory.GetAllItems())
                    if (existing.m_shared.m_name == item.m_shared.m_name && existing.m_quality == quality &&
                        existing.m_worldLevel == worldLevel)
                        free += Math.Max(existing.m_shared.m_maxStackSize - existing.m_stack, 0);
            return TradeAmounts.ClampBalance(free);
        }

        internal static int CapacityAfterRemoval(Inventory inventory, ItemDrop.ItemData item, int quality, int worldLevel,
            IEnumerable<Removal> removal)
        {
            if (inventory == null || item?.m_shared == null || item.m_shared.m_maxStackSize <= 0)
                return 0;

            Dictionary<ItemDrop.ItemData, int> removedAmounts = new Dictionary<ItemDrop.ItemData, int>();
            if (removal != null)
                foreach (Removal part in removal)
                {
                    if (part?.Item == null || part.Amount <= 0 || !inventory.ContainsItem(part.Item))
                        return 0;
                    long combined = (long)(removedAmounts.TryGetValue(part.Item, out int current) ? current : 0) + part.Amount;
                    if (combined > part.Item.m_stack)
                        return 0;
                    removedAmounts[part.Item] = (int)combined;
                }

            int maximumStack = item.m_shared.m_maxStackSize;
            int emptySlots = Math.Max(inventory.GetEmptySlots(), 0);
            long free = 0;
            foreach (ItemDrop.ItemData existing in inventory.GetAllItems())
            {
                int removed = removedAmounts.TryGetValue(existing, out int planned) ? planned : 0;
                int remaining = existing.m_stack - removed;
                if (remaining <= 0)
                {
                    if (existing.m_stack > 0)
                        emptySlots++;
                    continue;
                }

                if (maximumStack > 1 && existing.m_shared.m_name == item.m_shared.m_name &&
                    existing.m_quality == quality && existing.m_worldLevel == worldLevel)
                    free += Math.Max(existing.m_shared.m_maxStackSize - remaining, 0);
            }

            free += (long)emptySlots * maximumStack;
            return TradeAmounts.ClampBalance(free);
        }

        internal static bool PlanRemoval(Inventory inventory, IEnumerable<ItemDrop.ItemData> candidates, int amount, out List<Removal> plan)
        {
            plan = new List<Removal>();
            if (inventory == null || amount <= 0)
                return false;
            int remaining = amount;
            foreach (ItemDrop.ItemData item in candidates.Distinct())
            {
                if (item == null || item.m_stack <= 0 || !inventory.ContainsItem(item))
                    continue;
                int take = Math.Min(item.m_stack, remaining);
                plan.Add(new Removal { Item = item, Amount = take });
                remaining -= take;
                if (remaining == 0)
                    return true;
            }
            return false;
        }

        internal static bool Remove(Inventory inventory, List<Removal> plan)
        {
            // Recheck the complete plan before changing the first stack.
            if (plan.Any(part => !inventory.ContainsItem(part.Item) || part.Amount <= 0 || part.Item.m_stack < part.Amount))
                return false;
            foreach (Removal part in plan)
                if (!inventory.RemoveItem(part.Item, part.Amount))
                    return false;
            return true;
        }

        internal static List<ItemDrop.ItemData> CopyRemovedItems(List<Removal> plan)
        {
            return plan.Select(part =>
            {
                ItemDrop.ItemData copy = part.Item.Clone();
                copy.m_stack = part.Amount;
                copy.m_equipped = false;
                return copy;
            }).ToList();
        }

        // Call these methods inside a Snapshot. Valheim can partly add items and still return null/false.
        internal static bool AddPrefab(Inventory inventory, ItemDrop prefab, int amount, int quality)
        {
            if (prefab == null || amount <= 0 || Capacity(inventory, prefab.m_itemData, quality, Game.m_worldLevel) < amount)
                return false;
            bool previousForceDisableInit = ZNetView.m_forceDisableInit;
            try
            {
                return inventory.AddItem(Utils.GetPrefabName(prefab.gameObject), amount, quality,
                    prefab.m_itemData.m_variant, 0L, string.Empty) != null;
            }
            finally
            {
                ZNetView.m_forceDisableInit = previousForceDisableInit;
            }
        }

        private sealed class SavedStackSpace
        {
            internal ItemDrop.ItemData Item;
            internal long Space;
        }

        private static bool CanMergeSavedItem(ItemDrop.ItemData existing, ItemDrop.ItemData saved)
        {
            // Native shared-name stacking can discard variant, crafter and custom data. A buyback
            // receipt may merge only when all persisted per-item properties are interchangeable.
            return existing.m_shared.m_maxStackSize > 1 && !existing.m_equipped &&
                PrefabName(existing) == PrefabName(saved) && existing.m_shared.m_name == saved.m_shared.m_name &&
                existing.m_quality == saved.m_quality && existing.m_worldLevel == saved.m_worldLevel &&
                existing.m_variant == saved.m_variant && existing.m_durability == saved.m_durability &&
                existing.m_crafterID == saved.m_crafterID && existing.m_crafterName == saved.m_crafterName &&
                existing.m_pickedUp == saved.m_pickedUp && existing.m_customData.Count == saved.m_customData.Count &&
                existing.m_customData.All(pair => saved.m_customData.TryGetValue(pair.Key, out string value) && value == pair.Value);
        }

        internal static bool CanAddSavedItems(Inventory inventory, IEnumerable<ItemDrop.ItemData> items)
        {
            if (inventory == null || items == null)
                return false;
            int emptySlots = Math.Max(inventory.GetEmptySlots(), 0);
            List<SavedStackSpace> freeStacks = inventory.GetAllItems().Select(item => new SavedStackSpace
            {
                Item = item,
                Space = Math.Max(item.m_shared.m_maxStackSize - item.m_stack, 0)
            }).ToList();
            foreach (ItemDrop.ItemData saved in items)
            {
                if (saved?.m_shared == null || saved.m_dropPrefab == null || saved.m_stack <= 0 || saved.m_shared.m_maxStackSize <= 0)
                    return false;
                long remaining = saved.m_stack;
                foreach (SavedStackSpace stack in freeStacks)
                {
                    if (stack.Space <= 0 || !CanMergeSavedItem(stack.Item, saved))
                        continue;
                    long added = Math.Min(remaining, stack.Space);
                    stack.Space -= added;
                    remaining -= added;
                    if (remaining == 0)
                        break;
                }
                long slots = (remaining + saved.m_shared.m_maxStackSize - 1) / saved.m_shared.m_maxStackSize;
                if (slots > emptySlots)
                    return false;
                emptySlots -= (int)slots;
                if (slots > 0)
                    freeStacks.Add(new SavedStackSpace
                    {
                        Item = saved,
                        Space = slots * saved.m_shared.m_maxStackSize - remaining
                    });
            }
            return true;
        }

        internal static bool AddSavedItems(Inventory inventory, IEnumerable<ItemDrop.ItemData> items)
        {
            List<ItemDrop.ItemData> savedItems = items?.ToList();
            if (!CanAddSavedItems(inventory, savedItems))
                return false;
            foreach (ItemDrop.ItemData saved in savedItems)
            {
                int remaining = saved.m_stack;
                while (remaining > 0)
                {
                    ItemDrop.ItemData target = inventory.GetAllItems().FirstOrDefault(item =>
                        item.m_stack < item.m_shared.m_maxStackSize && CanMergeSavedItem(item, saved));
                    Vector2i position = target?.m_gridPos ?? inventory.FindEmptySlot(inventory.TopFirst(saved));
                    if (position.x < 0)
                        return false;
                    int capacity = target == null ? saved.m_shared.m_maxStackSize : target.m_shared.m_maxStackSize - target.m_stack;
                    int amount = Math.Min(remaining, capacity);
                    ItemDrop.ItemData copy = saved.Clone();
                    copy.m_equipped = false;
                    copy.m_stack = amount;
                    // Target the planned slot instead of allowing native auto-stack to pick a
                    // shared-name lookalike with different saved properties.
                    if (!inventory.AddItem(copy, amount, position.x, position.y))
                        return false;
                    remaining -= amount;
                }
            }
            return true;
        }
    }
}
