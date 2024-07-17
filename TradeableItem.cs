using UnityEngine;
using System;
using BepInEx;
using System.Linq;

namespace TradersExtended
{
    public partial class TradersExtended
    {
        [Serializable]
        public class TradeableItem
        {
            public const int qualityStackMultiplier = 1000000;

            public string prefab;
            public int stack = 1;
            public int price = 1;
            public int quality = 0;
            public string requiredGlobalKey = "";
            public string notRequiredGlobalKey = "";
            public string requiredPlayerKey = "";
            public string notRequiredPlayerKey = "";

            [NonSerialized]
            public ItemDrop itemDrop;

            public bool IsItemToSell()
            {
                if (!ZoneSystem.instance || !ObjectDB.instance || !Player.m_localPlayer)
                    return false;

                if (!string.IsNullOrEmpty(requiredGlobalKey) && requiredGlobalKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => !ZoneSystem.instance.GetGlobalKey(s)))
                    return false;

                if (!string.IsNullOrEmpty(notRequiredGlobalKey) && notRequiredGlobalKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => ZoneSystem.instance.GetGlobalKey(s)))
                    return false;

                if (!string.IsNullOrEmpty(requiredPlayerKey) && requiredPlayerKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => !Player.m_localPlayer.HaveUniqueKey(s)))
                    return false;

                if (!string.IsNullOrEmpty(notRequiredPlayerKey) && notRequiredPlayerKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => Player.m_localPlayer.HaveUniqueKey(s)))
                    return false;

                GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);

                if (itemPrefab == null || !itemPrefab.TryGetComponent(out itemDrop))
                    return false;

                return !checkForDiscovery.Value || IgnoreItemDiscovery(prefab.ToLower()) || Player.m_localPlayer.IsMaterialKnown(itemDrop.m_itemData.m_shared.m_name);
            }

            public Trader.TradeItem ToTradeItem()
            {
                if (itemDrop == null)
                {
                    GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);
                    itemDrop = itemPrefab.GetComponent<ItemDrop>();
                }

                return new Trader.TradeItem
                {
                    m_prefab = itemDrop,
                    m_price = price,
                    m_stack = GetStackFromStackQuality(stack, quality),
                    m_requiredGlobalKey = requiredGlobalKey
                };
            }

            public static void NormalizeStack(Trader.TradeItem item)
            {
                GetStackQualityFromStack(item.m_stack, out int stack, out int quality);
                item.m_stack = GetStackFromStackQuality(Mathf.Clamp(stack, 1, item.m_prefab.m_itemData.m_shared.m_maxStackSize), quality);
            }

            public static void GetStackQualityFromStack(int m_stack, out int stack, out int quality)
            {
                stack = m_stack % qualityStackMultiplier;
                quality = m_stack / qualityStackMultiplier;
            }

            public static int GetStackFromStackQuality(int stack, int quality)
            {
                return stack + qualityStackMultiplier * quality;
            }

            public static int GetStackFromStack(int m_stack)
            {
                return m_stack % qualityStackMultiplier;
            }
        }
    }
}
