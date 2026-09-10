using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static class CoinsPatches
    {
        public const string itemNameCoins = "Coins";
        public const string itemDropNameCoins = "$item_coins";

        private sealed class OriginalProperties
        {
            internal float Weight;
            internal int MaximumStack;
            internal float AppliedWeight;
            internal int AppliedMaximumStack;
        }

        private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, OriginalProperties> originals =
            new ConditionalWeakTable<ItemDrop.ItemData.SharedData, OriginalProperties>();
        private static readonly List<WeakReference<ItemDrop.ItemData.SharedData>> trackedData =
            new List<WeakReference<ItemDrop.ItemData.SharedData>>();

        private static bool IsCoins(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
                return false;
            if (item.m_dropPrefab != null)
                return Utils.GetPrefabName(item.m_dropPrefab) == itemNameCoins;
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemNameCoins) : null;
            return prefab != null && ReferenceEquals(item.m_shared, prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared);
        }

        public static void PatchCoinsItemData(ItemDrop.ItemData coins)
        {
            if (IsCoins(coins))
                ApplyProperties(coins.m_shared);
        }

        private static void ApplyProperties(ItemDrop.ItemData.SharedData shared)
        {
            if (coinsPatch?.Value != true)
            {
                RestoreProperties(shared);
                return;
            }
            if (!originals.TryGetValue(shared, out OriginalProperties original))
            {
                original = new OriginalProperties { Weight = shared.m_weight, MaximumStack = shared.m_maxStackSize };
                originals.Add(shared, original);
                trackedData.Add(new WeakReference<ItemDrop.ItemData.SharedData>(shared));
            }
            float weight = coinsWeight.Value;
            original.AppliedWeight = float.IsNaN(weight) || float.IsInfinity(weight) ? original.Weight : Math.Max(weight, 0f);
            original.AppliedMaximumStack = Math.Max(coinsStackSize.Value, 1);
            shared.m_weight = original.AppliedWeight;
            shared.m_maxStackSize = original.AppliedMaximumStack;
        }

        private static void RestoreProperties(ItemDrop.ItemData.SharedData shared)
        {
            if (!originals.TryGetValue(shared, out OriginalProperties original))
                return;
            if (shared.m_weight == original.AppliedWeight)
                shared.m_weight = original.Weight;
            if (shared.m_maxStackSize == original.AppliedMaximumStack)
                shared.m_maxStackSize = original.MaximumStack;
            originals.Remove(shared);
        }

        internal static void RestoreAll()
        {
            foreach (WeakReference<ItemDrop.ItemData.SharedData> reference in trackedData)
                if (reference.TryGetTarget(out ItemDrop.ItemData.SharedData shared))
                    RestoreProperties(shared);
            trackedData.Clear();
            Player.m_localPlayer?.GetInventory()?.UpdateTotalWeight();
        }

        public static void PatchCoinsInInventory(Inventory inventory)
        {
            // Temporary migration inventories contain incomplete item data without SharedData.
            if (inventory == null || inventory.m_temoraryInventory)
                return;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                PatchCoinsItemData(item);
            inventory.UpdateTotalWeight();
        }

        public static void UpdateCoinsPrefab()
        {
            for (int index = trackedData.Count - 1; index >= 0; index--)
            {
                if (!trackedData[index].TryGetTarget(out ItemDrop.ItemData.SharedData shared))
                {
                    trackedData.RemoveAt(index);
                    continue;
                }
                ApplyProperties(shared);
                if (!originals.TryGetValue(shared, out _))
                    trackedData.RemoveAt(index);
            }
            GameObject prefabCoins = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemNameCoins) : null;
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
                if (!IsCoins(item))
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

        [HarmonyPatch]
        public class Inventory_Load_CoinsPatch
        {
            private static IEnumerable<MethodBase> TargetMethods()
            {
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.Load), new[] { typeof(ZPackage) });
                yield return AccessTools.Method(typeof(Inventory), nameof(Inventory.Load), new[] { typeof(ZPackage), typeof(bool) });
            }

            public static void Postfix(Inventory __instance)
            {
                PatchCoinsInInventory(__instance);
            }
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool))]
        private static class Inventory_AddItem_ItemData_amount_x_y_PatchCoinsItemDataOnLoad
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(ItemDrop.ItemData item)
            {
                if (!IsCoins(item))
                    return;

                PatchCoinsItemData(item);
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
