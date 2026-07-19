using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static class TraderCurrency
    {
        private sealed class CurrencyReference
        {
            internal readonly string PrefabName;

            internal CurrencyReference(string prefabName)
            {
                PrefabName = prefabName;
            }
        }

        private static readonly ConditionalWeakTable<Trader.TradeItem, CurrencyReference> itemCurrencies = new ConditionalWeakTable<Trader.TradeItem, CurrencyReference>();
        private static readonly HashSet<string> invalidCurrenciesLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static ItemDrop vanillaCurrencyPrefab;

        internal static void CaptureVanillaCurrency(StoreGui storeGui)
        {
            if (vanillaCurrencyPrefab == null && storeGui != null && storeGui.m_coinPrefab != null)
                vanillaCurrencyPrefab = storeGui.m_coinPrefab;
        }

        internal static void RebuildOverrides()
        {
            if (StoreGui.instance != null)
            {
                ApplyTraderCurrency(StoreGui.instance);
                if (StoreGui.IsVisible() && StoreGui.instance.m_trader != null)
                    StoreGui.instance.FillList();
            }

            TooltipPrices.Rebuild();
        }

        internal static void ApplyTraderCurrency(StoreGui storeGui, Trader trader = null)
        {
            if (storeGui == null)
                return;

            CaptureVanillaCurrency(storeGui);

            ItemDrop currency = vanillaCurrencyPrefab;
            string configuredCurrency = GetTraderCurrencyPrefabName(trader ?? storeGui.m_trader);
            if (!string.IsNullOrEmpty(configuredCurrency))
                currency = ResolveCurrency(configuredCurrency) ?? vanillaCurrencyPrefab;

            if (currency != null)
                storeGui.m_coinPrefab = currency;

            StorePanel.UpdateCurrencyVisuals(storeGui);
        }

        internal static string GetTraderCurrencyPrefabName(Trader trader)
        {
            return trader == null ? string.Empty : TraderConfigManager.Get(trader).TraderCurrencyPrefab;
        }

        internal static string GetTraderCurrencyPrefabName(string traderName)
        {
            return TraderConfigManager.Get(traderName).TraderCurrencyPrefab;
        }

        internal static void RegisterCurrency(Trader.TradeItem tradeItem, string currencyPrefab, Trader trader)
        {
            if (tradeItem == null)
                return;

            string effectiveCurrency = string.IsNullOrWhiteSpace(currencyPrefab)
                ? GetTraderCurrencyPrefabName(trader)
                : currencyPrefab.Trim();

            itemCurrencies.Remove(tradeItem);
            if (string.IsNullOrEmpty(effectiveCurrency))
                return;

            itemCurrencies.Add(tradeItem, new CurrencyReference(effectiveCurrency));
        }

        internal static void RegisterCurrency(Trader.TradeItem tradeItem, ItemDrop currency, Trader trader)
        {
            RegisterCurrency(tradeItem, currency != null ? Utils.GetPrefabName(currency.gameObject) : string.Empty, trader);
        }

        internal static void CopyCurrency(Trader.TradeItem source, Trader.TradeItem destination)
        {
            if (source == null || destination == null || !itemCurrencies.TryGetValue(source, out CurrencyReference currency))
                return;

            itemCurrencies.Remove(destination);
            itemCurrencies.Add(destination, currency);
        }

        internal static ItemDrop GetCurrency(string currencyPrefab, StoreGui storeGui)
        {
            if (!string.IsNullOrWhiteSpace(currencyPrefab))
            {
                ItemDrop resolved = ResolveCurrency(currencyPrefab.Trim());
                if (resolved != null)
                    return resolved;
            }

            return storeGui != null ? storeGui.m_coinPrefab : vanillaCurrencyPrefab;
        }

        internal static ItemDrop GetCurrency(Trader.TradeItem tradeItem, StoreGui storeGui)
        {
            if (tradeItem != null && itemCurrencies.TryGetValue(tradeItem, out CurrencyReference currency))
            {
                ItemDrop resolved = ResolveCurrency(currency.PrefabName);
                if (resolved != null)
                    return resolved;
            }

            return storeGui != null ? storeGui.m_coinPrefab : vanillaCurrencyPrefab;
        }

        internal static int GetPlayerCurrencyAmount(Trader.TradeItem tradeItem, StoreGui storeGui)
        {
            if (Player.m_localPlayer == null)
                return 0;

            ItemDrop currency = GetCurrency(tradeItem, storeGui);
            return currency == null ? 0 : Player.m_localPlayer.GetInventory().CountItems(currency.m_itemData.m_shared.m_name);
        }

        internal static bool UsesStoreCurrency(Trader.TradeItem tradeItem, StoreGui storeGui)
        {
            ItemDrop currency = GetCurrency(tradeItem, storeGui);
            return currency == null || storeGui == null || storeGui.m_coinPrefab == null ||
                   currency.m_itemData.m_shared.m_name == storeGui.m_coinPrefab.m_itemData.m_shared.m_name;
        }

        internal static string GetCurrencyName(ItemDrop currency)
        {
            if (currency == null)
                return string.Empty;

            return Localization.instance != null
                ? Localization.instance.Localize(currency.m_itemData.m_shared.m_name)
                : currency.m_itemData.m_shared.m_name;
        }

        internal static string GetCurrencyName(Trader.TradeItem tradeItem, StoreGui storeGui)
        {
            return GetCurrencyName(GetCurrency(tradeItem, storeGui));
        }

        internal static string GetCurrencyName(string currencyPrefab)
        {
            return GetCurrencyName(ResolveCurrency(currencyPrefab));
        }

        internal static string GetConfiguredCurrencyName(string traderName)
        {
            string currencyPrefab = GetTraderCurrencyPrefabName(traderName);
            ItemDrop currency = string.IsNullOrEmpty(currencyPrefab)
                ? vanillaCurrencyPrefab
                : ResolveCurrency(currencyPrefab) ?? vanillaCurrencyPrefab;
            return GetCurrencyName(currency);
        }

        internal static ItemDrop GetVanillaCurrency()
        {
            return vanillaCurrencyPrefab;
        }

        private static ItemDrop ResolveCurrency(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || ObjectDB.instance == null)
                return null;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName.Trim());
            if (prefab != null && prefab.TryGetComponent(out ItemDrop itemDrop))
                return itemDrop;

            if (invalidCurrenciesLogged.Add(prefabName))
                LogWarning($"Currency item prefab '{prefabName}' was not found. The trader's default currency will be used.");

            return null;
        }
    }
}
