using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal sealed class ResolvedTraderConfig
    {
        internal string TraderName;

        internal bool SellOnlyDiscoveredItems;
        internal HashSet<string> UndiscoveredItemsListToSell;

        internal bool CanRepairWeapons;
        internal bool CanRepairArmor;
        internal int TradersRepairCost;
        internal string RepairCurrencyPrefab;

        internal bool TradersUseCoins;
        internal bool TradersUseFlexiblePricing;
        internal string TraderCurrencyPrefab;

        internal int CoinsAfterReplenishmentMinimum;
        internal int CoinsReplenishedDaily;
        internal int CoinsRemovedDaily;
        internal int CoinsAfterReplenishmentMaximum;
        internal float TraderDiscount;
        internal float TraderMarkup;
        internal int TraderCoinsReplenishmentRateInDays;
        internal bool SendReplenishmentMessageInTheMorning;

        internal string CustomTradersPrefabNames;
        internal bool DisableVanillaItems;
        internal bool DisableOtherModsItems;
        internal float QualityMultiplier;
        internal bool HideEquippedAndHotbarItems;
        internal bool AddCommonValuableItemsToSellList;
        internal Vector2 FixedStoreGuiPosition;

        internal bool EnableBuybackForLastItemSold;
        internal int BuybackLifetimeInWorldSeconds;
        internal Color BuybackItemBackgroundColor;
        internal Color BuybackItemHighlightedColor;
        internal Color BuybackItemFontColor;

        internal bool ShiftStoreGuiForEpicLoot;
    }

    internal static class TraderConfigManager
    {
        private static readonly Dictionary<string, JObject> traderOverrides = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ResolvedTraderConfig> resolvedConfigs = new Dictionary<string, ResolvedTraderConfig>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> invalidValuesLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static void LoadSyncedConfigs()
        {
            traderOverrides.Clear();
            resolvedConfigs.Clear();
            invalidValuesLogged.Clear();

            Dictionary<string, string> synchronizedConfigs = traderConfigFiles.Value ?? new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> config in synchronizedConfigs.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryParseSyncedTraderConfigKey(config.Key, out string traderName, out string fileName))
                    continue;

                JObject parsed = DeserializeTraderConfig(config.Value, fileName);
                if (parsed == null)
                    continue;

                string normalizedTraderName = TraderName(traderName);
                traderOverrides[normalizedTraderName] = parsed;
                LogLoadedOverrides(normalizedTraderName, fileName, parsed);
            }

            RefreshRuntime();
        }

        private static void LogLoadedOverrides(string traderName, string fileName, JObject root)
        {
            List<string> overrides = new List<string>();

            AddLoggedOverride<bool>(root, overrides, "Item discovery", "Sell only discovered items");
            AddLoggedOverride<string>(root, overrides, "Item discovery", "Undiscovered items list to sell");

            AddLoggedOverride<bool>(root, overrides, "Trader repair", "Weapons");
            AddLoggedOverride<bool>(root, overrides, "Trader repair", "Armor");
            AddLoggedOverride<int>(root, overrides, "Trader repair", "Repair cost");
            AddLoggedOverride<string>(root, overrides, "Trader repair", "Repair currency");

            AddLoggedOverride<bool>(root, overrides, "Trader coins", "Use currency");
            AddLoggedOverride<bool>(root, overrides, "Trader coins", "Use flexible pricing");

            AddLoggedOverride<int>(root, overrides, "Trader pricing", "Amount of currency after replenishment minimum");
            AddLoggedOverride<int>(root, overrides, "Trader pricing", "Amount of currency replenished daily");
            AddLoggedOverride<int>(root, overrides, "Trader pricing", "Amount of currency removed daily");
            AddLoggedOverride<int>(root, overrides, "Trader pricing", "Amount of currency after replenishment maximum");
            AddLoggedOverride<float>(root, overrides, "Trader pricing", "Trader discount");
            AddLoggedOverride<float>(root, overrides, "Trader pricing", "Trader markup");
            AddLoggedOverride<int>(root, overrides, "Trader pricing", "Currency replenishment rate in days");
            AddLoggedOverride<bool>(root, overrides, "Trader pricing", "Send replenishment message in the morning");

            AddLoggedOverride<string>(root, overrides, "Trader currency", "Override");

            AddLoggedOverride<bool>(root, overrides, "Trader buyback", "Enable buyback for last item sold");
            AddLoggedOverride<int>(root, overrides, "Trader buyback", "Buyback lifetime in world seconds");

            AddLoggedOverride<bool>(root, overrides, "Misc", "Disable vanilla items");
            AddLoggedOverride<bool>(root, overrides, "Misc", "Disable other mods items");
            AddLoggedOverride<bool>(root, overrides, "Misc", "Add common valuable items to sell list");
            AddLoggedOverride<Vector2>(root, overrides, "Misc", "Fixed position for Store GUI");
            AddLoggedOverride<bool>(root, overrides, "Misc", "Store GUI EpicLoot compatibility");

            LogInfo($"Loaded personal trader config '{fileName}' for trader '{traderName}' with {overrides.Count} valid override(s).");
            foreach (string entry in overrides)
                LogInfo("  " + entry);
        }

        private static void AddLoggedOverride<T>(JObject root, List<string> overrides, string sectionName, string settingName)
        {
            if (TryGetValue(root, sectionName, settingName, out T value))
                overrides.Add($"[{sectionName}] {settingName} = {FormatOverrideValue(value)}");
        }

        private static string FormatOverrideValue<T>(T value)
        {
            object boxed = value;
            return boxed switch
            {
                null => "null",
                string text => '"' + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + '"',
                bool boolean => boolean ? "true" : "false",
                float number => number.ToString("0.###", CultureInfo.InvariantCulture),
                double number => number.ToString("0.###", CultureInfo.InvariantCulture),
                Vector2 vector => $"({vector.x.ToString("0.###", CultureInfo.InvariantCulture)}, {vector.y.ToString("0.###", CultureInfo.InvariantCulture)})",
                Color color => $"RGBA({color.r.ToString("0.###", CultureInfo.InvariantCulture)}, {color.g.ToString("0.###", CultureInfo.InvariantCulture)}, {color.b.ToString("0.###", CultureInfo.InvariantCulture)}, {color.a.ToString("0.###", CultureInfo.InvariantCulture)})",
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => boxed.ToString()
            };
        }

        internal static void Invalidate()
        {
            resolvedConfigs.Clear();
            invalidValuesLogged.Clear();
        }

        internal static void InvalidateAndRefresh()
        {
            Invalidate();
            RefreshRuntime();
        }

        private static void RefreshRuntime()
        {
            TooltipPrices.Rebuild();

            if (StoreGui.instance == null || StoreGui.instance.m_trader == null || !StoreGui.IsVisible())
                return;

            TraderCurrency.ApplyTraderCurrency(StoreGui.instance);
            StorePanel.SetStoreGuiPosition();
            RepairPanel.Update(StoreGui.instance);
            TraderCoins.UpdateTraderCoinsVisibility();
            StoreGui.instance.FillList();
            StorePanel.UpdateNames();
        }

        internal static ResolvedTraderConfig Get(Trader trader)
        {
            return Get(TraderName(trader));
        }

        internal static ResolvedTraderConfig Get(string traderName)
        {
            traderName = TraderName(traderName);
            if (!resolvedConfigs.TryGetValue(traderName, out ResolvedTraderConfig config))
            {
                config = Build(traderName);
                resolvedConfigs[traderName] = config;
            }

            return config;
        }

        internal static IEnumerable<string> GetKnownTraderNames()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                TraderName("Haldor"),
                TraderName("Hildir"),
                TraderName("BogWitch")
            };

            result.UnionWith(traderOverrides.Keys);

            foreach (string key in tradeableItems.Keys.Concat(sellableItems.Keys))
            {
                int separator = key.LastIndexOf('.');
                string trader = separator > 0 ? key.Substring(0, separator) : key;
                if (!string.Equals(trader, "common", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(trader))
                    result.Add(TraderName(trader));
            }

            string custom = tradersCustomPrefabs?.Value ?? string.Empty;
            foreach (string trader in SplitList(custom))
                result.Add(TraderName(trader));


            return result.Where(name => !string.IsNullOrWhiteSpace(name));
        }

        internal static bool IsAutomaticCommonItemEnabled(Trader trader)
        {
            return Get(trader).AddCommonValuableItemsToSellList;
        }

        internal static bool IgnoreItemDiscovery(Trader trader, string prefabName)
        {
            return !string.IsNullOrWhiteSpace(prefabName) && Get(trader).UndiscoveredItemsListToSell.Contains(prefabName);
        }

        private static ResolvedTraderConfig Build(string traderName)
        {
            traderOverrides.TryGetValue(traderName, out JObject personal);

            bool defaultCanRepairWeapons = traderRepair.Value && ListContainsTrader(tradersToRepairWeapons.Value, traderName);
            bool defaultCanRepairArmor = traderRepair.Value && ListContainsTrader(tradersToRepairArmor.Value, traderName);

            bool hasPersonalCurrency = TryGetValue(personal, "Trader currency", "Override", out string personalCurrency);
            string configuredCurrency = hasPersonalCurrency
                ? (personalCurrency ?? string.Empty).Trim()
                : ResolveCurrencyForTrader(traderCurrencyOverrides.Value, traderName);

            return new ResolvedTraderConfig
            {
                TraderName = traderName,

                SellOnlyDiscoveredItems = GetValue(personal, "Item discovery", "Sell only discovered items", checkForDiscovery.Value),
                UndiscoveredItemsListToSell = ToSet(GetValue(personal, "Item discovery", "Undiscovered items list to sell", checkForDiscoveryIgnoreItems.Value)),

                CanRepairWeapons = GetValue(personal, "Trader repair", "Weapons", defaultCanRepairWeapons),
                CanRepairArmor = GetValue(personal, "Trader repair", "Armor", defaultCanRepairArmor),
                TradersRepairCost = GetValue(personal, "Trader repair", "Repair cost", traderRepairCost.Value),
                RepairCurrencyPrefab = (GetValue(personal, "Trader repair", "Repair currency", traderRepairCurrency.Value) ?? string.Empty).Trim(),

                TradersUseCoins = GetValue(personal, "Trader coins", "Use currency", traderUseCoins.Value),
                TradersUseFlexiblePricing = GetValue(personal, "Trader coins", "Use flexible pricing", traderUseFlexiblePricing.Value),
                TraderCurrencyPrefab = configuredCurrency,

                CoinsAfterReplenishmentMinimum = GetValue(personal, "Trader pricing", "Amount of currency after replenishment minimum", traderCoinsMinimumAmount.Value),
                CoinsReplenishedDaily = GetValue(personal, "Trader pricing", "Amount of currency replenished daily", traderCoinsIncreaseAmount.Value),
                CoinsRemovedDaily = GetValue(personal, "Trader pricing", "Amount of currency removed daily", traderCoinsDecreaseAmount.Value),
                CoinsAfterReplenishmentMaximum = GetValue(personal, "Trader pricing", "Amount of currency after replenishment maximum", traderCoinsMaximumAmount.Value),
                TraderDiscount = GetValue(personal, "Trader pricing", "Trader discount", traderDiscount.Value),
                TraderMarkup = GetValue(personal, "Trader pricing", "Trader markup", traderMarkup.Value),
                TraderCoinsReplenishmentRateInDays = GetValue(personal, "Trader pricing", "Currency replenishment rate in days", traderCoinsReplenishmentRate.Value),
                SendReplenishmentMessageInTheMorning = GetValue(personal, "Trader pricing", "Send replenishment message in the morning", traderCoinsSendReplenishmentMessage.Value),

                CustomTradersPrefabNames = tradersCustomPrefabs.Value,
                DisableVanillaItems = GetValue(personal, "Misc", "Disable vanilla items", disableVanillaItems.Value),
                DisableOtherModsItems = GetValue(personal, "Misc", "Disable other mods items", disableOtherModsItems.Value),
                QualityMultiplier = qualityMultiplier.Value,
                HideEquippedAndHotbarItems = hideEquippedAndHotbarItems.Value,
                AddCommonValuableItemsToSellList = GetValue(personal, "Misc", "Add common valuable items to sell list", addCommonValuableItemsToSellList.Value),
                FixedStoreGuiPosition = GetValue(personal, "Misc", "Fixed position for Store GUI", fixedStoreGuiPosition.Value),

                EnableBuybackForLastItemSold = GetValue(personal, "Trader buyback", "Enable buyback for last item sold", enableBuyBack.Value),
                BuybackLifetimeInWorldSeconds = GetValue(personal, "Trader buyback", "Buyback lifetime in world seconds", buybackLifetime.Value),
                BuybackItemBackgroundColor = colorBuybackNormal.Value,
                BuybackItemHighlightedColor = colorBuybackHighlighted.Value,
                BuybackItemFontColor = colorBuybackText.Value,

                ShiftStoreGuiForEpicLoot = GetValue(
                    personal,
                    "Misc",
                    "Store GUI EpicLoot compatibility",
                    ListContainsTrader(epicLootShiftedTraders.Value, traderName))
            };
        }

        internal static string ResolveCurrencyForTrader(string value, string traderName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string directValue = value.Trim();
            if (directValue.IndexOf(':') < 0 && directValue.IndexOf('=') < 0 &&
                directValue.IndexOf(',') < 0 && directValue.IndexOf(';') < 0 &&
                directValue.IndexOf('\n') < 0 && directValue.IndexOf('\r') < 0)
            {
                if (invalidValuesLogged.Add("Trader currency overrides/" + directValue))
                    LogWarning($"Ignored invalid trader currency override '{directValue}'. Expected TraderPrefab:CurrencyPrefab.");
                return string.Empty;
            }

            foreach (string rawEntry in directValue.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = rawEntry.Trim();
                int separator = entry.IndexOf(':');
                if (separator < 0)
                    separator = entry.IndexOf('=');
                if (separator <= 0 || separator >= entry.Length - 1)
                    continue;

                if (string.Equals(TraderName(entry.Substring(0, separator)), traderName, StringComparison.OrdinalIgnoreCase))
                    return entry.Substring(separator + 1).Trim();
            }

            return string.Empty;
        }

        private static bool ListContainsTrader(string value, string traderName)
        {
            return SplitList(value).Any(entry => string.Equals(TraderName(entry), traderName, StringComparison.OrdinalIgnoreCase));
        }

        private static HashSet<string> ToSet(string value)
        {
            return new HashSet<string>(SplitList(value), StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> ToTraderSet(string value)
        {
            return new HashSet<string>(SplitList(value).Select(TraderName), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> SplitList(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => entry.Trim())
                .Where(entry => !string.IsNullOrWhiteSpace(entry));
        }

        private static T GetValue<T>(JObject root, string sectionName, string settingName, T fallback)
        {
            return TryGetValue(root, sectionName, settingName, out T value) ? value : fallback;
        }

        private static bool TryGetValue<T>(JObject root, string sectionName, string settingName, out T value)
        {
            value = default;
            if (root == null ||
                root.GetValue(sectionName, StringComparison.OrdinalIgnoreCase) is not JObject section ||
                section.GetValue(settingName, StringComparison.OrdinalIgnoreCase) is not JToken token ||
                token.Type == JTokenType.Null)
                return false;

            try
            {
                if (typeof(T) == typeof(Vector2))
                    value = (T)(object)ParseVector2(token);
                else if (typeof(T) == typeof(string))
                {
                    if (token is JArray array)
                        value = (T)(object)string.Join(",", array.Select(item => item.Type == JTokenType.String ? item.Value<string>() : item.ToString(Formatting.None)));
                    else if (token is JObject obj)
                        value = (T)(object)string.Join(",", obj.Properties().Select(property => property.Name + ":" + (property.Value.Type == JTokenType.String ? property.Value.Value<string>() : property.Value.ToString(Formatting.None))));
                    else
                        value = (T)(object)(token.Type == JTokenType.String ? token.Value<string>() : token.ToString(Formatting.None));
                }
                else
                {
                    value = token.ToObject<T>();
                }

                return true;
            }
            catch (Exception exception)
            {
                string key = sectionName + "/" + settingName + "/" + token;
                if (invalidValuesLogged.Add(key))
                    LogWarning($"Invalid personal trader setting '{sectionName}.{settingName}': {exception.Message}. The BepInEx value will be used.");
                return false;
            }
        }

        private static Vector2 ParseVector2(JToken token)
        {
            if (token is not JObject obj)
                throw new FormatException("Expected an object with numeric 'x' and 'y' fields.");

            JToken xToken = obj.GetValue("x", StringComparison.OrdinalIgnoreCase);
            JToken yToken = obj.GetValue("y", StringComparison.OrdinalIgnoreCase);
            if (xToken == null || yToken == null ||
                (xToken.Type != JTokenType.Integer && xToken.Type != JTokenType.Float) ||
                (yToken.Type != JTokenType.Integer && yToken.Type != JTokenType.Float))
                throw new FormatException("Expected an object with numeric 'x' and 'y' fields.");

            return new Vector2(xToken.Value<float>(), yToken.Value<float>());
        }

        internal static JObject DeserializeTraderConfig(string content, string source)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new JObject();

            try
            {
                string extension = System.IO.Path.GetExtension(source);
                if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                    return JObject.Parse(content);

                object yaml = yamlDeserializer.Deserialize<object>(content);
                return JObject.Parse(JsonConvert.SerializeObject(yaml));
            }
            catch (Exception exception)
            {
                LogWarning($"Error parsing personal trader config '{source}': {exception.Message}");
                return null;
            }
        }
    }
}
