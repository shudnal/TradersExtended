using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using YamlDotNet.Serialization;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal enum EditorConfigKind
    {
        ItemList,
        TraderSettings,
        Unsupported
    }

    [Serializable]
    internal sealed class EditorFileInfo
    {
        public string Name { get; set; }
        public EditorConfigKind Kind { get; set; }
        public string Trader { get; set; }
        public ItemsListType ListType { get; set; }
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
    }

    internal sealed class EditorItemRow
    {
        internal EditorItemRow(TradeableItem item)
        {
            Item = item ?? new TradeableItem();
            StackText = Item.stack.ToString(CultureInfo.InvariantCulture);
            PriceText = Item.price.ToString(CultureInfo.InvariantCulture);
            QualityText = Item.quality.ToString(CultureInfo.InvariantCulture);
        }

        internal TradeableItem Item;
        internal bool Selected;
        internal string StackText;
        internal string PriceText;
        internal string QualityText;
        internal string ValidationError;
    }

    internal sealed class ItemConfigDocument
    {
        internal ItemConfigDocument(string fileName, IEnumerable<TradeableItem> items)
        {
            FileName = fileName;
            Rows = (items ?? Enumerable.Empty<TradeableItem>()).Select(item => new EditorItemRow(item)).ToList();
        }

        internal string FileName { get; }
        internal List<EditorItemRow> Rows { get; }
        internal bool Dirty { get; set; }

        internal bool Validate(out string error)
        {
            error = string.Empty;
            for (int index = 0; index < Rows.Count; index++)
            {
                EditorItemRow row = Rows[index];
                if (!int.TryParse(row.StackText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int stack))
                {
                    error = $"Row {index + 1}: stack must be a valid integer.";
                    return false;
                }
                if (!int.TryParse(row.PriceText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int price))
                {
                    error = $"Row {index + 1}: price must be a valid integer.";
                    return false;
                }
                if (!int.TryParse(row.QualityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int quality))
                {
                    error = $"Row {index + 1}: quality must be a valid integer.";
                    return false;
                }

                row.Item.stack = stack;
                row.Item.price = price;
                row.Item.quality = quality;
            }

            return true;
        }

        internal List<TradeableItem> GetItems()
        {
            return Rows.Select(row => row.Item).ToList();
        }
    }

    internal enum TraderSettingType
    {
        Boolean,
        Integer,
        Float,
        String,
        ItemPrefab,
        Vector2
    }

    internal sealed class TraderSettingDefinition
    {
        internal TraderSettingDefinition(string section, string name, TraderSettingType type, Func<string, object> fallback, string description)
        {
            Section = section;
            Name = name;
            Type = type;
            Fallback = fallback;
            Description = description;
        }

        internal string Section { get; }
        internal string Name { get; }
        internal TraderSettingType Type { get; }
        internal Func<string, object> Fallback { get; }
        internal string Description { get; }
    }

    internal sealed class TraderSettingState
    {
        internal TraderSettingDefinition Definition;
        internal bool HasOverride;
        internal object Value;
        internal string EditText;
        internal string ValidationError;
        internal string VectorXText;
        internal string VectorYText;

        internal object EffectiveValue(string trader)
        {
            return HasOverride ? Value : Definition.Fallback(trader);
        }
    }

    internal sealed class TraderConfigDocument
    {
        internal TraderConfigDocument(string fileName, string trader, JObject source)
        {
            FileName = fileName;
            Trader = trader;
            Source = source ?? new JObject();
            Settings = TraderConfigSchema.CreateStates(Trader, Source);
        }

        internal string FileName { get; }
        internal string Trader { get; }
        internal JObject Source { get; }
        internal List<TraderSettingState> Settings { get; }
        internal bool Dirty { get; set; }

        internal JObject BuildRoot()
        {
            JObject result = (JObject)Source.DeepClone();
            foreach (TraderSettingState state in Settings)
            {
                JProperty sectionProperty = result.Properties().FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, state.Definition.Section, StringComparison.OrdinalIgnoreCase));
                JObject section = sectionProperty?.Value as JObject;
                if (state.HasOverride)
                {
                    if (section == null)
                    {
                        section = new JObject();
                        if (sectionProperty != null)
                            sectionProperty.Value = section;
                        else
                        {
                            sectionProperty = new JProperty(state.Definition.Section, section);
                            result.Add(sectionProperty);
                        }
                    }

                    JProperty settingProperty = section.Properties().FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, state.Definition.Name, StringComparison.OrdinalIgnoreCase));
                    JToken value = TraderConfigSchema.ToToken(state.Definition.Type, state.Value);
                    if (settingProperty != null)
                        settingProperty.Value = value;
                    else
                        section.Add(state.Definition.Name, value);
                }
                else if (section != null)
                {
                    JProperty settingProperty = section.Properties().FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, state.Definition.Name, StringComparison.OrdinalIgnoreCase));
                    settingProperty?.Remove();
                    if (!section.Properties().Any())
                        sectionProperty?.Remove();
                }
            }

            return result;
        }
    }

    internal static class TraderConfigSchema
    {
        internal static readonly IReadOnlyList<TraderSettingDefinition> Definitions = new List<TraderSettingDefinition>
        {
            new TraderSettingDefinition("Item discovery", "Sell only discovered items", TraderSettingType.Boolean,
                _ => checkForDiscovery.Value,
                "Prevent the trader from selling items that the player has not discovered."),
            new TraderSettingDefinition("Item discovery", "Undiscovered items list to sell", TraderSettingType.String,
                _ => checkForDiscoveryIgnoreItems.Value,
                "Comma-separated item prefab names that bypass discovery checks."),

            new TraderSettingDefinition("Trader repair", "Weapons", TraderSettingType.Boolean,
                trader => ContainsTrader(tradersToRepairWeapons.Value, trader),
                "Allow this trader to repair weapons."),
            new TraderSettingDefinition("Trader repair", "Armor", TraderSettingType.Boolean,
                trader => ContainsTrader(tradersToRepairArmor.Value, trader),
                "Allow this trader to repair armor."),
            new TraderSettingDefinition("Trader repair", "Repair cost", TraderSettingType.Integer,
                _ => traderRepairCost.Value,
                "Currency amount charged for one repair."),
            new TraderSettingDefinition("Trader repair", "Repair currency", TraderSettingType.ItemPrefab,
                _ => traderRepairCurrency.Value,
                "Item prefab used to pay for repairs."),

            new TraderSettingDefinition("Trader coins", "Use currency", TraderSettingType.Boolean,
                _ => traderUseCoins.Value,
                "Use a limited replenishing trader balance."),
            new TraderSettingDefinition("Trader coins", "Use flexible pricing", TraderSettingType.Boolean,
                _ => traderUseFlexiblePricing.Value,
                "Adjust prices according to the current trader balance."),

            new TraderSettingDefinition("Trader pricing", "Amount of currency after replenishment minimum", TraderSettingType.Integer,
                _ => traderCoinsMinimumAmount.Value,
                "Minimum trader balance after replenishment."),
            new TraderSettingDefinition("Trader pricing", "Amount of currency replenished daily", TraderSettingType.Integer,
                _ => traderCoinsIncreaseAmount.Value,
                "Currency added during a daily balance update."),
            new TraderSettingDefinition("Trader pricing", "Amount of currency removed daily", TraderSettingType.Integer,
                _ => traderCoinsDecreaseAmount.Value,
                "Currency removed during a daily balance update."),
            new TraderSettingDefinition("Trader pricing", "Amount of currency after replenishment maximum", TraderSettingType.Integer,
                _ => traderCoinsMaximumAmount.Value,
                "Maximum trader balance after replenishment."),
            new TraderSettingDefinition("Trader pricing", "Trader discount", TraderSettingType.Float,
                _ => traderDiscount.Value,
                "Multiplier used when the trader pays the player."),
            new TraderSettingDefinition("Trader pricing", "Trader markup", TraderSettingType.Float,
                _ => traderMarkup.Value,
                "Multiplier used when the trader sells to the player."),
            new TraderSettingDefinition("Trader pricing", "Currency replenishment rate in days", TraderSettingType.Integer,
                _ => traderCoinsReplenishmentRate.Value,
                "Number of world days between balance updates."),
            new TraderSettingDefinition("Trader pricing", "Send replenishment message in the morning", TraderSettingType.Boolean,
                _ => traderCoinsSendReplenishmentMessage.Value,
                "Show a message when the trader balance is replenished."),

            new TraderSettingDefinition("Trader currency", "Override", TraderSettingType.ItemPrefab,
                trader => TraderConfigManager.ResolveCurrencyForTrader(traderCurrencyOverrides.Value, TraderName(trader)),
                "Default currency item prefab for this trader."),

            new TraderSettingDefinition("Trader buyback", "Enable buyback for last item sold", TraderSettingType.Boolean,
                _ => enableBuyBack.Value,
                "Allow buying back the last item sold to this trader."),
            new TraderSettingDefinition("Trader buyback", "Buyback lifetime in world seconds", TraderSettingType.Integer,
                _ => buybackLifetime.Value,
                "World-time duration before the saved buyback expires. Zero disables expiration."),

            new TraderSettingDefinition("Misc", "Disable vanilla items", TraderSettingType.Boolean,
                _ => disableVanillaItems.Value,
                "Hide the trader's vanilla item list."),
            new TraderSettingDefinition("Misc", "Disable other mods items", TraderSettingType.Boolean,
                _ => disableOtherModsItems.Value,
                "Hide trader entries added by other mods."),
            new TraderSettingDefinition("Misc", "Add common valuable items to sell list", TraderSettingType.Boolean,
                _ => addCommonValuableItemsToSellList.Value,
                "Automatically add valuable ObjectDB items to this trader's sell list."),
            new TraderSettingDefinition("Misc", "Fixed position for Store GUI", TraderSettingType.Vector2,
                _ => fixedStoreGuiPosition.Value,
                "Fixed Store GUI position. Use zero coordinates to retain the normal position."),
            new TraderSettingDefinition("Misc", "Store GUI EpicLoot compatibility", TraderSettingType.Boolean,
                trader => ContainsTrader(epicLootShiftedTraders.Value, trader),
                "Shift the Store GUI for EpicLoot Adventure Mode compatibility.")
        };

        internal static List<TraderSettingState> CreateStates(string trader, JObject root)
        {
            List<TraderSettingState> result = new List<TraderSettingState>();
            foreach (TraderSettingDefinition definition in Definitions)
            {
                TraderSettingState state = new TraderSettingState
                {
                    Definition = definition,
                    Value = definition.Fallback(trader)
                };

                JObject section = root?.GetValue(definition.Section, StringComparison.OrdinalIgnoreCase) as JObject;
                JToken token = section?.GetValue(definition.Name, StringComparison.OrdinalIgnoreCase);
                if (token != null && token.Type != JTokenType.Null)
                {
                    state.HasOverride = true;
                    try
                    {
                        state.Value = FromToken(definition.Type, token);
                    }
                    catch (Exception exception)
                    {
                        state.ValidationError = exception.Message;
                        state.Value = definition.Fallback(trader);
                    }
                }

                state.EditText = FormatEditText(definition.Type, state.Value);
                if (definition.Type == TraderSettingType.Vector2)
                {
                    Vector2 vector = state.Value is Vector2 parsed ? parsed : Vector2.zero;
                    state.VectorXText = vector.x.ToString("0.###", CultureInfo.InvariantCulture);
                    state.VectorYText = vector.y.ToString("0.###", CultureInfo.InvariantCulture);
                }
                result.Add(state);
            }

            return result;
        }

        internal static object FromToken(TraderSettingType type, JToken token)
        {
            return type switch
            {
                TraderSettingType.Boolean => token.Value<bool>(),
                TraderSettingType.Integer => token.Value<int>(),
                TraderSettingType.Float => token.Value<float>(),
                TraderSettingType.String => TokenToString(token),
                TraderSettingType.ItemPrefab => TokenToString(token),
                TraderSettingType.Vector2 => ParseVector2(token),
                _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
            };
        }

        internal static JToken ToToken(TraderSettingType type, object value)
        {
            if (type == TraderSettingType.Vector2)
            {
                Vector2 vector = value is Vector2 parsed ? parsed : Vector2.zero;
                return new JObject
                {
                    ["x"] = vector.x,
                    ["y"] = vector.y
                };
            }

            return value == null ? JValue.CreateNull() : JToken.FromObject(value);
        }

        internal static string FormatEditText(TraderSettingType type, object value)
        {
            return type switch
            {
                TraderSettingType.Integer => Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                TraderSettingType.Float => Convert.ToSingle(value, CultureInfo.InvariantCulture).ToString("0.###", CultureInfo.InvariantCulture),
                TraderSettingType.String => value?.ToString() ?? string.Empty,
                TraderSettingType.ItemPrefab => value?.ToString() ?? string.Empty,
                _ => string.Empty
            };
        }

        private static string TokenToString(JToken token)
        {
            if (token.Type == JTokenType.String)
                return token.Value<string>() ?? string.Empty;
            if (token is JArray array)
                return string.Join(",", array.Select(item => item.ToString(Formatting.None)));
            return token.ToString(Formatting.None);
        }

        private static Vector2 ParseVector2(JToken token)
        {
            if (token is not JObject obj)
                throw new FormatException("Expected an object with numeric x and y fields.");

            JToken x = obj.GetValue("x", StringComparison.OrdinalIgnoreCase);
            JToken y = obj.GetValue("y", StringComparison.OrdinalIgnoreCase);
            if (x == null || y == null)
                throw new FormatException("Expected an object with numeric x and y fields.");

            return new Vector2(x.Value<float>(), y.Value<float>());
        }

        private static bool ContainsTrader(string value, string trader)
        {
            string normalized = TraderName(trader);
            return (value ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(TraderName)
                .Any(entry => string.Equals(entry, normalized, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class ConfigEditorSerialization
    {
        private static readonly string[] CsvHeaders =
        {
            nameof(TradeableItem.prefab),
            nameof(TradeableItem.stack),
            nameof(TradeableItem.price),
            nameof(TradeableItem.quality),
            nameof(TradeableItem.currency),
            nameof(TradeableItem.requiredGlobalKey),
            nameof(TradeableItem.notRequiredGlobalKey),
            nameof(TradeableItem.requiredPlayerKey),
            nameof(TradeableItem.notRequiredPlayerKey)
        };

        internal static string SerializeItems(IEnumerable<TradeableItem> items, string extension)
        {
            List<TradeableItem> list = (items ?? Enumerable.Empty<TradeableItem>()).ToList();
            extension = NormalizeExtension(extension);
            if (extension == ".json")
                return JsonConvert.SerializeObject(list, Formatting.Indented);

            if (extension == ".yaml" || extension == ".yml")
            {
                ISerializer serializer = new SerializerBuilder().DisableAliases().Build();
                return serializer.Serialize(list);
            }

            if (extension == ".csv")
            {
                List<string> lines = new List<string> { string.Join(",", CsvHeaders) };
                lines.AddRange(list.Select(item => string.Join(",", new[]
                {
                    Csv(item.prefab),
                    item.stack.ToString(CultureInfo.InvariantCulture),
                    item.price.ToString(CultureInfo.InvariantCulture),
                    item.quality.ToString(CultureInfo.InvariantCulture),
                    Csv(item.currency),
                    Csv(item.requiredGlobalKey),
                    Csv(item.notRequiredGlobalKey),
                    Csv(item.requiredPlayerKey),
                    Csv(item.notRequiredPlayerKey)
                })));
                return string.Join(Environment.NewLine, lines) + Environment.NewLine;
            }

            throw new NotSupportedException($"Unsupported item configuration extension '{extension}'.");
        }

        internal static string SerializeTrader(JObject root, string extension)
        {
            extension = NormalizeExtension(extension);
            if (extension == ".json")
                return (root ?? new JObject()).ToString(Formatting.Indented);

            if (extension == ".yaml" || extension == ".yml")
            {
                object plain = JsonConvert.DeserializeObject<object>((root ?? new JObject()).ToString(Formatting.None));
                ISerializer serializer = new SerializerBuilder().DisableAliases().Build();
                return serializer.Serialize(plain);
            }

            throw new NotSupportedException($"Unsupported trader configuration extension '{extension}'.");
        }

        internal static bool Validate(string fileName, string content, out string error)
        {
            error = string.Empty;
            try
            {
                if (TryParseConfigFileName(fileName, out _, out _))
                {
                    if (DeserializeItems(content, fileName) == null)
                    {
                        error = "The item configuration could not be parsed.";
                        return false;
                    }

                    return true;
                }

                if (TryParseTraderConfigFileName(fileName, out _))
                {
                    if (TraderConfigManager.DeserializeTraderConfig(content, fileName) == null)
                    {
                        error = "The trader configuration could not be parsed.";
                        return false;
                    }

                    return true;
                }

                error = "The file name does not match a supported Traders Extended configuration pattern.";
                return false;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        internal static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return string.Empty;
            extension = extension.Trim().ToLowerInvariant();
            if (!extension.StartsWith(".", StringComparison.Ordinal))
                extension = "." + extension;
            return extension;
        }

        internal static string Csv(string value)
        {
            value ??= string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
    }
}
