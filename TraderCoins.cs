using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static class TraderCoins
    {
        internal enum BalanceOperation
        {
            Spend,
            Receive
        }

        private static Trader currentTrader;
        private static ZNetView traderNetView;
        private static int spendTransactionDepth;
        private static int receiveTransactionDepth;

        private sealed class BalanceTransaction : IDisposable
        {
            private readonly BalanceOperation operation;
            private bool disposed;

            internal BalanceTransaction(BalanceOperation operation)
            {
                this.operation = operation;
                if (operation == BalanceOperation.Spend)
                    spendTransactionDepth++;
                else
                    receiveTransactionDepth++;
            }

            public void Dispose()
            {
                if (disposed)
                    return;

                disposed = true;
                if (operation == BalanceOperation.Spend)
                    spendTransactionDepth = Math.Max(spendTransactionDepth - 1, 0);
                else
                    receiveTransactionDepth = Math.Max(receiveTransactionDepth - 1, 0);
            }
        }

        public static TMP_Text playerCoins;
        public static TMP_Text traderCoins;
        public static GameObject traderCoinsPanel;

        private static readonly int s_traderCoins = "traderCoins".GetStableHashCode();
        private static readonly int s_traderCoinsReplenished = "traderCoinsReplenished".GetStableHashCode();

        internal static IDisposable BeginBalanceTransaction(BalanceOperation operation)
        {
            return new BalanceTransaction(operation);
        }

        public static List<string> GetTraderPrefabs()
        {
            HashSet<string> traders = new HashSet<string>(new[] { "Haldor", "Hildir", "BogWitch" }, StringComparer.OrdinalIgnoreCase);
            traders.UnionWith((tradersCustomPrefabs?.Value ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value)));

            traders.UnionWith(TraderConfigManager.GetKnownTraderNames());
            return traders.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void ResetCurrentTrader(Trader trader)
        {
            currentTrader = trader;
            traderNetView = trader != null ? trader.GetComponent<ZNetView>() : null;
        }

        public static void SetTraderCoins(string traderName, int newAmount)
        {
            if (!EnvMan.instance || !ZNetScene.instance || ZDOMan.instance == null || string.IsNullOrWhiteSpace(traderName))
                return;

            string normalizedTraderName = TraderName(traderName);
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(zdo => zdo != null))
            {
                string prefabName = ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name;
                if (!string.Equals(TraderName(prefabName), normalizedTraderName, StringComparison.OrdinalIgnoreCase))
                    continue;

                SetTraderCoins(zdo, prefabName, newAmount);
            }
        }

        public static void SetTraderCoins(int prefab, int newAmount)
        {
            if (!EnvMan.instance || !ZNetScene.instance || ZDOMan.instance == null)
                return;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(zdo => zdo != null && prefab == zdo.GetPrefab()))
                SetTraderCoins(zdo, ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name, newAmount);
        }

        private static void SetTraderCoins(ZDO zdo, string prefabName, int newAmount)
        {
            int current = zdo.GetInt(s_traderCoins);
            int amount = Math.Max(newAmount, 0);
            zdo.Set(s_traderCoins, amount);
            zdo.Set(s_traderCoinsReplenished, EnvMan.instance.GetCurrentDay());
            LogInfo($"{prefabName} balance updated {current} -> {amount}");
        }

        public static bool UpdateTradersCoinsDaily()
        {
            if (!EnvMan.instance || !ZNetScene.instance || ZDOMan.instance == null)
                return false;

            HashSet<string> traderNames = new HashSet<string>(
                TraderConfigManager.GetKnownTraderNames(),
                StringComparer.OrdinalIgnoreCase);
            bool sendMessage = false;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(zdo => zdo != null))
            {
                string prefabName = ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name;
                if (string.IsNullOrWhiteSpace(prefabName) || !traderNames.Contains(TraderName(prefabName)))
                    continue;

                ResolvedTraderConfig config = TraderConfigManager.Get(prefabName);
                if (!config.TradersUseCoins)
                    continue;

                int coinsReplenished = zdo.GetInt(s_traderCoinsReplenished);
                if (EnvMan.instance.GetCurrentDay() - coinsReplenished < Math.Max(config.TraderCoinsReplenishmentRateInDays, 1))
                    continue;

                int minimum = Math.Max(config.CoinsAfterReplenishmentMinimum, 0);
                int maximum = Math.Max(config.CoinsAfterReplenishmentMaximum, minimum);
                int currentAmount = zdo.GetInt(s_traderCoins, minimum);
                if (currentAmount >= maximum && config.CoinsRemovedDaily <= 0)
                    continue;

                int newAmount = currentAmount >= maximum
                    ? Math.Max(maximum, currentAmount - Math.Max(config.CoinsRemovedDaily, 0))
                    : Mathf.Clamp(currentAmount + config.CoinsReplenishedDaily, minimum, maximum);

                zdo.Set(s_traderCoins, newAmount);
                zdo.Set(s_traderCoinsReplenished, EnvMan.instance.GetCurrentDay());
                sendMessage |= config.SendReplenishmentMessageInTheMorning;
                LogInfo($"{prefabName} balance updated {currentAmount} -> {newAmount}");
            }

            return sendMessage;
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateTriggers))]
        public static class EnvMan_UpdateTriggers_TraderCoinsUpdate
        {
            private static bool IsMorning(float oldDayFraction, float newDayFraction) =>
                oldDayFraction > 0.2f && oldDayFraction < 0.25f && newDayFraction >= 0.25f && newDayFraction < 0.3f;

            private static void Postfix(float oldDayFraction, float newDayFraction)
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer() || !IsMorning(oldDayFraction, newDayFraction))
                    return;

                if (UpdateTradersCoinsDaily())
                    MessageHud.instance?.MessageAll(MessageHud.MessageType.TopLeft, "$store_topic: $msg_added");
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Update))]
        public static class StoreGui_Update_PlayerTraderCoinsUpdate
        {
            private static void Postfix(StoreGui __instance)
            {
                if (!StorePanel.IsOpen())
                    return;

                playerCoins?.SetText(__instance.GetPlayerCoins().ToString());

                if (GetCurrentConfig().TradersUseCoins)
                    traderCoins?.SetText(GetTraderCoins().ToString());

                if (ZInput.GamepadActive)
                    StorePanel.UpdateNames();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new Type[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
        public static class Inventory_RemoveItem_TraderCoinsUpdate
        {
            private static void Prefix(Inventory __instance, string name, ref int __state)
            {
                __state = spendTransactionDepth > 0 && IsActiveCurrencySharedName(name) && IsLocalPlayerInventory(__instance)
                    ? __instance.CountItems(name)
                    : -1;
            }

            private static void Postfix(Inventory __instance, string name, int __state)
            {
                if (__state < 0)
                    return;

                int removed = __state - __instance.CountItems(name);
                if (removed > 0)
                    UpdateTraderCoins(removed);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(bool) })]
        public static class Inventory_AddItem_String_TraderCoinsUpdate
        {
            private static void Prefix(Inventory __instance, string name, ref int __state)
            {
                ItemDrop currency = GetActiveCurrency();
                __state = receiveTransactionDepth > 0 && currency != null && IsActiveCurrencyPrefabName(name) && IsLocalPlayerInventory(__instance)
                    ? __instance.CountItems(currency.m_itemData.m_shared.m_name)
                    : -1;
            }

            private static void Postfix(Inventory __instance, int __state)
            {
                if (__state < 0)
                    return;

                ItemDrop currency = GetActiveCurrency();
                if (currency == null)
                    return;

                int added = __instance.CountItems(currency.m_itemData.m_shared.m_name) - __state;
                if (added > 0)
                    UpdateTraderCoins(-added);
            }
        }

        private static bool IsLocalPlayerInventory(Inventory inventory)
        {
            return StorePanel.IsOpen() && Player.m_localPlayer != null && Player.m_localPlayer.GetInventory() == inventory;
        }

        private static ItemDrop GetActiveCurrency()
        {
            return StoreGui.instance != null ? StoreGui.instance.m_coinPrefab : null;
        }

        private static bool IsActiveCurrencySharedName(string name)
        {
            ItemDrop currency = GetActiveCurrency();
            return currency != null && string.Equals(name, currency.m_itemData.m_shared.m_name, StringComparison.Ordinal);
        }

        private static bool IsActiveCurrencyPrefabName(string name)
        {
            ItemDrop currency = GetActiveCurrency();
            return currency != null && string.Equals(name, Utils.GetPrefabName(currency.gameObject), StringComparison.Ordinal);
        }

        public static bool CanSell(int price)
        {
            return !GetCurrentConfig().TradersUseCoins || price <= GetTraderCoins();
        }

        public static void UpdateTraderCoins(int amountToAdd = 0)
        {
            if (!GetCurrentConfig().TradersUseCoins)
                return;

            ZNetView netView = GetTraderNetView();
            if (netView == null || !netView.IsValid())
                return;

            netView.GetZDO().Set(s_traderCoins, Math.Max(GetTraderCoins() + amountToAdd, 0));

            if (StorePanel.IsOpen())
                StorePanel.UpdateNames();
        }

        public static float GetPriceFactor(bool buyPrice)
        {
            int coins = GetTraderCoins();
            return buyPrice ? GetTraderBuyPriceFactor(coins) : GetTraderSellPriceFactor(coins);
        }

        public static void UpdateTraderCoinsVisibility()
        {
            traderCoinsPanel?.SetActive(GetCurrentConfig().TradersUseCoins);
        }

        public static int GetTraderCoins()
        {
            ZNetView netView = GetTraderNetView();
            int minimum = Math.Max(GetCurrentConfig().CoinsAfterReplenishmentMinimum, 0);
            if (netView == null || !netView.IsValid())
                return minimum;

            return netView.GetZDO().GetInt(s_traderCoins, minimum);
        }

        private static ZNetView GetTraderNetView()
        {
            Trader trader = currentTrader != null ? currentTrader : StoreGui.instance?.m_trader;
            if (trader == null)
                return null;

            if (traderNetView == null || traderNetView.gameObject != trader.gameObject)
            {
                currentTrader = trader;
                traderNetView = trader.GetComponent<ZNetView>();
            }

            return traderNetView;
        }

        private static float GetTraderBuyPriceFactor(int coins)
        {
            ResolvedTraderConfig config = GetCurrentConfig();
            if (!config.TradersUseFlexiblePricing)
                return 1f;

            GetPriceRange(config, out int minimum, out int maximum);
            if (coins < minimum)
            {
                float progress = minimum > 0 ? Mathf.Clamp01((float)coins / minimum) : 1f;
                return RoundFactorToPercent(Mathf.Lerp(config.TraderMarkup, 1f, progress));
            }

            float upperProgress = Mathf.Clamp01((float)(coins - minimum) / Math.Max(maximum - minimum, 1));
            return RoundFactorToPercent(Mathf.Lerp(1f, config.TraderDiscount, upperProgress));
        }

        private static float GetTraderSellPriceFactor(int coins)
        {
            ResolvedTraderConfig config = GetCurrentConfig();
            if (!config.TradersUseFlexiblePricing)
                return 1f;

            GetPriceRange(config, out int minimum, out int maximum);
            if (coins < minimum)
            {
                float progress = minimum > 0 ? Mathf.Clamp01((float)coins / minimum) : 1f;
                return RoundFactorToPercent(Mathf.Lerp(config.TraderDiscount, 1f, progress));
            }

            float upperProgress = Mathf.Clamp01((float)(coins - minimum) / Math.Max(maximum - minimum, 1));
            return RoundFactorToPercent(Mathf.Lerp(1f, config.TraderMarkup, upperProgress));
        }

        private static void GetPriceRange(ResolvedTraderConfig config, out int minimum, out int maximum)
        {
            minimum = Math.Max(config.CoinsAfterReplenishmentMinimum, 0);
            maximum = Math.Max(config.CoinsAfterReplenishmentMaximum, minimum + 1);
        }

        private static ResolvedTraderConfig GetCurrentConfig()
        {
            Trader trader = currentTrader != null ? currentTrader : StoreGui.instance?.m_trader;
            return TraderConfigManager.Get(trader);
        }

        private static float RoundFactorToPercent(float factor)
        {
            return Mathf.Round(factor * 100f) / 100f;
        }
    }
}
