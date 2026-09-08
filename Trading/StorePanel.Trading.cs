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

        internal static int GetMaximumBuyLots(StoreGui store, Trader.TradeItem offer)
        {
            if (store?.m_trader == null || offer?.m_prefab == null || Player.m_localPlayer == null || ItemToSell.IsBuyBackItem(offer))
                return 0;

            TradeableItem.GetStackQualityFromStack(offer.m_stack, out int lotSize, out int quality);
            ItemDrop.ItemData item = offer.m_prefab.m_itemData;
            quality = quality == 0 ? item.m_quality : quality;
            Inventory inventory = Player.m_localPlayer.GetInventory();
            ItemDrop currency = TraderCurrency.GetCurrency(offer, store);
            int currencyAmount = TradeInventory.CountCurrency(inventory, currency);
            int candidate = TradeAmounts.MaximumBuyLots(lotSize, offer.m_price, currencyAmount, int.MaxValue);

            // Paying happens before the purchased item is inserted. A full inventory can therefore
            // still accept the purchase when the payment empties a currency stack. Capacity is
            // monotonic with the amount paid; iteratively reduce the candidate to the capacity that
            // exists after its own payment plan until the greatest feasible whole-lot count is found.
            while (candidate > 0)
            {
                if (!TradeAmounts.TryGetPrice(offer.m_price, candidate, 1d, out int price) ||
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
                    if (inInventory > 0 && !TradeInventory.AddPrefab(inventory, currency, inInventory, currency.m_itemData.m_quality))
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
                if (GetMaximumBuyLots(store, offer) < lots || offer.m_prefab == null)
                    return;
                TradeableItem.GetStackQualityFromStack(offer.m_stack, out int lotSize, out int quality);
                if (!TradeAmounts.TryGetItemCount(lotSize, lots, out int amount) ||
                    !TradeAmounts.TryGetPrice(offer.m_price, lots, 1d, out int price))
                    return;
                ItemDrop currency = TraderCurrency.GetCurrency(offer, store);
                Inventory inventory = Player.m_localPlayer.GetInventory();
                if (!TradeInventory.PlanRemoval(inventory,
                    inventory.GetAllItems().Where(item => TradeInventory.MatchesCurrency(item, currency)), price, out List<TradeInventory.Removal> payment))
                    return;
                quality = quality == 0 ? offer.m_prefab.m_itemData.m_quality : quality;
                using (TradeInventory.Snapshot snapshot = new TradeInventory.Snapshot(inventory))
                {
                    // Remove only pre-existing currency stacks, not a currency item bought in this operation.
                    if (!TradeInventory.Remove(inventory, payment) || !TradeInventory.AddPrefab(inventory, offer.m_prefab, amount, quality))
                        return;
                    snapshot.Commit();
                }
                TraderCoins.UpdateTraderCoins(price);
                store.m_trader.OnBought(offer);
                store.m_buyEffects.Create(store.transform.position, Quaternion.identity);
                Player.m_localPlayer.ShowPickupMessage(offer.m_prefab.m_itemData, amount);
                Gogan.LogEvent("Game", "BoughtItem", offer.m_prefab.name, 0L);
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
                out List<TradeInventory.Removal> payment) ||
                !TradeInventory.CanAddSavedItemsAfterRemoval(inventory, offer.soldItems, payment))
                return false;
            using (TradeInventory.Snapshot snapshot = new TradeInventory.Snapshot(inventory))
            {
                if (!TradeInventory.Remove(inventory, payment) || !TradeInventory.AddSavedItems(inventory, offer.soldItems))
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
                __result = item != null && item.m_price > 0 && TraderCurrency.GetPlayerCurrencyAmount(item, __instance) >= item.m_price;
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
