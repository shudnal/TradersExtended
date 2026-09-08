using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static class TooltipPrices
    {
        private sealed class PriceInfo
        {
            internal string Trader;
            internal int Price;
            internal int Stack;
            internal int Quality;
            internal string CurrencyPrefab;
            internal bool Automatic;
            internal TradeableItem Source;
        }

        private static readonly Dictionary<string, List<PriceInfo>> pricesByPrefab = new Dictionary<string, List<PriceInfo>>(StringComparer.Ordinal);

        internal static void Rebuild()
        {
            pricesByPrefab.Clear();

            if (ObjectDB.instance == null)
                return;

            foreach (KeyValuePair<string, List<TradeableItem>> list in sellableItems)
            {
                string suffix = "." + ItemsListType.Sell;
                if (!list.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string trader = list.Key.Substring(0, list.Key.Length - suffix.Length);
                foreach (TradeableItem item in list.Value)
                {
                    if (item == null || string.IsNullOrWhiteSpace(item.prefab) || item.price <= 0 || item.stack <= 0)
                        continue;

                    GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
                    ItemDrop itemDrop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    if (itemDrop == null)
                        continue;

                    string itemName = item.prefab;
                    if (!pricesByPrefab.TryGetValue(itemName, out List<PriceInfo> prices))
                    {
                        prices = new List<PriceInfo>();
                        pricesByPrefab.Add(itemName, prices);
                    }

                    prices.Add(new PriceInfo
                    {
                        Trader = trader,
                        Price = item.price,
                        Stack = item.stack,
                        Quality = item.quality,
                        CurrencyPrefab = item.currency,
                        Automatic = item.automatic,
                        Source = item
                    });
                }
            }
        }

        private static string GetTooltip(ItemDrop.ItemData itemData, int quality)
        {
            string prefabName = TradeInventory.PrefabName(itemData);
            if (string.IsNullOrEmpty(prefabName) && itemData != null && ObjectDB.instance != null)
                prefabName = ObjectDB.instance.m_items.FirstOrDefault(prefab => prefab != null &&
                    prefab.TryGetComponent(out ItemDrop drop) && ReferenceEquals(drop.m_itemData, itemData))?.name;
            if (string.IsNullOrEmpty(prefabName) || !pricesByPrefab.TryGetValue(prefabName, out List<PriceInfo> allPrices))
                return string.Empty;

            List<PriceInfo> commonExplicitSource = allPrices
                .Where(price => string.Equals(price.Trader, "common", StringComparison.OrdinalIgnoreCase) && !price.Automatic)
                .ToList();
            List<PriceInfo> commonAutomaticSource = allPrices
                .Where(price => string.Equals(price.Trader, "common", StringComparison.OrdinalIgnoreCase) && price.Automatic)
                .ToList();
            Dictionary<string, List<PriceInfo>> specificPrices = allPrices
                .Where(price => !string.Equals(price.Trader, "common", StringComparison.OrdinalIgnoreCase))
                .GroupBy(price => TraderName(price.Trader), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

            HashSet<string> traderNames = new HashSet<string>(TraderConfigManager.GetKnownTraderNames(), StringComparer.OrdinalIgnoreCase);
            traderNames.UnionWith(specificPrices.Keys);
            KeepDiscoveredTraders(traderNames);
            if (traderNames.Count == 0)
                return string.Empty;

            bool automaticIsCommon = traderNames.Count > 0 &&
                                     traderNames.All(name => TraderConfigManager.Get(name).AddCommonValuableItemsToSellList);
            IEnumerable<PriceInfo> commonDisplaySource = automaticIsCommon
                ? commonExplicitSource.Concat(commonAutomaticSource)
                : commonExplicitSource;
            List<PriceInfo> commonPrices = GetEffectivePrices(commonDisplaySource, quality);

            StringBuilder result = new StringBuilder();
            result.Append("\n\n<color=#ffcc66>")
                .Append(Localization.instance?.Localize("$item_value") ?? "$item_value").Append("</color>");
            bool hasPrices = commonPrices.Count > 0;
            if (hasPrices)
            {
                hasPrices = true;
                AppendPriceLine(result, string.Empty, commonPrices, string.Empty);
            }

            foreach (string traderName in traderNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                specificPrices.TryGetValue(traderName, out List<PriceInfo> traderSpecificSource);

                IEnumerable<PriceInfo> effectiveSource = commonExplicitSource;
                if (TraderConfigManager.Get(traderName).AddCommonValuableItemsToSellList)
                    effectiveSource = effectiveSource.Concat(commonAutomaticSource);
                if (traderSpecificSource != null)
                    effectiveSource = effectiveSource.Concat(traderSpecificSource);

                List<PriceInfo> effectivePrices = GetEffectivePrices(effectiveSource, quality)
                    .Where(price => !commonPrices.Any(common => SamePrice(common, price, traderName)))
                    .ToList();
                if (effectivePrices.Count == 0)
                    continue;

                hasPrices = true;
                string label = LocalizeTraderName(traderName);
                string currency = TraderCurrency.GetConfiguredCurrencyName(traderName);
                AppendPriceLine(result, label, effectivePrices, currency);
            }

            return hasPrices ? result.ToString() : string.Empty;
        }

        private static void KeepDiscoveredTraders(HashSet<string> traderNames)
        {
            if (Player.m_localPlayer == null || ZoneSystem.instance == null || ZNet.instance == null)
            {
                traderNames.Clear();
                return;
            }

            // This is the live icon list consumed by Minimap, not the catalogue of possible icons.
            // GetLocationIcons uses the received icons on clients and the same visible set on hosts.
            Dictionary<Vector3, string> icons = new Dictionary<Vector3, string>();
            ZoneSystem.instance.GetLocationIcons(icons);
            HashSet<string> locations = new HashSet<string>(icons.Values, StringComparer.OrdinalIgnoreCase);
            traderNames.RemoveWhere(name => !HasTraderLocationIcon(TraderName(name), locations));
        }

        private static bool HasTraderLocationIcon(string trader, HashSet<string> locations)
        {
            switch (trader)
            {
                case "haldor": return locations.Contains("Vendor_BlackForest");
                case "hildir": return locations.Contains("Hildir_camp");
                case "bogwitch": return locations.Contains("BogWitch_Camp");
                default: return locations.Contains(trader) || locations.Contains(trader + "_camp");
            }
        }

        private static bool SamePrice(PriceInfo left, PriceInfo right, string trader)
        {
            if (left.Price != right.Price || left.Stack != right.Stack)
                return false;

            string traderCurrency = TraderCurrency.GetTraderCurrencyPrefabName(trader);
            string leftCurrency = string.IsNullOrWhiteSpace(left.CurrencyPrefab) ? traderCurrency : left.CurrencyPrefab.Trim();
            string rightCurrency = string.IsNullOrWhiteSpace(right.CurrencyPrefab) ? traderCurrency : right.CurrencyPrefab.Trim();
            return string.Equals(leftCurrency, rightCurrency, StringComparison.OrdinalIgnoreCase);
        }

        private static List<PriceInfo> GetEffectivePrices(IEnumerable<PriceInfo> prices, int quality)
        {
            List<PriceInfo> applicablePrices = prices
                .Where(price => price.Source == null || price.Source.RequirementsMet())
                .ToList();
            bool hasQualitySpecificPrices = applicablePrices.Any(price => price.Quality > 0 && price.Quality == quality);

            Dictionary<int, PriceInfo> pricesByStack = new Dictionary<int, PriceInfo>();
            foreach (PriceInfo price in applicablePrices)
            {
                if (hasQualitySpecificPrices ? price.Quality != quality : price.Quality > 0)
                    continue;

                pricesByStack[price.Stack] = price;
            }

            return pricesByStack.Values
                .OrderBy(price => price.Stack)
                .ThenBy(price => price.Price)
                .ToList();
        }

        private static void AppendPriceLine(StringBuilder result, string label, List<PriceInfo> prices, string currency)
        {
            if (prices == null || prices.Count == 0)
                return;

            result.Append('\n');

            if (!string.IsNullOrWhiteSpace(label))
                result.Append(label).Append(": ");

            AppendPrices(result, prices, currency);
        }

        private static void AppendPrices(StringBuilder result, List<PriceInfo> prices, string currency)
        {
            for (int i = 0; i < prices.Count; i++)
            {
                if (i > 0)
                    result.Append(", ");

                PriceInfo price = prices[i];
                result.Append(price.Price);
                string effectiveCurrency = currency;
                if (!string.IsNullOrWhiteSpace(price.CurrencyPrefab))
                {
                    string configuredCurrency = TraderCurrency.GetCurrencyName(price.CurrencyPrefab);
                    if (!string.IsNullOrEmpty(configuredCurrency))
                        effectiveCurrency = configuredCurrency;
                }
                if (!string.IsNullOrEmpty(effectiveCurrency) && effectiveCurrency != CoinsPatches.itemDropNameCoins)
                    result.Append(' ').Append(effectiveCurrency);
                if (price.Stack > 1)
                    result.Append(" / x").Append(price.Stack);
                if (price.Quality > 0)
                    result.Append(" (").Append(Localization.instance?.Localize("$item_quality") ?? "$item_quality")
                        .Append(' ').Append(price.Quality).Append(')');
            }
        }

        private static string LocalizeTraderName(string trader)
        {
            if (Localization.instance == null)
                return trader;

            string localized = Localization.instance.Localize("$npc_" + trader);
            return localized == "$npc_" + trader ? trader : localized;
        }

        [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.Start))]
        private static class ZoneSystem_Awake_RebuildTooltipPrices
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix() => Rebuild();
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
        private static class ItemData_GetTooltip_AddTraderPrices
        {
            private struct ItemValueState
            {
                internal ItemDrop.ItemData.SharedData SharedData;
                internal int Value;
            }

            [HarmonyPriority(Priority.Last)]
            private static void Prefix(ItemDrop.ItemData __0, out ItemValueState __state)
            {
                __state = default;
                if (hideVanillaItemValue?.Value != true || __0?.m_shared == null)
                    return;

                // Retain the exact shared object and value for this call, including nested tooltip calls.
                __state.SharedData = __0.m_shared;
                __state.Value = __state.SharedData.m_value;
                __state.SharedData.m_value = 0;
            }

            private static void Finalizer(ref ItemValueState __state)
            {
                if (__state.SharedData == null)
                    return;

                // Restore even after an exception or a setting change. Do not suppress the exception.
                __state.SharedData.m_value = __state.Value;
                __state = default;
            }

            private static void Postfix(ItemDrop.ItemData __0, int __1, ref string __result)
            {
                string prices = GetTooltip(__0, __1);
                if (!string.IsNullOrEmpty(prices))
                    __result += prices;
            }
        }
    }
}
