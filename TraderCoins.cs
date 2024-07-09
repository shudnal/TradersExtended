using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static TradersExtended.TraderCoins;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal class TraderCoins
    {
        private static ZNetView traderNetView;

        public static TMP_Text playerCoins;
        public static TMP_Text traderCoins;

        public static GameObject traderCoinsPanel;

        private static readonly int s_traderCoins = "traderCoins".GetStableHashCode();
        private static readonly int s_traderCoinsReplenished = "traderCoinsReplenished".GetStableHashCode();

        public static List<string> GetTraderPrefabs()
        {
            return new List<string>()
            {
                "Haldor",
                "Hildir"
            }.Concat(tradersCustomPrefabs.Value.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p))).ToList();
        }

        public static void SetTraderCoins(int prefab, int newAmount)
        {
            if (!EnvMan.instance || !ZNetScene.instance || ZDOMan.instance == null)
                return;

            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(zdo => prefab == zdo.GetPrefab()))
            {
                int current = zdo.GetInt(s_traderCoins);
                zdo.Set(s_traderCoins, newAmount);
                zdo.Set(s_traderCoinsReplenished, EnvMan.instance.GetCurrentDay());
                LogInfo($"{ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name} coins updated {current} -> {newAmount}");
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.UpdateTriggers))]
        public static class EnvMan_UpdateTriggers_TraderCoinsUpdate
        {
            private static bool IsMorning(float oldDayFraction, float newDayFraction)
            {
                return oldDayFraction > 0.2f && oldDayFraction < 0.25f && newDayFraction > 0.25f && newDayFraction < 0.3f;
            }

            private static void Postfix(EnvMan __instance, float oldDayFraction, float newDayFraction)
            {
                if (!traderUseCoins.Value)
                    return;

                if (!IsMorning(oldDayFraction, newDayFraction))
                    return;

                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "$store_topic: $msg_added $item_coins");

                if (!ZNet.instance.IsServer())
                    return;

                HashSet<int> traderPrefabs = new HashSet<int>(GetTraderPrefabs().Select(selector => selector.GetStableHashCode()));

                foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(zdo => traderPrefabs.Contains(zdo.GetPrefab())))
                {
                    int coinsReplenished = zdo.GetInt(s_traderCoinsReplenished);
                    if (__instance.GetCurrentDay() - coinsReplenished < traderCoinsReplenishmentRate.Value)
                        continue;

                    int current = zdo.GetInt(s_traderCoins);
                    if (current >= traderCoinsMaximumAmount.Value)
                        continue;

                    int newAmount = Mathf.Clamp(current + traderCoinsIncreaseAmount.Value, traderCoinsMinimumAmount.Value, traderCoinsMaximumAmount.Value);
                    zdo.Set(s_traderCoins, newAmount);
                    zdo.Set(s_traderCoinsReplenished, __instance.GetCurrentDay());
                    LogInfo($"{ZNetScene.instance.GetPrefab(zdo.GetPrefab())?.name} coins updated {current} -> {newAmount}");
                }
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Update))]
        public static class StoreGui_Update_PlayerTraderCoinsUpdate
        {
            private static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (!StorePanel.IsOpen())
                    return;

                playerCoins.SetText(__instance.GetPlayerCoins().ToString());

                if (traderUseCoins.Value)
                    traderCoins.SetText(GetTraderCoins().ToString());

                if (ZInput.GamepadActive)
                    StorePanel.UpdateNames();
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new Type[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
        public static class Inventory_RemoveItem_TraderCoinsUpdate
        {
            public static void Prefix(int amount, ref int __state)
            {
                __state = amount;
            }

            public static void Postfix(Inventory __instance, string name, int __state)
            {
                if (!modEnabled.Value)
                    return;

                if (name != CoinsPatches.itemDropNameCoins || __state == 0)
                    return;

                if (!StorePanel.IsOpen())
                    return;

                if (Player.m_localPlayer == null || Player.m_localPlayer.GetInventory() != __instance)
                    return;

                UpdateTraderCoins(__state);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new Type[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(bool) })]
        public static class Inventory_AddItem_String_TraderCoinsUpdate
        {
            public static void Postfix(Inventory __instance, string name, int stack, ItemDrop.ItemData __result)
            {
                if (!modEnabled.Value)
                    return;

                if (__result == null)
                    return;

                if (name != CoinsPatches.itemNameCoins || stack == 0)
                    return;

                if (!StorePanel.IsOpen())
                    return;

                if (Player.m_localPlayer == null || Player.m_localPlayer.GetInventory() != __instance)
                    return;

                UpdateTraderCoins(-stack);
            }
        }

        public static bool CanSell(int price)
        {
            return !traderUseCoins.Value || price <= GetTraderCoins();
        }

        public static void UpdateTraderCoins(int amountToAdd = 0)
        {
            if (!traderUseCoins.Value)
                return;

            if (traderNetView == null)
                traderNetView = StoreGui.instance.m_trader?.GetComponent<ZNetView>();

            if (traderNetView == null || !traderNetView.IsValid())
                return;

            traderNetView.GetZDO().Set(s_traderCoins, Math.Max(GetTraderCoins() + amountToAdd, 0));

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
            traderCoinsPanel.SetActive(traderUseCoins.Value);
        }

        public static int GetTraderCoins()
        {
            if (traderNetView == null)
                traderNetView = StoreGui.instance.m_trader?.GetComponent<ZNetView>();

            if (traderNetView == null || !traderNetView.IsValid())
                return traderCoinsMinimumAmount.Value;

            return traderNetView.GetZDO().GetInt(s_traderCoins, traderCoinsMinimumAmount.Value);
        }

        private static float GetTraderBuyPriceFactor(int coins)
        {
            if (!traderUseFlexiblePricing.Value)
                return 1f;

            if (coins < traderCoinsMinimumAmount.Value)
                return RoundFactorToPercent(Mathf.Lerp(traderMarkup.Value, 1f, (float)coins / traderCoinsMinimumAmount.Value));

            return RoundFactorToPercent(Mathf.Lerp(1f, traderDiscount.Value, (float)(coins - traderCoinsMinimumAmount.Value) / (traderCoinsMaximumAmount.Value - traderCoinsMinimumAmount.Value)));
        }

        private static float GetTraderSellPriceFactor(int coins)
        {
            if (!traderUseFlexiblePricing.Value)
                return 1f;

            if (coins < traderCoinsMinimumAmount.Value)
                return RoundFactorToPercent(Mathf.Lerp(traderDiscount.Value, 1f, (float)coins / traderCoinsMinimumAmount.Value));

            return RoundFactorToPercent(Mathf.Lerp(1f, traderMarkup.Value, (float)(coins - traderCoinsMinimumAmount.Value) / (traderCoinsMaximumAmount.Value - traderCoinsMinimumAmount.Value)));
        }

        private static float RoundFactorToPercent(float factor)
        {
            return Mathf.Round(factor * 100f) / 100f;
        }

    }
}
