using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static class BuybackManager
    {
        private const string CustomDataKey = pluginID + ".Buyback";

        [Serializable]
        private sealed class BuybackSaveData
        {
            public Dictionary<string, Dictionary<string, BuybackRecord>> worlds =
                new Dictionary<string, Dictionary<string, BuybackRecord>>(StringComparer.OrdinalIgnoreCase);
        }

        [Serializable]
        private sealed class BuybackRecord
        {
            public double createdAt;
            public string itemData;
            // Older records stored one representative item rather than every removed stack.
            public int[] itemAmounts;
            public StorePanel.ItemToSell.ItemType itemType;
            public int stack;
            public int price;
            public int amount;
            public int quality;
            public string currency;
        }

        private static BuybackSaveData cachedData;
        private static Player cachedPlayer;
        private static string cachedRawData;

        internal static StorePanel.ItemToSell Get(Trader trader)
        {
            if (trader == null || Player.m_localPlayer == null || ZNet.instance == null)
                return null;

            string traderName = TraderName(trader);
            ResolvedTraderConfig config = TraderConfigManager.Get(trader);
            if (!config.EnableBuybackForLastItemSold)
                return null;

            Dictionary<string, BuybackRecord> worldRecords = GetWorldRecords(create: false);
            if (worldRecords == null || !worldRecords.TryGetValue(traderName, out BuybackRecord record) || record == null)
                return null;

            int lifetime = Math.Max(config.BuybackLifetimeInWorldSeconds, 0);
            if (lifetime > 0 && GetWorldTime() - record.createdAt >= lifetime)
            {
                worldRecords.Remove(traderName);
                Save();
                return null;
            }

            try
            {
                if (record.price <= 0 || string.IsNullOrEmpty(record.itemData) ||
                    double.IsNaN(record.createdAt) || double.IsInfinity(record.createdAt))
                    throw new InvalidOperationException("Invalid buyback record.");

                int count = record.itemAmounts?.Length ?? 1;
                if (count < 1 || count > 10000)
                    throw new InvalidOperationException("Invalid buyback stack count.");

                Inventory inventory = new Inventory("Traders Extended buyback", null, count, 1);
                bool previousForceDisableInit = ZNetView.m_forceDisableInit;
                try { inventory.Load(new ZPackage(record.itemData)); }
                finally { ZNetView.m_forceDisableInit = previousForceDisableInit; }

                List<ItemDrop.ItemData> items = inventory.GetAllItems().OrderBy(item => item.m_gridPos.x).ToList();
                // A temporarily unavailable mod prefab must not erase the persisted receipt.
                if (items.Count != count)
                    return null;

                if (record.itemAmounts != null)
                {
                    for (int index = 0; index < count; index++)
                    {
                        if (record.itemAmounts[index] <= 0)
                            throw new InvalidOperationException("Invalid buyback item amount.");
                        // Inventory.Load clamps to today's stack limit; keep the full sold quantity.
                        items[index].m_stack = record.itemAmounts[index];
                    }
                }
                else
                {
                    items[0].m_stack = record.itemType == StorePanel.ItemToSell.ItemType.Stack ? record.stack :
                        record.itemType == StorePanel.ItemToSell.ItemType.Combined ? record.amount : items[0].m_stack;
                }
                long amount = items.Sum(item => (long)item.m_stack);
                if (amount <= 0 || amount > int.MaxValue)
                    throw new InvalidOperationException("Invalid buyback total quantity.");

                ItemDrop currency = string.IsNullOrWhiteSpace(record.currency)
                    ? TraderCurrency.GetCurrency(record.currency, StoreGui.instance)
                    : ObjectDB.instance?.GetItemPrefab(record.currency)?.GetComponent<ItemDrop>();
                if (currency == null)
                    return null;

                return new StorePanel.ItemToSell
                {
                    // A buyback is one indivisible receipt, regardless of the original offer's lot size.
                    itemType = StorePanel.ItemToSell.ItemType.Combined,
                    item = items[0],
                    stack = 1,
                    price = record.price,
                    amount = (int)amount,
                    quality = record.quality,
                    currency = currency,
                    soldItems = items
                };
            }
            catch (Exception exception)
            {
                LogWarning($"Could not restore buyback for trader '{traderName}': {exception.Message}");
                worldRecords.Remove(traderName);
                Save();
                return null;
            }
        }

        internal static void Set(Trader trader, StorePanel.ItemToSell item)
        {
            if (trader == null || item?.item == null || Player.m_localPlayer == null || ZNet.instance == null)
                return;

            try
            {
                List<ItemDrop.ItemData> items = (item.soldItems ?? new List<ItemDrop.ItemData> { item.item })
                    .Select(saved => saved.Clone()).ToList();
                if (items.Count == 0 || items.Count > 10000 || items.Any(saved => saved.m_stack <= 0))
                    throw new InvalidOperationException("Invalid buyback items.");
                Inventory inventory = new Inventory("Traders Extended buyback", null, items.Count, 1);
                for (int index = 0; index < items.Count; index++)
                {
                    items[index].m_equipped = false;
                    items[index].m_gridPos = new Vector2i(index, 0);
                    inventory.m_inventory.Add(items[index]);
                }
                ZPackage package = new ZPackage();
                inventory.Save(package);

                Dictionary<string, BuybackRecord> worldRecords = GetWorldRecords(create: true);
                if (worldRecords == null)
                    return;

                worldRecords[TraderName(trader)] = new BuybackRecord
                {
                    createdAt = GetWorldTime(),
                    itemData = package.GetBase64(),
                    itemAmounts = items.Select(saved => saved.m_stack).ToArray(),
                    itemType = item.itemType,
                    stack = item.stack,
                    price = item.price,
                    amount = item.amount,
                    quality = item.quality,
                    currency = item.currency != null ? Utils.GetPrefabName(item.currency.gameObject) : string.Empty
                };

                Save();
            }
            catch (Exception exception)
            {
                LogWarning($"Could not save buyback for trader '{TraderName(trader)}': {exception.Message}");
            }
        }

        internal static void Remove(Trader trader)
        {
            Dictionary<string, BuybackRecord> worldRecords = GetWorldRecords(create: false);
            if (worldRecords == null || trader == null || !worldRecords.Remove(TraderName(trader)))
                return;

            Save();
        }

        internal static void ResetCache()
        {
            cachedData = null;
            cachedPlayer = null;
            cachedRawData = null;
        }

        private static Dictionary<string, BuybackRecord> GetWorldRecords(bool create)
        {
            BuybackSaveData data = Load();
            if (data == null || ZNet.instance == null)
                return null;

            string world = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture);
            if ((!data.worlds.TryGetValue(world, out Dictionary<string, BuybackRecord> records) || records == null) && create)
            {
                records = new Dictionary<string, BuybackRecord>(StringComparer.OrdinalIgnoreCase);
                data.worlds[world] = records;
            }

            return records;
        }

        private static BuybackSaveData Load()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
                return null;

            player.m_customData.TryGetValue(CustomDataKey, out string rawData);
            if (cachedPlayer == player && string.Equals(cachedRawData, rawData, StringComparison.Ordinal) && cachedData != null)
                return cachedData;

            cachedPlayer = player;
            cachedRawData = rawData;
            try
            {
                cachedData = string.IsNullOrWhiteSpace(rawData)
                    ? new BuybackSaveData()
                    : JsonConvert.DeserializeObject<BuybackSaveData>(rawData) ?? new BuybackSaveData();
            }
            catch (Exception exception)
            {
                LogWarning($"Could not read saved buyback data: {exception.Message}");
                cachedData = new BuybackSaveData();
            }

            cachedData.worlds ??= new Dictionary<string, Dictionary<string, BuybackRecord>>(StringComparer.OrdinalIgnoreCase);
            return cachedData;
        }

        private static void Save()
        {
            Player player = Player.m_localPlayer;
            if (player == null || cachedData == null)
                return;

            cachedRawData = JsonConvert.SerializeObject(cachedData, Formatting.None);
            player.m_customData[CustomDataKey] = cachedRawData;
            cachedPlayer = player;
        }

        private static double GetWorldTime()
        {
            return ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0d;
        }
    }
}
