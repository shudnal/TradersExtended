using BepInEx;
using GUIFramework;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TradersExtended.Compatibility;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static partial class StorePanel
    {
        public class ItemToSell
        {
            public enum ItemType
            {
                Single,
                Stack,
                Combined
            }

            private const string c_buybackItem = "buybackItem";

            public ItemType itemType = ItemType.Single;
            public ItemDrop.ItemData item;
            public int stack;
            public int price;
            public int amount;
            public int quality;
            public int pricePerItem;
            public ItemDrop currency;
            internal float priceFactor = 1f;
            internal List<ItemDrop.ItemData> sourceItems = new List<ItemDrop.ItemData>();
            internal List<ItemDrop.ItemData> soldItems;

            public ItemToSell Clone()
            {
                ItemToSell obj = MemberwiseClone() as ItemToSell;
                obj.item = item.Clone();
                return obj;
            }

            public static bool IsBuyBackItem(Trader.TradeItem item)
            {
                return item?.m_requiredGlobalKey == c_buybackItem;
            }

            public static Trader.TradeItem SetBuyBackItem(Trader.TradeItem item)
            {
                item.m_requiredGlobalKey = c_buybackItem;
                return item;
            }
        }

        private static bool tradeInProgress;
        private static int requestedBuyLots = 1;
        private static ItemToSell buybackItem;

        internal static int GetSellLotSize(ItemToSell offer)
        {
            return offer.itemType == ItemToSell.ItemType.Stack ? offer.stack :
                offer.itemType == ItemToSell.ItemType.Single ? offer.item.m_stack : 1;
        }

        private static IEnumerable<ItemDrop.ItemData> GetSellSources(ItemToSell offer, StoreGui store)
        {
            Inventory inventory = Player.m_localPlayer.GetInventory();
            return offer.sourceItems.Where(item => inventory.ContainsItem(item) &&
                item.m_quality == offer.quality && item.m_worldLevel >= Game.m_worldLevel &&
                !IgnoreItemForSell(item, store.m_trader));
        }

        internal static bool TryGetSellQuote(ItemToSell offer, int lots, out int amount, out int price)
        {
            amount = price = 0;
            return offer?.item != null && TradeAmounts.TryGetItemCount(GetSellLotSize(offer), lots, out amount) &&
                TradeAmounts.TryGetPrice(offer.pricePerItem, lots, offer.priceFactor, out price, roundUp: false);
        }

        internal static int GetMaximumSellLots(StoreGui store, ItemToSell offer)
        {
            if (store?.m_trader == null || offer?.item == null || Player.m_localPlayer == null)
                return 0;
            int available = TradeAmounts.ClampBalance(GetSellSources(offer, store).Sum(item => (long)item.m_stack));
            int budget = TraderConfigManager.Get(store.m_trader).TradersUseCoins ? TraderCoins.GetTraderCoins() : int.MaxValue;
            return TradeAmounts.MaximumSellLots(GetSellLotSize(offer), offer.pricePerItem, available, offer.priceFactor, budget);
        }

        internal static bool HasPlayerKeyReward(Trader.TradeItem offer) => !string.IsNullOrEmpty(offer?.m_buyKey);

        internal static string GetBuyOfferName(Trader.TradeItem offer)
        {
            if (!string.IsNullOrWhiteSpace(offer?.m_name))
                return offer.m_name;
            return offer?.m_prefab != null ? offer.m_prefab.m_itemData?.m_shared?.m_name ?? string.Empty : string.Empty;
        }

        private static bool IsBuyOfferAvailable(Trader.TradeItem offer)
        {
            Player player = Player.m_localPlayer;
            return offer != null && player != null && offer.m_price >= 0 && !ItemToSell.IsBuyBackItem(offer) &&
                (string.IsNullOrEmpty(offer.m_requiredGlobalKey) ||
                    (ZoneSystem.instance != null && offer.m_requiredGlobalKey.Split(',')
                        .Select(key => key.Trim()).Where(key => key.Length > 0).All(key => ZoneSystem.instance.GetGlobalKey(key)))) &&
                (!HasPlayerKeyReward(offer) || !player.HaveUniqueKey(offer.m_buyKey)) &&
                (offer.m_prefab != null ? offer.m_stack > 0 && offer.m_prefab.m_itemData?.m_shared != null : HasPlayerKeyReward(offer));
        }

        internal static bool TryGetBuyPrice(Trader.TradeItem offer, int lots, out int price)
        {
            price = 0;
            if (offer == null || offer.m_price < 0 || lots <= 0)
                return false;
            long total = (long)offer.m_price * lots;
            if (total > int.MaxValue)
                return false;
            price = (int)total;
            return true;
        }

        private static bool TryGetIncrementedValue(Trader.TradeItem offer, Player player, out int value)
        {
            value = 0;
            if (!HasPlayerKeyReward(offer) || string.IsNullOrEmpty(offer.m_incrementKey))
                return true;
            if (player.TryGetUniqueKeyValue(offer.m_incrementKey, out string current))
                int.TryParse(current, out value);
            long next = (long)value + offer.m_incrementAmount;
            if (next < int.MinValue || next > int.MaxValue)
                return false;
            value = (int)next;
            // Native SetInventorySize also drops out-of-bounds items. Never purchase a shrink of an
            // already larger inventory (including an inventory expanded by another mod).
            return offer.m_incrementKey != Player.InventoryRowsKey ||
                (InventoryGui.instance != null && Mathf.Clamp(value, 0, 9) >= player.GetInventory().GetHeight());
        }

        private sealed class PlayerRewardSnapshot : IDisposable
        {
            private readonly Player player;
            private readonly string[] keys;
            private readonly int height;
            private bool committed;

            internal PlayerRewardSnapshot(Player player)
            {
                this.player = player;
                keys = player.m_uniques.ToArray();
                height = player.GetInventory().GetHeight();
            }

            internal void Commit() => committed = true;

            public void Dispose()
            {
                if (committed)
                    return;
                player.m_uniques.Clear();
                player.m_uniques.UnionWith(keys);
                if (player.GetInventory().GetHeight() != height)
                {
                    // Rollback must not call SetInventorySize: its clamp/drop path would discard items.
                    player.GetInventory().SetHeight(height);
                    InventoryGui.instance?.SetInventorySize(height);
                }
                ZoneSystem.instance?.UpdateWorldRates();
                player.UpdateEvents();
            }
        }

        internal static int GetMaximumBuyLots(StoreGui store, Trader.TradeItem offer)
        {
            if (store?.m_trader == null || !IsBuyOfferAvailable(offer) ||
                !TryGetIncrementedValue(offer, Player.m_localPlayer, out _))
                return 0;

            Inventory inventory = Player.m_localPlayer.GetInventory();
            ItemDrop currency = TraderCurrency.GetCurrency(offer, store);
            if (currency == null)
                return 0;
            int currencyAmount = TradeInventory.CountCurrency(inventory, currency);
            int candidate = offer.m_price == 0 ? TradeAmounts.MaximumSliderLots :
                Math.Min(TradeAmounts.MaximumSliderLots, currencyAmount / offer.m_price);
            if (HasPlayerKeyReward(offer))
                candidate = Math.Min(candidate, 1);
            if (offer.m_prefab == null)
                return candidate;

            TradeableItem.GetStackQualityFromStack(offer.m_stack, out int lotSize, out int quality);
            if (lotSize <= 0)
                return 0;
            ItemDrop.ItemData item = offer.m_prefab.m_itemData;
            quality = quality == 0 ? item.m_quality : quality;
            candidate = Math.Min(candidate, int.MaxValue / lotSize);

            // Capacity increases as payment empties stacks. Reduce the candidate to its own
            // post-payment capacity until the greatest feasible whole-lot count is reached.
            while (candidate > 0)
            {
                if (!TryGetBuyPrice(offer, candidate, out int price) ||
                    !TradeInventory.PlanRemoval(inventory,
                        inventory.GetAllItems().Where(existing => TradeInventory.MatchesCurrency(existing, currency)),
                        price, out List<TradeInventory.Removal> payment))
                    return 0;

                int capacity = TradeInventory.CapacityAfterRemoval(inventory, item, quality, Game.m_worldLevel, payment);
                int capacityLots = capacity / lotSize;
                if (capacityLots >= candidate)
                    return candidate;
                candidate = capacityLots;
            }
            return 0;
        }

        // Keep calling the public StoreGui entry point so other mods' purchase prefixes/postfixes still run.
        internal static void BuyLots(StoreGui store, int lots)
        {
            int previous = requestedBuyLots;
            requestedBuyLots = lots;
            try { store.BuySelectedItem(); }
            finally { requestedBuyLots = previous; }
        }

        public static void SellSelectedItem(StoreGui storeGui)
        {
            SellSelectedItem(storeGui, selectedItem?.itemType == ItemToSell.ItemType.Combined ? selectedItem.amount : 1);
        }

        internal static void SellSelectedItem(StoreGui storeGui, int lots)
        {
            ItemToSell offer = selectedItem;
            if (tradeInProgress || storeGui?.m_trader == null || Player.m_localPlayer == null ||
                !TryGetSellQuote(offer, lots, out int amount, out int price) || !TraderCoins.CanSell(price))
                return;

            ItemDrop currency = offer.currency ?? storeGui.m_coinPrefab;
            if (currency == null || TradeInventory.PrefabName(offer.item) == Utils.GetPrefabName(currency.gameObject))
                return;
            Inventory inventory = Player.m_localPlayer.GetInventory();
            if (!TradeInventory.PlanRemoval(inventory, GetSellSources(offer, storeGui), amount, out List<TradeInventory.Removal> removal))
            {
                storeGui.FillList();
                return;
            }

            ItemToSell receipt = offer.Clone();
            receipt.amount = receipt.stack = amount;
            receipt.price = price;
            receipt.currency = currency;
            receipt.soldItems = TradeInventory.CopyRemovedItems(removal);
            bool cheated = TradeInventory.IsCheated(removal);
            List<GameObject> droppedObjects = new List<GameObject>();
            bool committed = false;
            tradeInProgress = true;
            try
            {
                using (TradeInventory.Snapshot snapshot = new TradeInventory.Snapshot(inventory))
                {
                    if (!TradeInventory.Remove(inventory, removal))
                        return;

                    int inInventory = Math.Min(price, TradeInventory.Capacity(inventory, currency.m_itemData,
                        currency.m_itemData.m_quality, Game.m_worldLevel));
                    if (inInventory > 0 && !TradeInventory.AddPrefab(inventory, currency, inInventory, currency.m_itemData.m_quality, cheated))
                        return;

                    int remaining = price - inInventory;
                    int maximumStack = Math.Max(currency.m_itemData.m_shared.m_maxStackSize, 1);
                    if (((long)remaining + maximumStack - 1) / maximumStack > 100)
                    {
                        Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$inventory_full");
                        return;
                    }
                    while (remaining > 0)
                    {
                        GameObject dropped = UnityEngine.Object.Instantiate(currency.gameObject,
                            Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 2f + Vector3.up,
                            Quaternion.identity);
                        droppedObjects.Add(dropped);
                        ItemDrop item = dropped.GetComponent<ItemDrop>();
                        item.m_itemData.m_stack = Math.Min(remaining, maximumStack);
                        item.m_itemData.m_worldLevel = Game.m_worldLevel;
                        item.m_itemData.m_cheated = cheated;
                        item.Save();
                        remaining -= item.m_itemData.m_stack;
                    }

                    snapshot.Commit();
                    committed = true;
                    TraderCoins.UpdateTraderCoins(-price);
                    if (TraderConfigManager.Get(storeGui.m_trader).EnableBuybackForLastItemSold)
                        BuybackManager.Set(storeGui.m_trader, receipt);

                    if (price > inInventory)
                        Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft,
                            $"$msg_dropped: {TraderCurrency.GetCurrencyName(currency)} x{price - inInventory}");
                }

                string itemText = amount <= 1 ? offer.item.m_shared.m_name : $"{amount}x{offer.item.m_shared.m_name}";
                storeGui.m_sellEffects.Create(storeGui.transform.position, Quaternion.identity);
                string soldMessage = Localization.instance.Localize("$msg_sold", itemText, price.ToString());
                if (currency.m_itemData.m_shared.m_name != CoinsPatches.itemDropNameCoins)
                    soldMessage = $"{Localization.instance.Localize(itemText)}: {price} {TraderCurrency.GetCurrencyName(currency)}";
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, soldMessage, 0, offer.item.GetIcon());
                storeGui.m_trader.OnSold();
                Gogan.LogEvent("Game", "SoldItem", itemText, 0L);
            }
            finally
            {
                if (!committed)
                    foreach (GameObject dropped in droppedObjects)
                        if (dropped != null)
                        {
                            if (ZNetScene.instance != null)
                                ZNetScene.instance.Destroy(dropped);
                            else
                                UnityEngine.Object.Destroy(dropped);
                        }
                tradeInProgress = false;
                storeGui.FillList();
            }
        }

        private static void PurchaseSelectedItem(StoreGui store, int lots)
        {
            if (tradeInProgress || store?.m_trader == null || Player.m_localPlayer == null || store.m_selectedItem == null)
                return;
            tradeInProgress = true;
            try
            {
                Trader.TradeItem offer = store.m_selectedItem;
                if (ItemToSell.IsBuyBackItem(offer))
                {
                    BuyBackItem(store);
                    return;
                }
                Player player = Player.m_localPlayer;
                if (lots <= 0 || GetMaximumBuyLots(store, offer) < lots ||
                    !TryGetBuyPrice(offer, lots, out int price) || !TryGetIncrementedValue(offer, player, out int nextValue))
                    return;
                TradeableItem.GetStackQualityFromStack(offer.m_stack, out int lotSize, out int quality);
                int amount = 0;
                if (offer.m_prefab != null && !TradeAmounts.TryGetItemCount(lotSize, lots, out amount))
                    return;
                ItemDrop currency = TraderCurrency.GetCurrency(offer, store);
                Inventory inventory = player.GetInventory();
                if (!TradeInventory.PlanRemoval(inventory,
                    inventory.GetAllItems().Where(item => TradeInventory.MatchesCurrency(item, currency)), price, out List<TradeInventory.Removal> payment))
                    return;
                bool cheated = TradeInventory.IsCheated(payment);
                if (offer.m_prefab != null && quality == 0)
                    quality = offer.m_prefab.m_itemData.m_quality;
                using (TradeInventory.Snapshot snapshot = new TradeInventory.Snapshot(inventory))
                using (PlayerRewardSnapshot rewards = HasPlayerKeyReward(offer) ? new PlayerRewardSnapshot(player) : null)
                {
                    // Payment always precedes physical delivery. A failed delivery cannot grant a player key.
                    if (!TradeInventory.Remove(inventory, payment) ||
                        (offer.m_prefab != null && !TradeInventory.AddPrefab(inventory, offer.m_prefab, amount, quality, cheated)))
                        return;
                    if (HasPlayerKeyReward(offer))
                    {
                        player.AddUniqueKey(offer.m_buyKey);
                        if (!string.IsNullOrEmpty(offer.m_incrementKey))
                        {
                            if (offer.m_incrementKey == Player.InventoryRowsKey)
                                player.SetInventorySize(nextValue);
                            else
                                player.AddUniqueKeyValue(offer.m_incrementKey, nextValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }
                    }
                    rewards?.Commit();
                    snapshot.Commit();
                }
                TraderCoins.UpdateTraderCoins(price);
                store.m_trader.OnBought(offer);
                if (offer.m_prefab != null)
                {
                    player.ShowPickupMessage(offer.m_prefab.m_itemData, amount);
                    Gogan.LogEvent("Game", "BoughtItem", offer.m_prefab.name, 0L);
                }
                store.m_buyEffects.Create(store.transform.position, Quaternion.identity);
                offer.m_buyPlayerEffects?.Create(player.transform.position, Quaternion.identity);
                if (offer.m_levelUpEffect)
                    player.OnSkillLevelup(Skills.SkillType.None, 0f);
            }
            finally
            {
                tradeInProgress = false;
                store.FillList();
            }
        }

        public static bool BuyBackItem(StoreGui store)
        {
            if (!ItemToSell.IsBuyBackItem(store.m_selectedItem) || Player.m_localPlayer == null)
                return false;
            ItemToSell offer = BuybackManager.Get(store.m_trader);
            if (offer == null || offer.price <= 0 || offer.currency == null)
                return false;
            Inventory inventory = Player.m_localPlayer.GetInventory();
            if (!TradeInventory.PlanRemoval(inventory,
                inventory.GetAllItems().Where(item => TradeInventory.MatchesCurrency(item, offer.currency)), offer.price,
                out List<TradeInventory.Removal> payment))
                return false;
            List<ItemDrop.ItemData> items = TradeInventory.PrepareBuybackItems(offer.soldItems, payment);
            if (!TradeInventory.CanAddSavedItemsAfterRemoval(inventory, items, payment))
                return false;
            using (TradeInventory.Snapshot snapshot = new TradeInventory.Snapshot(inventory))
            {
                if (!TradeInventory.Remove(inventory, payment) || !TradeInventory.AddSavedItems(inventory, items))
                    return false;
                snapshot.Commit();
            }
            TraderCoins.UpdateTraderCoins(offer.price);
            BuybackManager.Remove(store.m_trader);
            buybackItem = null;
            store.m_selectedItem = null;
            store.m_buyEffects.Create(store.transform.position, Quaternion.identity);
            return true;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.SellItem))]
        public static class StoreGui_SellItem_SellItemFromSellableList
        {
            private static bool Prefix(StoreGui __instance)
            {
                if (!AmountDialog.IsOpen())
                    SellSelectedItem(__instance);

                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.CanAfford))]
        public static class StoreGui_CanAfford_CustomCurrency
        {
            private static bool Prefix(StoreGui __instance, Trader.TradeItem item, ref bool __result)
            {
                __result = item != null && item.m_price >= 0 && TraderCurrency.GetPlayerCurrencyAmount(item, __instance) >= item.m_price;
                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.BuySelectedItem))]
        public static class StoreGui_BuySelectedItem_TraderCoinsUpdate
        {
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(StoreGui __instance)
            {
                if (!AmountDialog.IsOpen())
                    PurchaseSelectedItem(__instance, requestedBuyLots);
                return false;
            }
        }
    }
}
