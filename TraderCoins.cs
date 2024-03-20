﻿using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
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

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.OnMorning))]
        public static class EnvMan_OnMorning_TraderCoinsUpdate
        {
            private static void Postfix(EnvMan __instance)
            {
                if (!traderUseCoins.Value)
                    return;

                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "$store_topic. $msg_added: $item_coins");

                if (!ZNet.instance.IsServer())
                    return;

                HashSet<int> traderPrefabs = new HashSet<int>(tradersCustomPrefabs.Value.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(selector => selector.GetStableHashCode()).ToList())
                {
                    "Haldor".GetStableHashCode(),
                    "Hildir".GetStableHashCode()
                };

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

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.BuySelectedItem))]
        public static class StoreGui_BuySelectedItem_TraderCoinsUpdate
        {
            public static bool isCalled = false;

            public static bool Prefix()
            {
                if (!modEnabled.Value)
                    return true;

                if (AmountDialog.IsOpen())
                    return false;

                isCalled = true;
                return true;
            }

            public static void Postfix() => isCalled = false;
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem), new Type[] { typeof(string), typeof(int), typeof(int), typeof(bool) })]
        public static class Inventory_RemoveItem_TraderCoinsUpdate
        {
            public static void Prefix(int amount, ref int __state)
            {
                __state = amount;
            }

            public static void Postfix(Inventory __instance, string name, int amount, int __state)
            {
                if (!modEnabled.Value)
                    return;

                int amountSpent = __state - Math.Max(amount, 0);

                if (name != CoinsPatches.itemDropNameCoins || amountSpent == 0)
                    return;

                if (!StorePanel.IsOpen())
                    return;

                if (Player.m_localPlayer == null || Player.m_localPlayer.GetInventory() != __instance)
                    return;

                UpdateTraderCoins(amountSpent);
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

        [HarmonyPatch(typeof(Character), nameof(Character.ShowPickupMessage))]
        public static class Character_ShowPickupMessage_FixIncorrectStackMessage
        {
            private static void Prefix(ItemDrop.ItemData item, ref int amount)
            {
                if (!modEnabled.Value)
                    return;

                if (StoreGui.instance != null && StoreGui_BuySelectedItem_TraderCoinsUpdate.isCalled && StoreGui.instance.m_selectedItem != null && item == StoreGui.instance.m_selectedItem.m_prefab?.m_itemData)
                    amount = StoreGui.instance.m_selectedItem.m_stack;
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

        private static int GetTraderCoins()
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
