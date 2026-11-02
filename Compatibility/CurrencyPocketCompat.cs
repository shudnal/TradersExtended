using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Globalization;
using UnityEngine;

namespace TradersExtended.Compatibility
{
    // CurrencyPocket stores vanilla coins in player custom data instead of an inventory stack.
    internal static class CurrencyPocketCompat
    {
        internal const string PlayerDataKey = "CoinPocket_CoinCount";
        private const string PluginGuid = "Azumatt.CurrencyPocket";
        private static readonly bool installed = Chainloader.PluginInfos.ContainsKey(PluginGuid);

        internal static bool IsInstalled => installed;

        internal static bool IsCoinCurrency(ItemDrop currency) =>
            currency != null && string.Equals(Utils.GetPrefabName(currency.gameObject),
                CoinsPatches.itemNameCoins, StringComparison.Ordinal);

        internal static int GetStoredCoins(ItemDrop currency) => installed && IsCoinCurrency(currency) ? GetStoredCoins() : 0;

        internal static int GetStoredCoins()
        {
            Player player = Player.m_localPlayer;
            if (!installed || player == null || !player.m_customData.TryGetValue(PlayerDataKey, out string stored) ||
                !int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out int coins))
                return 0;

            return Math.Max(coins, 0);
        }

        internal static bool TrySpend(ItemDrop currency, int amount)
        {
            if (amount == 0)
                return true;

            Player player = Player.m_localPlayer;
            if (amount < 0 || player == null || !installed || !IsCoinCurrency(currency))
                return false;

            int balance = GetStoredCoins();
            if (balance < amount)
                return false;

            player.m_customData[PlayerDataKey] = (balance - amount).ToString(CultureInfo.InvariantCulture);
            StorePanel.InvalidateInventoryAvailability();
            return true;
        }

        // CurrencyPocket adds its coins to every StoreGui.GetPlayerCoins call, even when a trader
        // uses a different currency. Keep the native UI balance correct for those traders.
        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.GetPlayerCoins))]
        private static class StoreGui_GetPlayerCoins_CurrencyPocket
        {
            [HarmonyPostfix]
            [HarmonyAfter(PluginGuid)]
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(StoreGui __instance, ref int __result)
            {
                if (installed && __instance != null && __instance.m_coinPrefab != null &&
                    !IsCoinCurrency(__instance.m_coinPrefab))
                    __result = Math.Max(0, __result - GetStoredCoins());
            }
        }
    }
}
