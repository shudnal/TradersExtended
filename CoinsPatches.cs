using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal class CoinsPatches
    {
        private const string itemNameCoins = "Coins";
        private const string itemDropNameCoins = "$item_coins";

        private static List<ItemDrop.ItemData> _itemDataList = new List<ItemDrop.ItemData>();

        public static void PatchCoinsItemData(ItemDrop.ItemData coins)
        {
            if (coins == null)
                return;

            if (!coinsPatch.Value)
                return;

            coins.m_shared.m_weight = coinsWeight.Value;
            coins.m_shared.m_maxStackSize = coinsStackSize.Value;
        }

        public static void PatchCoinsInInventory(Inventory inventory)
        {
            if (inventory == null)
                return;

            _itemDataList.Clear();
            inventory.GetAllItems(itemDropNameCoins, _itemDataList);

            foreach (ItemDrop.ItemData item in _itemDataList)
                PatchCoinsItemData(item);
        }

        public static void UpdateCoinsPrefab()
        {
            GameObject prefabCoins = ObjectDB.instance.GetItemPrefab(itemNameCoins);
            if (prefabCoins != null)
                PatchCoinsItemData(prefabCoins.GetComponent<ItemDrop>()?.m_itemData);

            PatchCoinsInInventory(Player.m_localPlayer?.GetInventory());
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
        public static class ObjectDB_Awake_CoinsPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                UpdateCoinsPrefab();
            }
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        public static class ObjectDB_CopyOtherDB_CoinsPatch
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix()
            {
                UpdateCoinsPrefab();
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.AddKnownItem))]
        public static class Player_AddKnownItem_CoinsPatch
        {
            private static void Postfix(ref ItemDrop.ItemData item)
            {
                if (item.m_shared.m_name != itemDropNameCoins)
                    return;

                PatchCoinsItemData(item);
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
        public class Player_OnSpawned_CoinsPatch
        {
            public static void Postfix(Player __instance)
            {
                if (__instance != Player.m_localPlayer)
                    return;

                PatchCoinsInInventory(__instance.GetInventory());
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.Load))]
        public class Inventory_Load_CoinsPatch
        {
            public static void Postfix(Inventory __instance)
            {
                PatchCoinsInInventory(__instance);
            }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Start))]
        public static class ItemDrop_Start_CoinsPatch
        {
            private static void Postfix(ItemDrop __instance)
            {
                if (__instance.GetPrefabName(__instance.name) != itemNameCoins)
                    return;

                PatchCoinsItemData(__instance.m_itemData);
            }
        }


    }
}
