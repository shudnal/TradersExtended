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
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("_shudnal.ConditionalConfigSync", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Azumatt.AzuExtendedPlayerInventory", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("shudnal.ExtraSlots", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("randyknapp.mods.auga")]
    public partial class TradersExtended : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.TradersExtended";
        public const string pluginName = "Traders Extended";
        public const string pluginVersion = "2.0.0";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID)
        {
            DisplayName = pluginName,
            CurrentVersion = pluginVersion,
            MinimumRequiredVersion = pluginVersion,
            ModRequired = true
        };

        public static ManualLogSource logger;
        internal static TradersExtended instance;

        private static ConfigEntry<bool> loggingEnabled;
        private static ConfigEntry<bool> configLocked;

        public static ConfigEntry<bool> checkForDiscovery;
        internal static ConfigEntry<string> checkForDiscoveryIgnoreItems;

        public static ConfigEntry<bool> traderRepair;
        public static ConfigEntry<int> traderRepairCost;
        public static ConfigEntry<string> traderRepairCurrency;
        internal static ConfigEntry<string> tradersToRepairWeapons;
        internal static ConfigEntry<string> tradersToRepairArmor;

        public static ConfigEntry<bool> traderUseCoins;
        public static ConfigEntry<bool> traderUseFlexiblePricing;
        public static ConfigEntry<int> traderCoinsMinimumAmount;
        public static ConfigEntry<int> traderCoinsIncreaseAmount;
        public static ConfigEntry<int> traderCoinsDecreaseAmount;
        public static ConfigEntry<int> traderCoinsMaximumAmount;
        public static ConfigEntry<float> traderDiscount;
        public static ConfigEntry<float> traderMarkup;
        public static ConfigEntry<int> traderCoinsReplenishmentRate;
        public static ConfigEntry<bool> traderCoinsSendReplenishmentMessage;
        public static ConfigEntry<string> traderCurrencyOverrides;

        public static ConfigEntry<bool> coinsPatch;
        public static ConfigEntry<float> coinsWeight;
        public static ConfigEntry<int> coinsStackSize;

        public static ConfigEntry<string> tradersCustomPrefabs;
        public static ConfigEntry<bool> disableVanillaItems;
        public static ConfigEntry<bool> disableOtherModsItems;
        public static ConfigEntry<float> qualityMultiplier;
        public static ConfigEntry<bool> hideEquippedAndHotbarItems;
        public static ConfigEntry<bool> addCommonValuableItemsToSellList;
        public static ConfigEntry<Vector2> fixedStoreGuiPosition;

        public static ConfigEntry<bool> enableBuyBack;
        public static ConfigEntry<int> buybackLifetime;
        public static ConfigEntry<Color> colorBuybackNormal;
        public static ConfigEntry<Color> colorBuybackHighlighted;
        public static ConfigEntry<Color> colorBuybackText;

        public static ConfigEntry<string> epicLootShiftedTraders;

        public static readonly Dictionary<string, List<TradeableItem>> tradeableItems = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, List<TradeableItem>> sellableItems = new Dictionary<string, List<TradeableItem>>(StringComparer.OrdinalIgnoreCase);

        private static readonly CustomSyncedValue<Dictionary<string, string>> itemConfigs =
            new CustomSyncedValue<Dictionary<string, string>>(configSync, "Item configs", new Dictionary<string, string>());

        internal static readonly CustomSyncedValue<Dictionary<string, string>> traderConfigFiles =
            new CustomSyncedValue<Dictionary<string, string>>(configSync, "Personal trader configs", new Dictionary<string, string>());

        internal static readonly IDeserializer yamlDeserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        private static DirectoryInfo configDirectory;
        private static readonly List<FileSystemWatcher> configWatchers = new List<FileSystemWatcher>();
        private static Coroutine configReloadCoroutine;
        private static Coroutine configLoadCoroutine;

        public static HashSet<string> _ignoreItemDiscovery = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> _tradersToRepairWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> _tradersToRepairArmor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public enum ItemsListType
        {
            Buy,
            Sell
        }

        private void Awake()
        {
            instance = this;
            logger = Logger;

            configDirectory = new DirectoryInfo(Paths.ConfigPath);

            ConfigInit();
            configSync.AddLockingConfigEntry(configLocked);
            itemConfigs.ValueChanged += StartConfigLoad;
            traderConfigFiles.ValueChanged += TraderConfigManager.LoadSyncedConfigs;

            EpicLootCompat.CheckForCompatibility();

            harmony.PatchAll();
            Game.isModded = true;
        }

        private void Start()
        {
            FillConfigLists();
            TraderCurrency.RebuildOverrides();
            SetupConfigWatcher();
        }

        private void Update()
        {
            AmountDialog.Update();
        }

        private void OnDestroy()
        {
            DisposeConfigWatchers();
            if (configReloadCoroutine != null)
                StopCoroutine(configReloadCoroutine);
            if (configLoadCoroutine != null)
                StopCoroutine(configLoadCoroutine);

            itemConfigs.ValueChanged -= StartConfigLoad;
            traderConfigFiles.ValueChanged -= TraderConfigManager.LoadSyncedConfigs;
            BuybackManager.ResetCache();
            Config.Save();
            harmony.UnpatchSelf();
            instance = null;
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled != null && loggingEnabled.Value && logger != null)
                logger.LogInfo(data);
        }

        public static void LogWarning(object data)
        {
            if (logger != null)
                logger.LogWarning(data);
        }

        private void ConfigInit()
        {
            configLocked = serverConfig("General", "Lock Configuration", true, "Configuration is locked and can be changed by server administrators only.");
            loggingEnabled = config("General", "Logging enabled", false, "Enable diagnostic logging. [Not synchronized with server]", false);

            checkForDiscovery = config("Item discovery", "Sell only discovered items", true, "A trader will not sell items that the buyer has not discovered.");
            checkForDiscoveryIgnoreItems = config("Item discovery", "Undiscovered items list to sell", "", "Comma-separated prefab names that bypass the discovery check. Vanilla trader items are included by default.");
            checkForDiscoveryIgnoreItems.SettingChanged += delegate { FillConfigLists(); };

            coinsPatch = config("Item coins", "Change values", false, "Change properties of the Coins item.");
            coinsWeight = config("Item coins", "Coins weight", 0f, "Weight of one coin.");
            coinsStackSize = config("Item coins", "Coins stack size", 2000, "Maximum coin stack size.");

            traderRepair = config("Trader repair", "Traders can repair items", true, "Allow configured traders to repair items.");
            tradersToRepairWeapons = config("Trader repair", "Traders capable to repair weapons", "Haldor", "Comma-separated trader prefab names that can repair weapons.");
            tradersToRepairArmor = config("Trader repair", "Traders capable to repair armor", "Hildir", "Comma-separated trader prefab names that can repair armor.");
            traderRepairCost = config("Trader repair", "Traders repair cost", 2, "Repair cost.");
            traderRepairCurrency = config("Trader repair", "Repair currency", "Coins", "Item prefab used to pay for repairs.");
            tradersToRepairWeapons.SettingChanged += delegate { FillConfigLists(); };
            tradersToRepairArmor.SettingChanged += delegate { FillConfigLists(); };

            traderUseCoins = config("Trader coins", "Traders use coins", true, "Traders have a limited daily replenished balance.");
            traderUseFlexiblePricing = config("Trader coins", "Traders use flexible pricing", true, "Adjust buy and sell prices according to the trader's current balance.");
            traderCurrencyOverrides = config("Trader currency", "Trader currency overrides", "", "Comma-separated TraderPrefab:CurrencyPrefab pairs. The configured item becomes the default currency for all transactions with that trader. An item config entry's currency field overrides it for that buy or sell entry only.");

            traderCoinsMinimumAmount = config("Trader coins pricing", "Amount of coins after replenishment minimum", 2000, "Minimum balance after replenishment.");
            traderCoinsIncreaseAmount = config("Trader coins pricing", "Amount of coins replenished daily", 1000, "Amount added to the current balance until the maximum is reached.");
            traderCoinsDecreaseAmount = config("Trader coins pricing", "Amount of coins removed daily", 0, "Amount removed from balances above the maximum.");
            traderCoinsMaximumAmount = config("Trader coins pricing", "Amount of coins after replenishment maximum", 6000, "Maximum replenished balance.");
            traderDiscount = config("Trader coins pricing", "Trader discount", 0.7f, "Buy-price factor at the maximum trader balance.");
            traderMarkup = config("Trader coins pricing", "Trader markup", 1.5f, "Buy-price factor at zero trader balance.");
            traderCoinsReplenishmentRate = config("Trader coins pricing", "Trader coins replenishment rate in days", 1, "Number of days between balance updates.");
            traderCoinsSendReplenishmentMessage = config("Trader coins pricing", "Send replenishment message in the morning", true, "Show a message when trader balances are updated.");

            tradersCustomPrefabs = config("Misc", "Custom traders prefab names", "", "Comma-separated, case-sensitive custom trader prefab names whose balances should be managed.");
            disableVanillaItems = config("Misc", "Disable vanilla items", false, "Remove vanilla items from trader buy lists. Compatibility depends on the custom trader implementation.");
            qualityMultiplier = config("Misc", "Quality multiplier", 0.0f, "Additional price factor applied for each item quality level above one.");
            hideEquippedAndHotbarItems = config("Misc", "Hide equipped and hotbar items", true, "Hide equipped items and equippable items in the first inventory row from the sell list.");
            addCommonValuableItemsToSellList = config("Misc", "Add common valuable items to sell list", true, "Add ObjectDB items with a positive vanilla value to the common sell list.");
            fixedStoreGuiPosition = config("Misc", "Fixed position for Store GUI", Vector2.zero, "Use an absolute Store GUI position when this value is not zero.");
            disableOtherModsItems = config("Misc", "Disable other mods items", false, "Remove all buy-list items added by other mods before Traders Extended adds its own lists.");

            enableBuyBack = config("Trader buyback", "Enable buyback for last item sold", true, "Add the last item sold to this trader to the beginning of the buy list.");
            buybackLifetime = config("Trader buyback", "Buyback lifetime in world seconds", 1800, "How long a saved buyback remains available in world-time seconds. Set to 0 to disable expiration.");
            colorBuybackNormal = config("Trader buyback", "Item background color", new Color(0f, 0.42f, 0.42f), "Buyback item background color.");
            colorBuybackHighlighted = config("Trader buyback", "Item highlighted color", new Color(0.25f, 0.62f, 0.62f), "Buyback item highlighted color.");
            colorBuybackText = config("Trader buyback", "Item font color", new Color(1f, 0.81f, 0f), "Buyback item name color.");

            epicLootShiftedTraders = config("EpicLoot compatibility", "Traders with shifted Store GUI position", "Haldor", "Comma-separated trader prefab names whose Store GUI should be shifted when EpicLoot Adventure Mode is active.");

            InitCommands();
        }

        private ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> entry = configSync.AddConfigEntry(
                Config,
                group,
                name,
                defaultValue,
                description,
                syncMode: ConfigSyncMode.Conditional,
                serverControlledByDefault: synchronizedSetting).SourceConfig;

            if (!string.Equals(group, "General", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(group, "Item coins", StringComparison.OrdinalIgnoreCase))
                entry.SettingChanged += delegate { TraderConfigManager.InvalidateAndRefresh(); };

            return entry;
        }

        private ConfigEntry<T> serverConfig<T>(string group, string name, T defaultValue, ConfigDescription description)
        {
            return configSync.AddConfigEntry(
                Config,
                group,
                name,
                defaultValue,
                description,
                syncMode: ConfigSyncMode.AlwaysServerControlled,
                serverControlledByDefault: true).SourceConfig;
        }

        private ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true)
        {
            return config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);
        }

        private ConfigEntry<T> serverConfig<T>(string group, string name, T defaultValue, string description)
        {
            return serverConfig(group, name, defaultValue, new ConfigDescription(description));
        }

        public static void InitCommands()
        {
            new Terminal.ConsoleCommand("tradersextended", "save [json|yml|csv] - Save the full item list as JSON by default, itemlist - Save the filtered item list as CSV", delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length >= 2)
                {
                    string action = args[1];
                    if (string.Equals(action, "save", StringComparison.OrdinalIgnoreCase))
                        SaveFromObjectDB(args.Context, args.Length >= 3 ? args[2] : "json");
                    else if (string.Equals(action, "itemlist", StringComparison.OrdinalIgnoreCase))
                        ExportItemListFromObjectDB(args.Context);
                    else
                        args.Context.AddString("Actions: save [json|yml|csv] - Save the full item list as JSON by default, itemlist - Save the filtered item list as CSV");
                }
                else
                {
                    args.Context.AddString("Actions: save [json|yml|csv] - Save the full item list as JSON by default, itemlist - Save the filtered item list as CSV");
                }
            }, false, false, false, false, false, delegate
            {
                return new List<string> { "save", "itemlist" };
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

        public static void FillConfigLists()
        {
            _ignoreItemDiscovery = new HashSet<string>(
                (checkForDiscoveryIgnoreItems?.Value ?? string.Empty).Split(',').Select(value => value.Trim()).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            _tradersToRepairWeapons = new HashSet<string>(
                (tradersToRepairWeapons?.Value ?? string.Empty).Split(',').Select(value => TraderName(value.Trim())).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            _tradersToRepairArmor = new HashSet<string>(
                (tradersToRepairArmor?.Value ?? string.Empty).Split(',').Select(value => TraderName(value.Trim())).Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
        }

        public static bool IgnoreItemDiscovery(string prefabName)
        {
            return !string.IsNullOrWhiteSpace(prefabName) && _ignoreItemDiscovery.Contains(prefabName);
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

            FileSystemWatcher watcher = new FileSystemWatcher(directory.FullName, pluginID + ".*")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                SynchronizingObject = ThreadingHelper.SynchronizingObject,
                EnableRaisingEvents = true
            };

            watcher.Changed += OnConfigFileChanged;
            watcher.Created += OnConfigFileChanged;
            watcher.Deleted += OnConfigFileChanged;
            watcher.Renamed += OnConfigFileChanged;
            configWatchers.Add(watcher);
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

            bool supported = IsSupportedConfigExtension(Path.GetExtension(args.FullPath));
            if (!supported && args is RenamedEventArgs renamed)
                supported = IsSupportedConfigExtension(Path.GetExtension(renamed.OldFullPath));
            if (!supported)
                return;

            if (configReloadCoroutine != null)
                instance.StopCoroutine(configReloadCoroutine);
            configReloadCoroutine = instance.StartCoroutine(ReloadConfigsAfterFileWrite());
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

        private static void ReadConfigs()
        {
            Dictionary<string, string> localItemConfigs = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, PersonalTraderConfigSource> localTraderConfigs =
                new Dictionary<string, PersonalTraderConfigSource>(StringComparer.OrdinalIgnoreCase);
            int index = 0;

            index = AddEmbeddedConfigs(localItemConfigs, localTraderConfigs, index);
            AddDirectoryConfigs(localItemConfigs, localTraderConfigs, configDirectory, "config", index);

            Dictionary<string, string> synchronizedTraderConfigs = localTraderConfigs.ToDictionary(
                pair => BuildSyncedTraderConfigKey(pair.Key, pair.Value.FileName),
                pair => pair.Value.Content,
                StringComparer.Ordinal);

            traderConfigFiles.AssignLocalValueAndNotify(synchronizedTraderConfigs);
            itemConfigs.AssignLocalValueAndNotify(localItemConfigs);
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
                files = directory.GetFiles(pluginID + ".*", SearchOption.AllDirectories)
                    .Where(file => IsSupportedConfigExtension(file.Extension))
                    .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch (Exception exception)
            {
                LogWarning($"Could not enumerate config files in '{directory.FullName}': {exception.Message}");
                return index;
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
                    LogWarning($"Error reading item config '{file.FullName}': {exception.Message}");
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
                    LogWarning($"Error reading personal trader config '{selected.FullName}': {exception.Message}");
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
                    LogWarning($"Error reading embedded item config '{resource}': {exception.Message}");
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
                    LogWarning($"Error reading embedded personal trader config '{selected.Resource}': {exception.Message}");
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

        private static bool TryParseConfigFileName(string fileName, out string trader, out ItemsListType listType)
        {
            trader = string.Empty;
            listType = ItemsListType.Buy;

            string extension = Path.GetExtension(fileName);
            if (!IsSupportedItemConfigExtension(extension))
                return false;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string prefix = pluginID + ".";
            if (!withoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return TryParseConfigName(withoutExtension.Substring(prefix.Length), out trader, out listType);
        }

        private static bool TryParseConfigName(string configName, out string trader, out ItemsListType listType)
        {
            trader = string.Empty;
            listType = ItemsListType.Buy;

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

                listType = parsedListType;
                return true;
            }

            return false;
        }

        private static bool TryParseTraderConfigFileName(string fileName, out string trader)
        {
            trader = string.Empty;
            if (!IsSupportedTraderConfigExtension(Path.GetExtension(fileName)))
                return false;

            string withoutExtension = Path.GetFileNameWithoutExtension(fileName);
            string prefix = pluginID + ".";
            if (!withoutExtension.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;

            return TryParseTraderConfigName(withoutExtension.Substring(prefix.Length), out trader);
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

        private static bool IsSupportedConfigExtension(string extension)
        {
            return IsSupportedItemConfigExtension(extension) || IsSupportedTraderConfigExtension(extension);
        }

        private static bool IsSupportedItemConfigExtension(string extension)
        {
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedTraderConfigExtension(string extension)
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

            tradeableItems.Clear();
            sellableItems.Clear();

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
                    continue;

                string listKey = TraderListKey(trader, listType);
                Dictionary<string, List<TradeableItem>> destination = listType == ItemsListType.Buy ? tradeableItems : sellableItems;
                if (!destination.TryGetValue(listKey, out List<TradeableItem> currentItems))
                {
                    currentItems = new List<TradeableItem>();
                    destination.Add(listKey, currentItems);
                }

                currentItems.AddRange(items.Where(item => item != null));
                LogInfo($"Loaded {items.Count} {listType.ToString().ToLowerInvariant()} item entries from {fileName}");
            }

            yield return AddCommonValuableItems();

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

        private static List<TradeableItem> DeserializeItems(string content, string source)
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
            List<List<string>> rows = ParseCsv(content);
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

            string[] unknownHeaders = headers.Where(header => !string.IsNullOrWhiteSpace(header) && !supportedHeaders.Contains(header)).ToArray();
            if (unknownHeaders.Length > 0)
                LogWarning($"Ignored unknown CSV headers in '{source}': {string.Join(", ", unknownHeaders)}");

            List<TradeableItem> result = new List<TradeableItem>();
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> row = rows[rowIndex];
                if (row.All(string.IsNullOrWhiteSpace))
                    continue;

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

        private static List<List<string>> ParseCsv(string content)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder field = new StringBuilder();
            bool quoted = false;

            for (int index = 0; index < content.Length; index++)
            {
                char current = content[index];
                if (quoted)
                {
                    if (current == '"')
                    {
                        if (index + 1 < content.Length && content[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(current);
                    }

                    continue;
                }

                if (current == '"' && field.Length == 0)
                {
                    quoted = true;
                }
                else if (current == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (current == '\r' || current == '\n')
                {
                    if (current == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                        index++;

                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                }
                else
                {
                    field.Append(current);
                }
            }

            if (quoted)
                throw new FormatException("CSV contains an unterminated quoted field.");

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }

        private static IEnumerator AddCommonValuableItems()
        {
            yield return new WaitUntil(delegate { return ObjectDB.instance != null; });

            string listKey = CommonListKey(ItemsListType.Sell);
            if (!sellableItems.TryGetValue(listKey, out List<TradeableItem> commonItems))
            {
                commonItems = new List<TradeableItem>();
                sellableItems[listKey] = commonItems;
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
    }
}
