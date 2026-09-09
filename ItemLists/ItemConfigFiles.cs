using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using TradersExtended.Compatibility;
using ConditionalConfigSync;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using YamlDotNet.Serialization;

namespace TradersExtended
{
    public partial class TradersExtended
    {
        public enum ItemsListType
        {
            Buy,
            Sell
        }

        public static void InitCommands()
        {
            new Terminal.ConsoleCommand("tradersextended", "editor - Open the configuration editor, save [json|yml|csv] - Save the full item list as JSON by default, itemlist - Save the filtered item list as CSV", delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length >= 2)
                {
                    string action = args[1];
                    if (string.Equals(action, "editor", StringComparison.OrdinalIgnoreCase))
                        configEditor?.Toggle();
                    else if (string.Equals(action, "save", StringComparison.OrdinalIgnoreCase))
                        SaveFromObjectDB(args.Context, args.Length >= 3 ? args[2] : "json");
                    else if (string.Equals(action, "itemlist", StringComparison.OrdinalIgnoreCase))
                        ExportItemListFromObjectDB(args.Context);
                    else
                        args.Context.AddString("Actions: editor - Open the configuration editor, save [json|yml|csv] - Save the full item list as JSON by default, itemlist - Save the filtered item list as CSV");
                }
                else
                {
                    args.Context.AddString("Actions: editor - Open the configuration editor, save [json|yml|csv] - Save the full item list as JSON by default, itemlist - Save the filtered item list as CSV");
                }
            }, false, false, false, false, false, delegate
            {
                return new List<string> { "editor", "save", "itemlist" };
            }, true, false);

            new Terminal.ConsoleCommand("settradercoins", "[trader] [amount]", delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length <= 1)
                    return false;

                TraderCoins.SetTraderCoins(args[1], args.TryParameterInt(2, TraderConfigManager.Get(args[1]).CoinsAfterReplenishmentMinimum));
                return true;
            }, true, false, true, false, false, delegate { return TraderCoins.GetTraderPrefabs(); }, true, false, true);
        }

        private sealed class ObjectDbExportItem
        {
            public string prefab { get; set; }
            public int price { get; set; }
        }

        public static void SaveFromObjectDB(Terminal context, string requestedFormat = "json")
        {
            if (ObjectDB.instance == null)
            {
                context.AddString("ObjectDB is not initialized.");
                return;
            }

            string format = (requestedFormat ?? "json").Trim().ToLowerInvariant();
            if (format == "yaml")
                format = "yml";

            if (format != "json" && format != "yml" && format != "csv")
            {
                context.AddString("Usage: tradersextended save [json|yml|csv]");
                return;
            }

            List<ObjectDbExportItem> allItems = new List<ObjectDbExportItem>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null || !prefab.TryGetComponent(out ItemDrop itemDrop))
                    continue;

                allItems.Add(new ObjectDbExportItem
                {
                    prefab = prefab.name,
                    price = Math.Max(itemDrop.m_itemData.m_shared.m_value, 1)
                });
            }

            allItems = allItems.OrderBy(item => item.prefab, StringComparer.Ordinal).ToList();

            string outputDirectory = Path.Combine(configDirectory.FullName, pluginID);
            Directory.CreateDirectory(outputDirectory);
            string filename = Path.Combine(outputDirectory, $"ObjectDB.list.{format}");

            if (format == "json")
            {
                string json = JsonConvert.SerializeObject(allItems, Formatting.Indented);
                File.WriteAllText(filename, json, new UTF8Encoding(false));
            }
            else if (format == "yml")
            {
                ISerializer serializer = new SerializerBuilder()
                    .DisableAliases()
                    .Build();
                File.WriteAllText(filename, serializer.Serialize(allItems), new UTF8Encoding(false));
            }
            else
            {
                IEnumerable<string> lines = new[] { "prefab,price" }.Concat(
                    allItems.Select(item => CsvField(item.prefab) + "," + item.price.ToString(CultureInfo.InvariantCulture)));
                File.WriteAllLines(filename, lines, new UTF8Encoding(false));
            }

            context.AddString($"Saved {allItems.Count} items to \"\\config\\{pluginID}\\ObjectDB.list.{format}\"");
        }

        private static string CsvField(string value)
        {
            value = value ?? string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        public static void ExportItemListFromObjectDB(Terminal context)
        {
            if (ObjectDB.instance == null)
            {
                context.AddString("ObjectDB is not initialized.");
                return;
            }

            List<string[]> allItems = new List<string[]>();
            HashSet<string> itemNames = new HashSet<string>(StringComparer.Ordinal);

            Trader[] traders = Resources.FindObjectsOfTypeAll<Trader>();
            foreach (Trader trader in traders)
            {
                if (trader == null || trader.m_items == null)
                    continue;

                foreach (Trader.TradeItem item in trader.m_items)
                {
                    if (item == null || item.m_prefab == null || !itemNames.Add(item.m_prefab.name))
                        continue;

                    if (item.m_prefab.m_itemData.m_shared.m_name.IsNullOrWhiteSpace() || item.m_prefab.m_itemData.m_shared.m_description == null ||
                        item.m_prefab.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None || item.m_prefab.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization)
                        continue;

                    allItems.Add(new[]
                    {
                        item.m_prefab.name,
                        Localization.instance.Localize(item.m_prefab.m_itemData.m_shared.m_name),
                        Math.Max(item.m_prefab.m_itemData.m_shared.m_value, 0).ToString(CultureInfo.InvariantCulture),
                        item.m_price.ToString(CultureInfo.InvariantCulture),
                        item.m_stack.ToString(CultureInfo.InvariantCulture),
                        Utils.GetPrefabName(trader.name)
                    });
                }
            }

            IEnumerable<Humanoid> enemies = Resources.FindObjectsOfTypeAll<Humanoid>().Where(humanoid => humanoid != null && humanoid.TryGetComponent<BaseAI>(out _));
            foreach (Humanoid humanoid in enemies)
            {
                foreach (GameObject item in humanoid.m_defaultItems)
                {
                    if (item != null && item.TryGetComponent(out ItemDrop _))
                        itemNames.Add(item.name);
                }
            }

            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null || !prefab.TryGetComponent(out ItemDrop itemDrop) || !itemNames.Add(prefab.name))
                    continue;

                if (!itemDrop.m_itemData.m_shared.m_name.StartsWith("$", StringComparison.Ordinal) ||
                    itemDrop.m_itemData.m_shared.m_description == null ||
                    itemDrop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.None ||
                    itemDrop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Customization)
                    continue;

                allItems.Add(new[]
                {
                    itemDrop.name,
                    Localization.instance.Localize(itemDrop.m_itemData.m_shared.m_name),
                    Math.Max(itemDrop.m_itemData.m_shared.m_value, 0).ToString(CultureInfo.InvariantCulture),
                    string.Empty,
                    string.Empty,
                    string.Empty
                });
            }

            IEnumerable<string> lines = new[] { "prefab,name,sellPrice,buyPrice,stack,trader" }.Concat(
                allItems
                    .OrderByDescending(item => item[5], StringComparer.Ordinal)
                    .ThenBy(item => item[0], StringComparer.Ordinal)
                    .Select(item => string.Join(",", item.Select(CsvField))));

            string outputDirectory = Path.Combine(configDirectory.FullName, pluginID);
            Directory.CreateDirectory(outputDirectory);
            string filename = Path.Combine(outputDirectory, "ItemList.csv");
            File.WriteAllLines(filename, lines, new UTF8Encoding(false));

            context.AddString($"Saved {allItems.Count} items to \"\\config\\{pluginID}\\ItemList.csv\"");
        }

        public static void SetupConfigWatcher()
        {
            DisposeConfigWatchers();
            AddConfigWatcher(configDirectory);
            ReadConfigs();
        }

        private static void AddConfigWatcher(DirectoryInfo directory)
        {
            if (directory == null || !directory.Exists)
                return;

            FileSystemWatcher watcher = new FileSystemWatcher(directory.FullName, "*")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                SynchronizingObject = ThreadingHelper.SynchronizingObject,
                EnableRaisingEvents = false
            };

            watcher.Changed += OnConfigFileChanged;
            watcher.Created += OnConfigFileChanged;
            watcher.Deleted += OnConfigFileChanged;
            watcher.Renamed += OnConfigFileChanged;
            configWatchers.Add(watcher);
            watcher.EnableRaisingEvents = true;
        }

        private static void DisposeConfigWatchers()
        {
            foreach (FileSystemWatcher watcher in configWatchers)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= OnConfigFileChanged;
                watcher.Created -= OnConfigFileChanged;
                watcher.Deleted -= OnConfigFileChanged;
                watcher.Renamed -= OnConfigFileChanged;
                watcher.Dispose();
            }

            configWatchers.Clear();
        }

        private static void OnConfigFileChanged(object sender, FileSystemEventArgs args)
        {
            if (args == null || instance == null)
                return;

            bool supported = IsWatchedConfigPath(args.FullPath);
            if (!supported && args is RenamedEventArgs renamed)
                supported = IsWatchedConfigPath(renamed.OldFullPath);
            if (!supported)
                return;

            if (configReloadCoroutine != null)
                instance.StopCoroutine(configReloadCoroutine);
            configReloadCoroutine = instance.StartCoroutine(ReloadConfigsAfterFileWrite());
        }

        private static bool IsWatchedConfigPath(string path)
        {
            string name = Path.GetFileName(path);
            if (!TryParseConfigFileName(name, out _, out _) && !TryParseTraderConfigFileName(name, out _))
                return false;
            string directory = Path.GetFullPath(Path.Combine(Paths.ConfigPath, pluginID)) + Path.DirectorySeparatorChar;
            return name.StartsWith(pluginID + ".", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(path).StartsWith(directory, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerator ReloadConfigsAfterFileWrite()
        {
            yield return new WaitForSecondsRealtime(0.25f);
            configReloadCoroutine = null;
            ReadConfigs();
        }

        private sealed class PersonalTraderConfigSource
        {
            internal string FileName;
            internal string Content;
        }

        internal static bool ReadConfigs()
        {
            try
            {
                Dictionary<string, string> localItemConfigs = new Dictionary<string, string>(StringComparer.Ordinal);
                Dictionary<string, PersonalTraderConfigSource> localTraderConfigs =
                    new Dictionary<string, PersonalTraderConfigSource>(StringComparer.OrdinalIgnoreCase);
                int index = AddEmbeddedConfigs(localItemConfigs, localTraderConfigs, 0);
                AddDirectoryConfigs(localItemConfigs, localTraderConfigs, configDirectory, "config", index);

                // Validate the complete snapshot before replacing any active or synchronized data.
                // A temporarily locked or half-written file is not an intentional deletion.
                foreach (KeyValuePair<string, string> item in localItemConfigs)
                    if (TryParseSyncedConfigKey(item.Key, out _, out _, out string fileName) && DeserializeItems(item.Value, fileName) == null)
                        throw new InvalidDataException($"Invalid item configuration '{fileName}'.");
                foreach (PersonalTraderConfigSource item in localTraderConfigs.Values)
                    if (TraderConfigManager.DeserializeTraderConfig(item.Content, item.FileName) == null)
                        throw new InvalidDataException($"Invalid personal trader configuration '{item.FileName}'.");

                Dictionary<string, string> synchronizedTraderConfigs = localTraderConfigs.ToDictionary(
                    pair => BuildSyncedTraderConfigKey(pair.Key, pair.Value.FileName), pair => pair.Value.Content, StringComparer.Ordinal);
                traderConfigFiles.AssignLocalValueAndNotify(synchronizedTraderConfigs);
                itemConfigs.AssignLocalValueAndNotify(localItemConfigs);
                return true;
            }
            catch (Exception exception)
            {
                LogWarning($"Configuration reload was not applied; the last valid snapshot is retained: {exception.Message}");
                return false;
            }
        }

        private static int AddDirectoryConfigs(
            Dictionary<string, string> itemTarget,
            Dictionary<string, PersonalTraderConfigSource> traderTarget,
            DirectoryInfo directory,
            string source,
            int index)
        {
            if (directory == null || !directory.Exists)
                return index;

            FileInfo[] files;
            try
            {
                string editorDirectory = Path.GetFullPath(Path.Combine(Paths.ConfigPath, pluginID));
                files = directory.GetFiles("*", SearchOption.AllDirectories)
                    .Where(file => IsSupportedConfigExtension(file.Extension))
                    .Where(file =>
                    {
                        bool legacyName = file.Name.StartsWith(pluginID + ".", StringComparison.OrdinalIgnoreCase);
                        string fullName = Path.GetFullPath(file.FullName);
                        bool inEditorDirectory = fullName.StartsWith(editorDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                        return legacyName || inEditorDirectory;
                    })
                    .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                throw new IOException($"Could not read configuration source '{directory.FullName}'.", exception);
            }

            foreach (FileInfo file in files.Where(file => TryParseConfigFileName(file.Name, out _, out _)))
            {
                try
                {
                    string content = ReadFileContent(file);
                    string key = BuildSyncedConfigKey(source, index++, file.Name);
                    itemTarget[key] = content;
                    LogInfo($"Found item config {file.FullName}");
                }
                catch (Exception exception)
                {
                    throw new IOException($"Could not read configuration source '{file.FullName}'.", exception);
                }
            }

            foreach (IGrouping<string, FileInfo> group in files
                .Select(file => new { File = file, Parsed = TryParseTraderConfigFileName(file.Name, out string trader), Trader = trader })
                .Where(entry => entry.Parsed)
                .GroupBy(entry => TraderName(entry.Trader), entry => entry.File, StringComparer.OrdinalIgnoreCase))
            {
                FileInfo selected = SelectPersonalTraderConfig(group.Key, group.ToArray(), file => file.Extension, file => file.FullName);
                if (selected == null)
                    continue;

                try
                {
                    traderTarget[group.Key] = new PersonalTraderConfigSource
                    {
                        FileName = selected.Name,
                        Content = ReadFileContent(selected)
                    };
                    LogInfo($"Found personal trader config {selected.FullName}");
                }
                catch (Exception exception)
                {
                    throw new IOException($"Could not read configuration source '{selected.FullName}'.", exception);
                }
            }

            return index;
        }

        private static string ReadFileContent(FileInfo file)
        {
            using (FileStream stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }

        private static int AddEmbeddedConfigs(
            Dictionary<string, string> itemTarget,
            Dictionary<string, PersonalTraderConfigSource> traderTarget,
            int index)
        {
            const string resourcePrefix = "TradersExtended.Configs.";
            Assembly assembly = Assembly.GetExecutingAssembly();
            string[] resources = assembly.GetManifestResourceNames()
                .Where(resource => resource.StartsWith(resourcePrefix, StringComparison.Ordinal) &&
                                   IsSupportedConfigExtension(Path.GetExtension(resource)))
                .OrderBy(resource => resource, StringComparer.Ordinal)
                .ToArray();

            foreach (string resource in resources)
            {
                string fileName = resource.Substring(resourcePrefix.Length);
                if (!TryParseConfigFileName(fileName, out _, out _) &&
                    !TryParseConfigFileName(pluginID + "." + fileName, out _, out _) &&
                    !TryParseEmbeddedConfigName(fileName, out _, out _))
                    continue;

                try
                {
                    using (Stream stream = assembly.GetManifestResourceStream(resource))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string key = BuildSyncedConfigKey("internal", index++, fileName);
                        itemTarget[key] = reader.ReadToEnd();
                        LogInfo($"Found embedded item config {resource}");
                    }
                }
                catch (Exception exception)
                {
                    throw new IOException($"Could not read configuration source '{resource}'.", exception);
                }
            }

            var personalResources = resources
                .Select(resource =>
                {
                    string fileName = resource.Substring(resourcePrefix.Length);
                    bool parsed = TryParseTraderConfigFileName(fileName, out string trader) ||
                                  TryParseTraderConfigFileName(pluginID + "." + fileName, out trader) ||
                                  TryParseEmbeddedTraderConfigName(fileName, out trader);
                    return new { Resource = resource, FileName = fileName, Parsed = parsed, Trader = trader };
                })
                .Where(entry => entry.Parsed)
                .GroupBy(entry => TraderName(entry.Trader), StringComparer.OrdinalIgnoreCase);

            foreach (var group in personalResources)
            {
                var selected = SelectPersonalTraderConfig(
                    group.Key,
                    group.ToArray(),
                    entry => Path.GetExtension(entry.FileName),
                    entry => entry.Resource);
                if (selected == null)
                    continue;

                try
                {
                    using (Stream stream = assembly.GetManifestResourceStream(selected.Resource))
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        traderTarget[group.Key] = new PersonalTraderConfigSource
                        {
                            FileName = selected.FileName,
                            Content = reader.ReadToEnd()
                        };
                        LogInfo($"Found embedded personal trader config {selected.Resource}");
                    }
                }
                catch (Exception exception)
                {
                    throw new IOException($"Could not read configuration source '{selected.Resource}'.", exception);
                }
            }

            return index;
        }

        private static T SelectPersonalTraderConfig<T>(
            string trader,
            T[] candidates,
            Func<T, string> extensionSelector,
            Func<T, string> nameSelector)
            where T : class
        {
            if (candidates == null || candidates.Length == 0)
                return null;

            T selected = candidates
                .OrderBy(candidate => PersonalTraderConfigExtensionPriority(extensionSelector(candidate)))
                .ThenBy(nameSelector, StringComparer.OrdinalIgnoreCase)
                .First();

            if (candidates.Length > 1)
            {
                string ignored = string.Join(", ", candidates.Where(candidate => !ReferenceEquals(candidate, selected)).Select(nameSelector));
                LogWarning($"Multiple personal config files were found for trader '{trader}'. Using '{nameSelector(selected)}' and ignoring: {ignored}");
            }

            return selected;
        }

        private static int PersonalTraderConfigExtensionPriority(string extension)
        {
            if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase))
                return 1;
            return 2;
        }

        private static bool TryParseEmbeddedConfigName(string fileName, out string trader, out ItemsListType listType)
        {
            trader = string.Empty;
            listType = ItemsListType.Buy;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string prefix = pluginID + ".";
            string remainder = withoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? withoutExtension.Substring(prefix.Length)
                : withoutExtension;

            return TryParseConfigName(remainder, out trader, out listType);
        }

        internal static bool TryParseConfigFileName(string fileName, out string trader, out ItemsListType listType)
        {
            return TryParseConfigFileName(fileName, out trader, out listType, out _);
        }

        internal static bool TryParseConfigFileName(string fileName, out string trader, out ItemsListType listType, out string identifier)
        {
            trader = string.Empty;
            listType = ItemsListType.Buy;
            identifier = string.Empty;

            string extension = Path.GetExtension(fileName);
            if (!IsSupportedItemConfigExtension(extension))
                return false;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string prefix = pluginID + ".";
            string configName = withoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? withoutExtension.Substring(prefix.Length)
                : withoutExtension;

            return TryParseConfigName(configName, out trader, out listType, out identifier);
        }

        private static bool TryParseConfigName(string configName, out string trader, out ItemsListType listType)
        {
            return TryParseConfigName(configName, out trader, out listType, out _);
        }

        private static bool TryParseConfigName(string configName, out string trader, out ItemsListType listType, out string identifier)
        {
            trader = string.Empty;
            listType = ItemsListType.Buy;
            identifier = string.Empty;

            if (string.IsNullOrWhiteSpace(configName))
                return false;

            string[] segments = configName.Split('.');
            for (int index = 1; index < segments.Length; index++)
            {
                ItemsListType parsedListType;
                if (string.Equals(segments[index], nameof(ItemsListType.Buy), StringComparison.OrdinalIgnoreCase))
                    parsedListType = ItemsListType.Buy;
                else if (string.Equals(segments[index], nameof(ItemsListType.Sell), StringComparison.OrdinalIgnoreCase))
                    parsedListType = ItemsListType.Sell;
                else
                    continue;

                trader = string.Join(".", segments.Take(index));
                if (string.IsNullOrWhiteSpace(trader))
                    return false;

                identifier = string.Join(".", segments.Skip(index + 1));
                listType = parsedListType;
                return true;
            }

            return false;
        }

        internal static bool TryParseTraderConfigFileName(string fileName, out string trader)
        {
            trader = string.Empty;
            if (!IsSupportedTraderConfigExtension(Path.GetExtension(fileName)))
                return false;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string prefix = pluginID + ".";
            string configName = withoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? withoutExtension.Substring(prefix.Length)
                : withoutExtension;

            return TryParseTraderConfigName(configName, out trader);
        }

        private static bool TryParseEmbeddedTraderConfigName(string fileName, out string trader)
        {
            trader = string.Empty;
            if (!IsSupportedTraderConfigExtension(Path.GetExtension(fileName)))
                return false;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string prefix = pluginID + ".";
            string remainder = withoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? withoutExtension.Substring(prefix.Length)
                : withoutExtension;
            return TryParseTraderConfigName(remainder, out trader);
        }

        private static bool TryParseTraderConfigName(string configName, out string trader)
        {
            trader = string.Empty;
            const string suffix = ".config";
            if (string.IsNullOrWhiteSpace(configName) ||
                !configName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                configName.Length <= suffix.Length)
                return false;

            trader = configName.Substring(0, configName.Length - suffix.Length);
            return !string.IsNullOrWhiteSpace(trader);
        }

        internal static bool IsSupportedConfigExtension(string extension)
        {
            return IsSupportedItemConfigExtension(extension) || IsSupportedTraderConfigExtension(extension);
        }

        internal static bool IsSupportedItemConfigExtension(string extension)
        {
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsSupportedTraderConfigExtension(string extension)
        {
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSyncedTraderConfigKey(string trader, string fileName)
        {
            return TraderName(trader) + "/" + fileName;
        }

        internal static bool TryParseSyncedTraderConfigKey(string key, out string trader, out string fileName)
        {
            trader = string.Empty;
            fileName = string.Empty;
            if (string.IsNullOrWhiteSpace(key))
                return false;

            int separator = key.IndexOf('/');
            if (separator <= 0 || separator >= key.Length - 1)
                return false;

            trader = key.Substring(0, separator);
            fileName = key.Substring(separator + 1);
            if (!TryParseTraderConfigFileName(fileName, out string fileTrader) &&
                !TryParseEmbeddedTraderConfigName(fileName, out fileTrader))
                return false;

            return string.Equals(TraderName(trader), TraderName(fileTrader), StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSyncedConfigKey(string source, int index, string fileName)
        {
            return source + "/" + index.ToString("D6") + "/" + fileName;
        }

        private static bool TryGetSyncedConfigMetadata(string key, out string source, out int index, out string fileName)
        {
            source = string.Empty;
            index = -1;
            fileName = string.Empty;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            int sourceSeparator = key.IndexOf('/');
            if (sourceSeparator <= 0)
                return false;

            int indexSeparator = key.IndexOf('/', sourceSeparator + 1);
            if (indexSeparator <= sourceSeparator + 1 || indexSeparator >= key.Length - 1)
                return false;

            source = key.Substring(0, sourceSeparator);
            if (!int.TryParse(key.Substring(sourceSeparator + 1, indexSeparator - sourceSeparator - 1), out index))
                return false;

            fileName = key.Substring(indexSeparator + 1);
            return !string.IsNullOrWhiteSpace(fileName);
        }

        private static void StartConfigLoad()
        {
            if (instance == null)
                return;

            if (configLoadCoroutine != null)
                instance.StopCoroutine(configLoadCoroutine);
            configLoadCoroutine = instance.StartCoroutine(LoadConfigs());
        }

        private static IEnumerator LoadConfigs()
        {
            yield return null;

            Dictionary<string, List<TradeableItem>> stagedBuy = new Dictionary<string, List<TradeableItem>>();
            Dictionary<string, List<TradeableItem>> stagedSell = new Dictionary<string, List<TradeableItem>>();

            Dictionary<string, string> synchronizedConfigs = itemConfigs.Value ?? new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> itemConfig in synchronizedConfigs
                .OrderBy(pair => ConfigSourcePriority(pair.Key))
                .ThenBy(pair => ConfigSourceOrder(pair.Key))
                .ThenBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!TryParseSyncedConfigKey(itemConfig.Key, out string trader, out ItemsListType listType, out string fileName))
                    continue;
                List<TradeableItem> items = DeserializeItems(itemConfig.Value, fileName);
                if (items == null)
                {
                    configLoadCoroutine = null;
                    yield break;
                }

                string listKey = TraderListKey(trader, listType);
                Dictionary<string, List<TradeableItem>> destination = listType == ItemsListType.Buy ? stagedBuy : stagedSell;
                if (!destination.TryGetValue(listKey, out List<TradeableItem> currentItems))
                {
                    currentItems = new List<TradeableItem>();
                    destination.Add(listKey, currentItems);
                }

                currentItems.AddRange(items.Where(item => item != null));
                LogInfo($"Loaded {items.Count} {listType.ToString().ToLowerInvariant()} item entries from {fileName}");
            }

            yield return AddCommonValuableItems(stagedSell);
            tradeableItems.Clear();
            sellableItems.Clear();
            foreach (KeyValuePair<string, List<TradeableItem>> list in stagedBuy)
                tradeableItems.Add(list.Key, list.Value);
            foreach (KeyValuePair<string, List<TradeableItem>> list in stagedSell)
                sellableItems.Add(list.Key, list.Value);
            TraderConfigManager.Invalidate();

            TooltipPrices.Rebuild();
            configLoadCoroutine = null;

            if (StoreGui.instance != null && StoreGui.instance.m_trader != null && StoreGui.IsVisible())
                StoreGui.instance.FillList();
        }

        private static bool TryParseSyncedConfigKey(string key, out string trader, out ItemsListType listType, out string fileName)
        {
            trader = string.Empty;
            listType = ItemsListType.Buy;
            fileName = key ?? string.Empty;

            if (TryGetSyncedConfigMetadata(key, out _, out _, out string synchronizedFileName))
            {
                fileName = synchronizedFileName;
                return TryParseConfigFileName(fileName, out trader, out listType) ||
                       TryParseEmbeddedConfigName(fileName, out trader, out listType);
            }

            // Keep accepting payloads produced by earlier 2.0.0 builds.
            if (string.IsNullOrWhiteSpace(key))
                return false;

            foreach (ItemsListType type in Enum.GetValues(typeof(ItemsListType)))
            {
                string marker = "." + type + ".";
                int markerIndex = key.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (markerIndex <= 0)
                    continue;

                trader = key.Substring(0, markerIndex);
                listType = type;
                return true;
            }

            return false;
        }

        private static int ConfigSourcePriority(string key)
        {
            if (TryGetSyncedConfigMetadata(key, out string source, out _, out _))
            {
                if (string.Equals(source, "internal", StringComparison.OrdinalIgnoreCase))
                    return 0;
                if (string.Equals(source, "plugin", StringComparison.OrdinalIgnoreCase))
                    return 1;
                return 2;
            }

            if (key.IndexOf(".internal.", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0;
            if (key.IndexOf(".plugin.", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1;
            return 2;
        }

        private static int ConfigSourceOrder(string key)
        {
            return TryGetSyncedConfigMetadata(key, out _, out int index, out _) ? index : int.MaxValue;
        }

        internal static List<TradeableItem> DeserializeItems(string content, string source)
        {
            if (string.IsNullOrWhiteSpace(content))
                return new List<TradeableItem>();

            try
            {
                string extension = Path.GetExtension(source);
                if (string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                    return JsonConvert.DeserializeObject<List<TradeableItem>>(content) ?? new List<TradeableItem>();

                if (string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase))
                    return yamlDeserializer.Deserialize<List<TradeableItem>>(content) ?? new List<TradeableItem>();

                if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
                    return DeserializeCsvItems(content, source);

                string trimmed = content.TrimStart();
                return trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal)
                    ? JsonConvert.DeserializeObject<List<TradeableItem>>(content) ?? new List<TradeableItem>()
                    : yamlDeserializer.Deserialize<List<TradeableItem>>(content) ?? new List<TradeableItem>();
            }
            catch (Exception exception)
            {
                LogWarning($"Error parsing item config '{source}': {exception.Message}");
                return null;
            }
        }

        private static List<TradeableItem> DeserializeCsvItems(string content, string source)
        {
            List<List<string>> rows = ParseCsvRecords(content);
            if (rows.Count == 0)
                return new List<TradeableItem>();

            string[] headers = rows[0]
                .Select((header, index) => index == 0 ? (header ?? string.Empty).TrimStart('\uFEFF').Trim() : (header ?? string.Empty).Trim())
                .ToArray();

            HashSet<string> supportedHeaders = new HashSet<string>(new[]
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
            }, StringComparer.OrdinalIgnoreCase);

            if (!headers.Any(header => string.Equals(header, nameof(TradeableItem.prefab), StringComparison.OrdinalIgnoreCase)))
                throw new FormatException("CSV item configs must contain the 'prefab' header.");

            if (headers.Where(header => !string.IsNullOrWhiteSpace(header))
                .GroupBy(header => header, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
                throw new FormatException("CSV contains duplicate column headers.");

            string[] unknownHeaders = headers.Where(header => !string.IsNullOrWhiteSpace(header) && !supportedHeaders.Contains(header)).ToArray();
            if (unknownHeaders.Length > 0)
                LogWarning($"Ignored unknown CSV headers in '{source}': {string.Join(", ", unknownHeaders)}");

            List<TradeableItem> result = new List<TradeableItem>();
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.All(string.IsNullOrWhiteSpace))
                    continue;

                if (row.Skip(headers.Length).Any(value => !string.IsNullOrWhiteSpace(value)))
                    throw new FormatException($"CSV row {rowIndex + 1} has more values than its header.");

                TradeableItem item = new TradeableItem();
                for (int column = 0; column < headers.Length; column++)
                {
                    string header = headers[column];
                    if (string.IsNullOrWhiteSpace(header) || !supportedHeaders.Contains(header))
                        continue;

                    string value = column < row.Count ? row[column] ?? string.Empty : string.Empty;
                    if (string.IsNullOrEmpty(value))
                        continue;

                    if (string.Equals(header, nameof(TradeableItem.prefab), StringComparison.OrdinalIgnoreCase))
                        item.prefab = value.Trim();
                    else if (string.Equals(header, nameof(TradeableItem.stack), StringComparison.OrdinalIgnoreCase))
                        item.stack = ParseCsvInt(value, source, rowIndex + 1, header);
                    else if (string.Equals(header, nameof(TradeableItem.price), StringComparison.OrdinalIgnoreCase))
                        item.price = ParseCsvInt(value, source, rowIndex + 1, header);
                    else if (string.Equals(header, nameof(TradeableItem.quality), StringComparison.OrdinalIgnoreCase))
                        item.quality = ParseCsvInt(value, source, rowIndex + 1, header);
                    else if (string.Equals(header, nameof(TradeableItem.currency), StringComparison.OrdinalIgnoreCase))
                        item.currency = value.Trim();
                    else if (string.Equals(header, nameof(TradeableItem.requiredGlobalKey), StringComparison.OrdinalIgnoreCase))
                        item.requiredGlobalKey = value.Trim();
                    else if (string.Equals(header, nameof(TradeableItem.notRequiredGlobalKey), StringComparison.OrdinalIgnoreCase))
                        item.notRequiredGlobalKey = value.Trim();
                    else if (string.Equals(header, nameof(TradeableItem.requiredPlayerKey), StringComparison.OrdinalIgnoreCase))
                        item.requiredPlayerKey = value.Trim();
                    else if (string.Equals(header, nameof(TradeableItem.notRequiredPlayerKey), StringComparison.OrdinalIgnoreCase))
                        item.notRequiredPlayerKey = value.Trim();
                }

                if (string.IsNullOrWhiteSpace(item.prefab))
                {
                    LogWarning($"Ignored CSV row {rowIndex + 1} in '{source}' because prefab is empty.");
                    continue;
                }

                result.Add(item);
            }

            return result;
        }

        private static int ParseCsvInt(string value, string source, int row, string header)
        {
            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                return result;

            throw new FormatException($"Invalid integer '{value}' in row {row}, column '{header}' of '{source}'.");
        }

        private static IEnumerator AddCommonValuableItems(Dictionary<string, List<TradeableItem>> destination)
        {
            yield return new WaitUntil(delegate { return ObjectDB.instance != null; });

            string listKey = CommonListKey(ItemsListType.Sell);
            if (!destination.TryGetValue(listKey, out List<TradeableItem> commonItems))
            {
                commonItems = new List<TradeableItem>();
                destination[listKey] = commonItems;
            }

            HashSet<string> existingPrefabs = new HashSet<string>(
                commonItems.Where(item => item != null && !string.IsNullOrWhiteSpace(item.prefab)).Select(item => item.prefab),
                StringComparer.OrdinalIgnoreCase);
            int initialCount = commonItems.Count;

            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (prefab == null ||
                    string.Equals(prefab.name, CoinsPatches.itemNameCoins, StringComparison.OrdinalIgnoreCase) ||
                    !prefab.TryGetComponent(out ItemDrop itemDrop) ||
                    itemDrop.m_itemData.m_shared.m_value <= 0 ||
                    !existingPrefabs.Add(prefab.name))
                    continue;

                commonItems.Add(new TradeableItem
                {
                    prefab = prefab.name,
                    price = itemDrop.m_itemData.m_shared.m_value,
                    automatic = true
                });
            }

            LogInfo($"Loaded {commonItems.Count - initialCount} common valuable items from ObjectDB");
        }

        internal static string TraderName(Trader trader)
        {
            return trader == null ? string.Empty : TraderName(Utils.GetPrefabName(trader.gameObject));
        }

        internal static string TraderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            return name.Trim().ToLowerInvariant().Replace("$npc_", string.Empty).Replace("npc_", string.Empty);
        }

        public static string TraderListKey(Trader trader, ItemsListType type)
        {
            return TraderListKey(TraderName(trader), type);
        }

        public static string TraderListKey(string name, ItemsListType type)
        {
            return TraderName(name) + "." + type;
        }

        public static string CommonListKey(ItemsListType type)
        {
            return TraderListKey("common", type);
        }

        private static List<List<string>> ParseCsvRecords(string content)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            bool closedQuote = false;
            content = (content ?? string.Empty).TrimStart('\uFEFF');
            for (int index = 0; index < content.Length; index++)
            {
                char current = content[index];
                if (quoted)
                {
                    if (current != '"')
                        field.Append(current);
                    else if (index + 1 < content.Length && content[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        quoted = false;
                        closedQuote = true;
                    }
                    continue;
                }
                if (current == ',' || current == '\r' || current == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    closedQuote = false;
                    if (current != ',')
                    {
                        if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                            index++;
                        rows.Add(row);
                        row = new List<string>();
                    }
                }
                else if (current == '"' && field.Length == 0 && !closedQuote)
                    quoted = true;
                else if (closedQuote || current == '"')
                    throw new FormatException("CSV contains unexpected text outside a quoted field.");
                else
                    field.Append(current);
            }
            if (quoted)
                throw new FormatException("CSV contains an unterminated quoted field.");
            if (field.Length > 0 || closedQuote || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
