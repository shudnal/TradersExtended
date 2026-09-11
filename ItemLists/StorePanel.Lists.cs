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
        private sealed class SellPrice
        {
            internal int Price;
            internal string CurrencyPrefab;
        }

        private static readonly Dictionary<string, Dictionary<int, SellPrice>> tempItemsPrice = new Dictionary<string, Dictionary<int, SellPrice>>();

        private static void AddItemToSellList(TradeableItem item)
        {
            if (item == null || item.price <= 0 || item.stack <= 0 || !item.RequirementsMet())
                return;

            if (!TryGetPriceKey(item, out string key))
                return;

            if (!tempItemsPrice.ContainsKey(key))
                tempItemsPrice[key] = new Dictionary<int, SellPrice>();

            tempItemsPrice[key][item.stack] = new SellPrice
            {
                Price = item.price,
                CurrencyPrefab = item.currency
            };
        }

        private static bool TryGetPriceKey(TradeableItem item, out string key)
        {
            key = "";
            if (item == null || ObjectDB.instance == null || string.IsNullOrWhiteSpace(item.prefab))
                return false;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
            if (prefab == null || !prefab.TryGetComponent(out ItemDrop itemDrop))
                return false;

            ItemDrop.ItemData itemData = itemDrop.m_itemData;
            if (itemData.m_shared.m_maxStackSize == 1 && item.stack != 1)
                return false;

            key = GetPriceKey(item.prefab, item.quality);

            return true;
        }

        private static string GetPriceKey(string prefab, int quality)
        {
            return prefab + "|" + quality;
        }

        private static bool IgnoreItemForSell(ItemDrop.ItemData item, Trader trader)
        {
            if (!string.IsNullOrWhiteSpace(playerFilter.text) && Localization.instance.Localize(item.m_shared.m_name).IndexOf(playerFilter.text, StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            if (!TraderConfigManager.Get(trader).HideEquippedAndHotbarItems)
                return false;

            if (item.m_equipped) // Ignore currently equipped item
                return true;
            if (item.m_gridPos.y == 0 && item.IsEquipable()) // Ignore equippable item from first row (hotbar)
                return true;
            if (AzuExtendedPlayerInventory.API.GetSlots().GetItemFuncs.Where(func => func != null).Select(func => func(Player.m_localPlayer)).Contains(item))
                return true;
            if (ExtraSlots.API.GetAllExtraSlotsItems().Contains(item))
                return true;

            return false;
        }

        private static Dictionary<int, SellPrice> GetStackPrices(ItemDrop.ItemData item, out int quality)
        {
            quality = item.m_quality;
            string key = GetPriceKey(TradeInventory.PrefabName(item), quality);
            if (tempItemsPrice.ContainsKey(key))
                return tempItemsPrice[key];

            quality = 0;
            key = GetPriceKey(TradeInventory.PrefabName(item), quality);
            if (tempItemsPrice.ContainsKey(key))
                return tempItemsPrice[key];

            return null;
        }

        internal static int CalculateSellPrice(int basePrice, int amount)
        {
            return CalculateSellPrice(basePrice, amount, TraderCoins.GetPriceFactor(buyPrice: false));
        }

        private static int CalculateSellPrice(int basePrice, int amount, float priceFactor)
        {
            return TradeAmounts.TryGetPrice(basePrice, amount, priceFactor, out int price, roundUp: false) ? price : int.MaxValue;
        }

        private static void AddToSellList(ItemDrop.ItemData item, int itemStack, int itemPrice, float priceFactor, int configuredQuality, ItemToSell.ItemType itemType, ItemDrop currency)
        {
            int effectiveQuality = configuredQuality > 0 ? configuredQuality : item.m_quality;
            int adjustedItemPrice = itemPrice;
            float configuredQualityMultiplier = TraderConfigManager.Get(StoreGui.instance?.m_trader).QualityMultiplier;
            if (itemType != ItemToSell.ItemType.Stack && configuredQualityMultiplier != 0 && configuredQuality == 0 && item.m_quality > 1)
            {
                if (!TradeAmounts.TryGetQualityPrice(itemPrice, item.m_quality, configuredQualityMultiplier, out adjustedItemPrice))
                    return;
            }

            int price = CalculateSellPrice(adjustedItemPrice, itemType == ItemToSell.ItemType.Combined ? itemStack : 1, priceFactor);

            if (itemType == ItemToSell.ItemType.Single)
                tempItems.Add(new ItemToSell()
                {
                    itemType = itemType,
                    item = item,
                    stack = itemStack,
                    price = price,
                    quality = effectiveQuality,
                    currency = currency,
                    pricePerItem = adjustedItemPrice,
                    priceFactor = priceFactor,
                    sourceItems = new List<ItemDrop.ItemData> { item }
                });
            else if (itemType == ItemToSell.ItemType.Stack)
            {
                ItemToSell currentStack = tempItems.Find(tmpItem => tmpItem.itemType == itemType &&
                                                                    tmpItem.stack == itemStack &&
                                                                    TradeInventory.PrefabName(tmpItem.item) == TradeInventory.PrefabName(item) &&
                                                                    tmpItem.quality == effectiveQuality &&
                                                                    tmpItem.price == price &&
                                                                    SameCurrency(tmpItem.currency, currency));
                if (currentStack != null)
                {
                    currentStack.amount = TradeAmounts.ClampBalance((long)currentStack.amount + item.m_stack);
                    currentStack.sourceItems.Add(item);
                }
                else
                    tempItems.Add(new ItemToSell()
                    {
                        itemType = itemType,
                        item = item,
                        stack = itemStack,
                        price = price,
                        amount = item.m_stack,
                        quality = effectiveQuality,
                        currency = currency,
                        pricePerItem = adjustedItemPrice,
                        priceFactor = priceFactor,
                        sourceItems = new List<ItemDrop.ItemData> { item }
                    });
            }
            else if (itemType == ItemToSell.ItemType.Combined)
            {
                ItemToSell currentStack = tempItems.Find(tmpItem => tmpItem.itemType == itemType &&
                                                                    TradeInventory.PrefabName(tmpItem.item) == TradeInventory.PrefabName(item) &&
                                                                    tmpItem.quality == effectiveQuality &&
                                                                    tmpItem.stack == 1 &&
                                                                    tmpItem.pricePerItem == adjustedItemPrice &&
                                                                    SameCurrency(tmpItem.currency, currency));
                if (currentStack != null)
                {
                    currentStack.amount = TradeAmounts.ClampBalance((long)currentStack.amount + itemStack);
                    currentStack.sourceItems.Add(item);
                    currentStack.price = CalculateSellPrice(currentStack.pricePerItem, currentStack.amount, priceFactor);
                }
                else
                {
                    tempItems.Add(new ItemToSell()
                    {
                        itemType = itemType,
                        item = item,
                        stack = 1,
                        price = price,
                        amount = itemStack,
                        quality = effectiveQuality,
                        pricePerItem = adjustedItemPrice,
                        currency = currency,
                        priceFactor = priceFactor,
                        sourceItems = new List<ItemDrop.ItemData> { item }
                    });
                }
            }
        }

        private static bool SameCurrency(ItemDrop first, ItemDrop second)
        {
            if (ReferenceEquals(first, second))
                return true;
            if (first == null || second == null)
                return false;

            return string.Equals(Utils.GetPrefabName(first.gameObject), Utils.GetPrefabName(second.gameObject), StringComparison.Ordinal);
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_FinalizeItems
        {
            public static bool ItemIsValid(ItemDrop item)
            {
                try
                {
                    return item?.m_itemData?.GetIcon() != null;
                }
                catch
                {
                    return false;
                }
            }

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            public static Exception Finalizer(Exception __exception, Trader __instance, ref List<Trader.TradeItem> __result)
            {
                if (__exception != null)
                    return __exception;

                if (__instance == null)
                    return null;

                if (__result == null)
                    __result = new List<Trader.TradeItem>();

                ResolvedTraderConfig config = TraderConfigManager.Get(__instance);
                if (config.DisableOtherModsItems)
                {
                    __result.Clear();

                    AddVanillaAvailableItems(__instance, __result, onlyPlayerKeyRewards: config.DisableVanillaItems);
                }
                else if (config.DisableVanillaItems)
                {
                    RemoveVanillaItems(__instance, __result);
                }

                AddAvailableItems(CommonListKey(ItemsListType.Buy), __instance, __result);
                AddAvailableItems(TraderListKey(__instance, ItemsListType.Buy), __instance, __result);

                ApplyFilterAndFlexiblePrices(__instance, __result);

                buybackItem = BuybackManager.Get(__instance);
                if (buybackItem != null)
                {
                    Trader.TradeItem buybackTradeItem = ItemToSell.SetBuyBackItem(new Trader.TradeItem()
                    {
                        m_prefab = null,
                        m_stack = 0,
                        m_price = buybackItem.price
                    });
                    TraderCurrency.RegisterCurrency(buybackTradeItem, buybackItem.currency, __instance);
                    __result.Insert(0, buybackTradeItem);
                }

                return null;
            }

            private static void AddVanillaAvailableItems(Trader trader, List<Trader.TradeItem> result, bool onlyPlayerKeyRewards)
            {
                List<Trader.TradeItem> vanillaItems = trader.m_items;

                if (vanillaItems == null || vanillaItems.Count == 0)
                    return;

                for (int i = 0; i < vanillaItems.Count; i++)
                {
                    Trader.TradeItem item = vanillaItems[i];

                    // Player-key upgrades have no equivalent in the configurable item lists.
                    if (item == null || (onlyPlayerKeyRewards && !HasPlayerKeyReward(item)))
                        continue;

                    if (IsBuyOfferAvailable(item))
                        result.Add(item);
                }
            }

            private static void RemoveVanillaItems(Trader trader, List<Trader.TradeItem> result)
            {
                if (result == null || result.Count == 0)
                    return;

                List<Trader.TradeItem> vanillaItems = trader.m_items;

                if (vanillaItems == null || vanillaItems.Count == 0)
                    return;

                // Replace ordinary goods without hiding native progression purchases, including
                // offers that grant a player key and deliver an item in the same transaction.
                HashSet<Trader.TradeItem> vanillaSet = new HashSet<Trader.TradeItem>(
                    vanillaItems.Where(item => !HasPlayerKeyReward(item)));

                int writeIndex = 0;

                for (int readIndex = 0; readIndex < result.Count; readIndex++)
                {
                    Trader.TradeItem item = result[readIndex];

                    if (item == null || vanillaSet.Contains(item))
                        continue;

                    if (writeIndex != readIndex)
                        result[writeIndex] = item;

                    writeIndex++;
                }

                if (writeIndex < result.Count)
                    result.RemoveRange(writeIndex, result.Count - writeIndex);
            }

            private static void ApplyFilterAndFlexiblePrices(Trader trader, List<Trader.TradeItem> result)
            {
                if (result == null || result.Count == 0)
                    return;

                float factor = TraderCoins.GetPriceFactor(buyPrice: true);

                string filterText = traderFilter?.text;
                bool filterEnabled = !string.IsNullOrWhiteSpace(filterText);

                for (int i = result.Count - 1; i >= 0; i--)
                {
                    Trader.TradeItem tradeItem = result[i];

                    if (!IsBuyOfferAvailable(tradeItem) ||
                        (tradeItem.m_prefab != null && !ItemIsValid(tradeItem.m_prefab)))
                    {
                        result.RemoveAt(i);
                        continue;
                    }

                    if (filterEnabled)
                    {
                        string itemName = Localization.instance.Localize(GetBuyOfferName(tradeItem));

                        if (itemName.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                    }

                    if (tradeItem.m_price > 0 && TraderConfigManager.Get(trader).TradersUseFlexiblePricing)
                    {
                        tradeItem = CloneTradeItem(tradeItem);

                        if (!TradeAmounts.TryGetPrice(tradeItem.m_price, 1, factor, out int price, roundUp: false))
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                        tradeItem.m_price = price;

                        result[i] = tradeItem;
                    }
                }
            }

            private static Trader.TradeItem CloneTradeItem(Trader.TradeItem item)
            {
                Trader.TradeItem clone = new Trader.TradeItem()
                {
                    m_prefab = item.m_prefab,
                    m_stack = item.m_stack,
                    m_price = item.m_price,
                    m_requiredGlobalKey = item.m_requiredGlobalKey,
                    m_icon = item.m_icon,
                    m_name = item.m_name,
                    m_tooltip = item.m_tooltip,
                    m_buyKey = item.m_buyKey,
                    m_incrementKey = item.m_incrementKey,
                    m_incrementAmount = item.m_incrementAmount,
                    m_buyPlayerEffects = item.m_buyPlayerEffects,
                    m_levelUpEffect = item.m_levelUpEffect
                };
                TraderCurrency.CopyCurrency(item, clone);
                return clone;
            }
        }

        private static void AddAvailableItems(string listKey, Trader trader, List<Trader.TradeItem> result)
        {
            if (!tradeableItems.TryGetValue(listKey, out List<TradeableItem> items))
                return;

            foreach (TradeableItem item in items)
            {
                if (!item.IsItemToSell(trader))
                    continue;

                Trader.TradeItem tradeItem = item.ToTradeItem();
                if (tradeItem == null)
                    continue;

                TraderCurrency.RegisterCurrency(tradeItem, item.currency, trader);
                result.Add(tradeItem);
            }
        }
    }
}
