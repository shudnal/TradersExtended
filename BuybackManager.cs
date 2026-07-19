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
            if (worldRecords == null || !worldRecords.TryGetValue(traderName, out BuybackRecord record))
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
                Inventory inventory = new Inventory("Traders Extended buyback", null, 1, 1);
                inventory.Load(new ZPackage(record.itemData));
                ItemDrop.ItemData item = inventory.GetAllItems().FirstOrDefault();
                if (item == null)
                {
                    worldRecords.Remove(traderName);
                    Save();
                    return null;
                }

                return new StorePanel.ItemToSell
                {
                    itemType = record.itemType,
                    item = item,
                    stack = record.stack,
                    price = record.price,
                    amount = record.amount,
                    quality = record.quality,
                    currency = TraderCurrency.GetCurrency(record.currency, StoreGui.instance)
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
                ItemDrop.ItemData savedItem = item.item.Clone();
                savedItem.m_equipped = false;
                savedItem.m_gridPos = new Vector2i(0, 0);

                Inventory inventory = new Inventory("Traders Extended buyback", null, 1, 1);
                inventory.m_inventory.Add(savedItem);
                ZPackage package = new ZPackage();
                inventory.Save(package);

                Dictionary<string, BuybackRecord> worldRecords = GetWorldRecords(create: true);
                if (worldRecords == null)
                    return;

                worldRecords[TraderName(trader)] = new BuybackRecord
                {
                    createdAt = GetWorldTime(),
                    itemData = package.GetBase64(),
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
            if (!data.worlds.TryGetValue(world, out Dictionary<string, BuybackRecord> records) && create)
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
