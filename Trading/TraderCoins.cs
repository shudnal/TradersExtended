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
        private static Trader currentTrader;
        private static ZNetView traderNetView;

        public static TMP_Text playerCoins;
        public static TMP_Text traderCoins;
        public static GameObject traderCoinsPanel;

        private static readonly int s_traderCoins = "traderCoins".GetStableHashCode();
        private static readonly int s_traderCoinsReplenished = "traderCoinsReplenished".GetStableHashCode();

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
                    : Math.Max(minimum, Math.Min(maximum, TradeAmounts.ClampBalance((long)currentAmount + config.CoinsReplenishedDaily)));

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

        public static bool CanSell(int price)
        {
            return price > 0 && (!GetCurrentConfig().TradersUseCoins || price <= GetTraderCoins());
        }

        public static void UpdateTraderCoins(int amountToAdd = 0)
        {
            if (!GetCurrentConfig().TradersUseCoins)
                return;

            ZNetView netView = GetTraderNetView();
            if (netView == null || !netView.IsValid())
                return;

            netView.GetZDO().Set(s_traderCoins, TradeAmounts.ClampBalance((long)GetTraderCoins() + amountToAdd));

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

            return Math.Max(netView.GetZDO().GetInt(s_traderCoins, minimum), 0);
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
            maximum = Math.Max(config.CoinsAfterReplenishmentMaximum, minimum);
        }

        private static ResolvedTraderConfig GetCurrentConfig()
        {
            Trader trader = currentTrader != null ? currentTrader : StoreGui.instance?.m_trader;
            return TraderConfigManager.Get(trader);
        }

        private static float RoundFactorToPercent(float factor)
        {
            if (float.IsNaN(factor) || float.IsInfinity(factor) || factor < 0f)
                return 1f;
            return (float)(Math.Round(factor * 100d) / 100d);
        }
    }
}
