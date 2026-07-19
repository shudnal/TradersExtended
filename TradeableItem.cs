using BepInEx;
using Newtonsoft.Json;
using System;
using System.Linq;
using UnityEngine;
using YamlDotNet.Serialization;

namespace TradersExtended
{
    public partial class TradersExtended
    {
        [Serializable]
        public class TradeableItem
        {
            public const int qualityStackMultiplier = 1000000;

            public string prefab { get; set; }
            public int stack { get; set; } = 1;
            public int price { get; set; } = 1;
            public int quality { get; set; }
            public string currency { get; set; } = string.Empty;
            public string requiredGlobalKey { get; set; } = string.Empty;
            public string notRequiredGlobalKey { get; set; } = string.Empty;
            public string requiredPlayerKey { get; set; } = string.Empty;
            public string notRequiredPlayerKey { get; set; } = string.Empty;

            [NonSerialized]
            [JsonIgnore]
            [YamlIgnore]
            public ItemDrop itemDrop;

            [NonSerialized]
            [JsonIgnore]
            [YamlIgnore]
            public bool automatic;

            public bool RequirementsMet()
            {
                if (ZoneSystem.instance == null || Player.m_localPlayer == null)
                    return false;

                if (!string.IsNullOrEmpty(requiredGlobalKey) && requiredGlobalKey.Split(',').Select(value => value.Trim()).Where(value => !value.IsNullOrWhiteSpace()).Any(value => !ZoneSystem.instance.GetGlobalKey(value)))
                    return false;

                if (!string.IsNullOrEmpty(notRequiredGlobalKey) && notRequiredGlobalKey.Split(',').Select(value => value.Trim()).Where(value => !value.IsNullOrWhiteSpace()).Any(value => ZoneSystem.instance.GetGlobalKey(value)))
                    return false;

                if (!string.IsNullOrEmpty(requiredPlayerKey) && requiredPlayerKey.Split(',').Select(value => value.Trim()).Where(value => !value.IsNullOrWhiteSpace()).Any(value => !Player.m_localPlayer.HaveUniqueKey(value)))
                    return false;

                if (!string.IsNullOrEmpty(notRequiredPlayerKey) && notRequiredPlayerKey.Split(',').Select(value => value.Trim()).Where(value => !value.IsNullOrWhiteSpace()).Any(value => Player.m_localPlayer.HaveUniqueKey(value)))
                    return false;

                return true;
            }

            public bool IsItemToSell(Trader trader)
            {
                if (ObjectDB.instance == null || !RequirementsMet() || string.IsNullOrWhiteSpace(prefab) || stack <= 0 || price <= 0)
                    return false;

                GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);
                if (itemPrefab == null || !itemPrefab.TryGetComponent(out itemDrop))
                    return false;

                ResolvedTraderConfig config = TraderConfigManager.Get(trader);
                return !config.SellOnlyDiscoveredItems ||
                       TraderConfigManager.IgnoreItemDiscovery(trader, prefab) ||
                       Player.m_localPlayer.IsMaterialKnown(itemDrop.m_itemData.m_shared.m_name);
            }

            public Trader.TradeItem ToTradeItem()
            {
                if (itemDrop == null && ObjectDB.instance != null && !string.IsNullOrWhiteSpace(prefab))
                {
                    GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefab);
                    if (itemPrefab != null)
                        itemDrop = itemPrefab.GetComponent<ItemDrop>();
                }

                if (itemDrop == null)
                    return null;

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
                if (item == null || item.m_prefab == null)
                    return;

                GetStackQualityFromStack(item.m_stack, out int stack, out int quality);
                item.m_stack = GetStackFromStackQuality(Mathf.Clamp(stack, 1, item.m_prefab.m_itemData.m_shared.m_maxStackSize), quality);
            }

            public static void GetStackQualityFromStack(int encodedStack, out int stack, out int quality)
            {
                stack = encodedStack % qualityStackMultiplier;
                quality = encodedStack / qualityStackMultiplier;
            }

            public static int GetStackFromStackQuality(int stack, int quality)
            {
                return stack + qualityStackMultiplier * quality;
            }

            public static int GetStackFromStack(int encodedStack)
            {
                return encodedStack % qualityStackMultiplier;
            }
        }
    }
}
