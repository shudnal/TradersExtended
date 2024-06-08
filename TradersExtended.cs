using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using ServerSync;
using System.IO;
using System;
using Newtonsoft.Json;
using System.Linq;

namespace TradersExtended
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("Azumatt.AzuExtendedPlayerInventory", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("randyknapp.mods.auga")]
    public partial class TradersExtended : BaseUnityPlugin
    {
        private const string pluginID = "shudnal.TradersExtended";
        private const string pluginName = "Traders Extended";
        private const string pluginVersion = "1.2.3";

        private Harmony harmony;

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        public static ManualLogSource logger;

        internal static TradersExtended instance;

        public static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<bool> loggingEnabled;
        private static ConfigEntry<bool> configLocked;

        public static ConfigEntry<bool> checkForDiscovery;
        private static ConfigEntry<string> checkForDiscoveryIgnoreItems;

        public static ConfigEntry<bool> traderRepair;
        public static ConfigEntry<int> traderRepairCost;
        private static ConfigEntry<string> tradersToRepairWeapons;
        private static ConfigEntry<string> tradersToRepairArmor;

        public static ConfigEntry<bool> traderUseCoins;
        public static ConfigEntry<bool> traderUseFlexiblePricing;
        public static ConfigEntry<int> traderCoinsMinimumAmount;
        public static ConfigEntry<int> traderCoinsIncreaseAmount;
        public static ConfigEntry<int> traderCoinsMaximumAmount;
        public static ConfigEntry<float> traderDiscount;
        public static ConfigEntry<float> traderMarkup;
        public static ConfigEntry<int> traderCoinsReplenishmentRate;

        public static ConfigEntry<bool> coinsPatch;
        public static ConfigEntry<float> coinsWeight;
        public static ConfigEntry<int> coinsStackSize;

        public static ConfigEntry<string> tradersCustomPrefabs;
        public static ConfigEntry<bool> disableVanillaItems;
        public static ConfigEntry<float> qualityMultiplier;
        public static ConfigEntry<bool> hideEquippedAndHotbarItems;
        public static ConfigEntry<bool> addCommonValuableItemsToSellList;
        public static ConfigEntry<Vector2> fixedStoreGuiPosition;

        public static ConfigEntry<bool> enableBuyBack;
        public static ConfigEntry<Color> colorBuybackNormal;
        public static ConfigEntry<Color> colorBuybackHighlighted;
        public static ConfigEntry<Color> colorBuybackText;

        public static readonly Dictionary<string, List<TradeableItem>> tradeableItems = new Dictionary<string, List<TradeableItem>>();
        public static readonly Dictionary<string, List<TradeableItem>> sellableItems = new Dictionary<string, List<TradeableItem>>();

        private static readonly CustomSyncedValue<Dictionary<string, string>> configsJSON = new CustomSyncedValue<Dictionary<string, string>>(configSync, "JSON configs", new Dictionary<string, string>());

        private static DirectoryInfo pluginDirectory;
        private static DirectoryInfo configDirectory;

        public static Component epicLootPlugin;

        public static HashSet<string> _ignoreItemDiscovery = new HashSet<string>();
        public static HashSet<string> _tradersToRepairWeapons = new HashSet<string>();
        public static HashSet<string> _tradersToRepairArmor = new HashSet<string>();

        public enum ItemsListType
        {
            Buy,
            Sell
        }

        void Awake()
        {
            harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginID);

            instance = this;

            logger = Logger;

            pluginDirectory = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent;
            configDirectory = new DirectoryInfo(Paths.ConfigPath);

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            configsJSON.ValueChanged += new Action(LoadConfigs);

            epicLootPlugin = GetComponent("EpicLoot");

            Game.isModded = true;
        }

        void Update()
        {
            AmountDialog.Update();
        }

        void OnDestroy()
        {
            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

        public static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }

        private void ConfigInit()
        {
            config("General", "NexusID", 2509, "Nexus mod ID for updates", false);
            modEnabled = config("General", "Enabled", defaultValue: true, "Enable this mod. Reload the game to take effect.");
            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging. [Not Synced with Server]", false);

            checkForDiscovery = config("Item discovery", "Sell only discovered items", defaultValue: true, "Trader will not sell items had not discovered by a buyer.");
            checkForDiscoveryIgnoreItems = config("Item discovery", "Undiscovered items list to sell", defaultValue: "", "Trader will sell items from that list without check for discovery. Vanilla items are included by default.");

            checkForDiscoveryIgnoreItems.SettingChanged += (sender, args) => FillConfigLists();

            coinsPatch = config("Item coins", "Change values", defaultValue: false, "Change properties of coins item");
            coinsWeight = config("Item coins", "Coins weight", defaultValue: 0f, "Weight of single coin");
            coinsStackSize = config("Item coins", "Coins stack size", defaultValue: 2000, "Max size of coins stack");

            traderRepair = config("Trader repair", "Traders can repair items", defaultValue: true, "Traders will have an ability to repair items");
            tradersToRepairWeapons = config("Trader repair", "Traders capable to repair weapons", defaultValue: "$npc_haldor", "Traders that have an ability to repair weapons");
            tradersToRepairArmor = config("Trader repair", "Traders capable to repair armor", defaultValue: "$npc_hildir", "Traders that have an ability to repair armor");
            traderRepairCost = config("Trader repair", "Traders repair cost", defaultValue: 2, "Cost of repair in gold");

            tradersToRepairWeapons.SettingChanged += (sender, args) => FillConfigLists();
            tradersToRepairArmor.SettingChanged += (sender, args) => FillConfigLists();

            traderUseCoins = config("Trader coins", "Traders use coins", defaultValue: true, "Traders will have an limited daily refilled amount of coins");
            traderUseFlexiblePricing = config("Trader coins", "Traders use flexible pricing", defaultValue: true, "Traders will give a discount when their amount of coins is more than minimum or will set a markup when their amount of coins is less than minimum. Amount changes gradually.");

            traderCoinsMinimumAmount = config("Trader coins pricing", "Amount of coins after replenishment minimum", defaultValue: 2000, "Minimum amount of coins trader will have after replenishment.");
            traderCoinsIncreaseAmount = config("Trader coins pricing", "Amount of coins replenished daily", defaultValue: 1000, "Amount of coins added to current amount until maximum is reached");
            traderCoinsMaximumAmount = config("Trader coins pricing", "Amount of coins after replenishment maximum", defaultValue: 6000, "Maximum amount of coins for replenishments to stop.");
            traderDiscount = config("Trader coins pricing", "Trader discount", defaultValue: 0.7f, "Discount for items to buy from trader when current amount of coins is more than maximum replenishment amount.");
            traderMarkup = config("Trader coins pricing", "Trader markup", defaultValue: 1.5f, "Markup for items to buy from trader when current amount of coins is less than minimum replenishment amount.");
            traderCoinsReplenishmentRate = config("Trader coins pricing", "Trader coins replenishment rate in days", defaultValue: 1, "Amount of coins is updated at morning");

            tradersCustomPrefabs = config("Misc", "Custom traders prefab names", defaultValue: "", "List of custom prefab names of Trader added by mods to control coins. Prefab name, case sensitive, comma separated");
            disableVanillaItems = config("Misc", "Disable vanilla items", defaultValue: false, "Disable vanilla items on traders. Custom traders could or could not work depending on their implementation.");
            qualityMultiplier = config("Misc", "Quality multiplier", defaultValue: 0.0f, "Quality multiplier for price. Each level of additional quality level adds that percent of price.");
            hideEquippedAndHotbarItems = config("Misc", "Hide equipped and hotbar items", defaultValue: true, "Equippable items from first row of inventory and all items currently equipped will not be shown at the sell list.");
            addCommonValuableItemsToSellList = config("Misc", "Add common valuable items to sell list", defaultValue: true, "Add common valuable items to all traders sell list.");
            fixedStoreGuiPosition = config("Misc", "Fixed position for Store GUI", defaultValue: Vector2.zero, "If set then Store GUI will take that absolute position.");

            fixedStoreGuiPosition.SettingChanged += (sender, args) => StorePanel.SetStoreGuiAbsolutePosition();

            enableBuyBack = config("Trader buyback", "Enable buyback for last item sold", defaultValue: true, "First item to buy will be the last item you have recently sold.");
            colorBuybackNormal = config("Trader buyback", "Item background color", defaultValue: new Color(0f, 0.42f, 0.42f), "Color of buyback item background.");
            colorBuybackHighlighted = config("Trader buyback", "Item highlighted color", defaultValue: new Color(0.25f, 0.62f, 0.62f), "Color of highlighted buyback item.");
            colorBuybackText = config("Trader buyback", "Item font color", defaultValue: new Color(1f, 0.81f, 0f), "Color of buyback item name.");

            InitCommands();
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        public static void InitCommands()
        {
            new Terminal.ConsoleCommand("tradersextended", "[action]", delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length >= 2)
                {
                    string action = args.FullLine.Substring(args[0].Length + 1);
                    if (action == "save")
                    {
                        SaveFromObjectDB(args.Context);
                    }
                }
                else
                {
                    args.Context.AddString("Syntax: tradersextended [action]");
                }
            }, isCheat: false, isNetwork: false, onlyServer: false, isSecret: false, allowInDevBuild: false, () => new List<string>() { "save  -  Save every item from ObjectDB into file ObjectDB.list.json next to dll" }, alwaysRefreshTabOptions: true, remoteCommand: false);
            
            new Terminal.ConsoleCommand("settradercoins", "[trader] [amount]", delegate (Terminal.ConsoleEventArgs args)
            {
                if (args.Length <= 1)
                    return false;

                TraderCoins.SetTraderCoins(args[1].GetStableHashCode(), args.TryParameterInt(2, traderCoinsMinimumAmount.Value));

                return true;
            }, isCheat: true, isNetwork: false, onlyServer: true, isSecret: false, allowInDevBuild: false, () => TraderCoins.GetTraderPrefabs(), alwaysRefreshTabOptions: true, remoteCommand: false, onlyAdmin: true);
        }

        public static void SaveFromObjectDB(Terminal context)
        {
            List<TradeableItem> allItems = new List<TradeableItem>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (!prefab.TryGetComponent<ItemDrop>(out ItemDrop itemDrop))
                    continue;

                allItems.Add(new TradeableItem()
                {
                    prefab = prefab.name,
                    price = Math.Max(itemDrop.m_itemData.m_shared.m_value, 1)
                });
            }

            string JSON = JsonConvert.SerializeObject(allItems, Formatting.Indented);

            string filename = Path.Combine(pluginDirectory.FullName, "ObjectDB.list.json");

            File.WriteAllText(filename, JSON);

            context.AddString($"Saved {allItems.Count} items to {filename}");
        }

        public static void FillConfigLists()
        {
            _ignoreItemDiscovery = new HashSet<string>(checkForDiscoveryIgnoreItems.Value.Split(',').Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList());
            _tradersToRepairWeapons = new HashSet<string>(tradersToRepairWeapons.Value.Split(',').Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList());
            _tradersToRepairArmor = new HashSet<string>(tradersToRepairArmor.Value.Split(',').Select(p => p.Trim().ToLower()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList());
        }

        public static bool IgnoreItemDiscovery(string prefabName)
        {
            return _ignoreItemDiscovery.Contains(prefabName);
        }

        public static void SetupConfigWatcher()
        {
            string filter = $"{pluginID}.*.json";

            FileSystemWatcher fileSystemWatcherPlugin = new FileSystemWatcher(pluginDirectory.FullName, filter);
            fileSystemWatcherPlugin.Changed += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcherPlugin.Created += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcherPlugin.Renamed += new RenamedEventHandler(ReadConfigs);
            fileSystemWatcherPlugin.Deleted += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcherPlugin.IncludeSubdirectories = true;
            fileSystemWatcherPlugin.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcherPlugin.EnableRaisingEvents = true;

            FileSystemWatcher fileSystemWatcherConfig = new FileSystemWatcher(configDirectory.FullName, filter);
            fileSystemWatcherConfig.Changed += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcherConfig.Created += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcherConfig.Renamed += new RenamedEventHandler(ReadConfigs);
            fileSystemWatcherConfig.Deleted += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcherConfig.IncludeSubdirectories = true;
            fileSystemWatcherConfig.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcherConfig.EnableRaisingEvents = true;

            ReadConfigs(null, null);
        }

        private static void ReadConfigs(object sender, FileSystemEventArgs eargs)
        {
            Dictionary<string, string> localConfig = new Dictionary<string, string>();

            foreach (FileInfo file in pluginDirectory.GetFiles("*.json", SearchOption.AllDirectories))
            {
                string[] filename = file.Name.Split('.');

                if (filename.Length != 5)
                    continue;

                if (!file.Name.ToLower().StartsWith(pluginID.ToLower()))
                    continue;

                if (!Enum.TryParse(filename[3], true, out ItemsListType list))
                    continue;

                LogInfo($"Found {file.FullName}");

                string listKey = TraderListKey(filename[2].ToLower(), list) + ".plugin";

                try
                {
                    using (FileStream fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        localConfig.Add(listKey, reader.ReadToEnd());
                        reader.Close();
                        fs.Dispose();
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                }
            }

            foreach (FileInfo file in configDirectory.GetFiles("*.json", SearchOption.AllDirectories))
            {
                string[] filename = file.Name.Split('.');

                if (filename.Length != 5)
                    continue;

                if (!file.Name.ToLower().StartsWith(pluginID.ToLower()))
                    continue;

                if (!Enum.TryParse(filename[3], true, out ItemsListType list))
                    continue;

                LogInfo($"Found {file.FullName}");

                string listKey = TraderListKey(filename[2].ToLower(), list) + ".config";

                try
                {
                    using (FileStream fs = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        localConfig.Add(listKey, reader.ReadToEnd());
                        reader.Close();
                        fs.Dispose();
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning($"Error reading file ({file.FullName})! Error: {e.Message}");
                }
            }

            foreach (string resource in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (!resource.StartsWith("TradersExtended.Configs.") && !resource.EndsWith(".json"))
                    continue;

                string[] resName = resource.Replace("TradersExtended.Configs.", "").Replace(".json", "").Split('.');

                if (resName.Length != 2)
                    continue;

                if (!Enum.TryParse(resName[1], true, out ItemsListType listType))
                    continue;

                string list = TraderListKey(resName[0].ToLower(), listType) + ".internal";

                LogInfo($"Found resource {list}");

                try
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        localConfig.Add(list, reader.ReadToEnd());
                        reader.Close();
                        stream.Dispose();
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning($"Error reading resource ({resource})! Error: {e.Message}");
                }
            }

            configsJSON.AssignLocalValue(localConfig);
        }

        private static void LoadConfigs()
        {
            tradeableItems.Clear();
            sellableItems.Clear();

            foreach (KeyValuePair<string, string> configJSON in configsJSON.Value)
            {
                string[] configKey = configJSON.Key.Split('.');
                string trader = configKey[0];
                Enum.TryParse(configKey[1], true, out ItemsListType list);

                if (list == ItemsListType.Buy)
                    LoadTradeableItems(configJSON.Value, trader);
                else
                    LoadSellableItems(configJSON.Value, trader);
            }

            if (addCommonValuableItemsToSellList.Value)
                AddCommonValuableItems();
        }

        private static void LoadTradeableItems(string JSON, string trader)
        {
            string listKey = TraderListKey(trader, ItemsListType.Buy);

            if (!tradeableItems.ContainsKey(listKey))
                tradeableItems.Add(listKey, new List<TradeableItem>());

            List<TradeableItem> itemsFromFile;

            try
            {
                itemsFromFile = JsonConvert.DeserializeObject<List<TradeableItem>>(JSON);
            }
            catch (Exception e)
            {
                logger.LogWarning($"Error parsing items ({listKey})! Error: {e.Message}");
                return;
            }

            List<TradeableItem> items = tradeableItems[listKey].Concat(itemsFromFile).ToList();

            LogInfo($"Loaded {itemsFromFile.Count} tradeable item from {listKey}");

            tradeableItems[listKey] = items;
        }

        private static void LoadSellableItems(string JSON, string trader)
        {
            string listKey = TraderListKey(trader, ItemsListType.Sell);

            if (!sellableItems.ContainsKey(listKey))
                sellableItems.Add(listKey, new List<TradeableItem>());

            List<TradeableItem> itemsFromFile;

            try
            {
                itemsFromFile = JsonConvert.DeserializeObject<List<TradeableItem>>(JSON);
            }
            catch (Exception e)
            {
                logger.LogWarning($"Error parsing items ({listKey})! Error: {e.Message}");
                return;
            }

            List<TradeableItem> items = sellableItems[listKey].Concat(itemsFromFile).ToList();

            LogInfo($"Loaded {itemsFromFile.Count} sellable item from {listKey}");

            sellableItems[listKey] = items;
        }

        private static void AddCommonValuableItems()
        {
            string listKey = CommonListKey(ItemsListType.Sell);

            sellableItems[listKey] = new List<TradeableItem>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (!prefab.TryGetComponent(out ItemDrop itemDrop))
                    continue;

                if (itemDrop.m_itemData.m_shared.m_value <= 0)
                    continue;

                if (IsItemInSellList(prefab.name))
                    continue;

                sellableItems[listKey].Add(new TradeableItem()
                {
                    prefab = prefab.name,
                    price = itemDrop.m_itemData.m_shared.m_value
                });
            }

            LogInfo($"Loaded {sellableItems[listKey].Count} common valuable items from ObjectDB");
        }

        private static bool IsItemInSellList(string prefabName)
        {
            foreach (string listKey in sellableItems.Keys)
                if (sellableItems[listKey].Any(item => item.prefab == prefabName))
                    return true;
            
            return false;
        }

        private static string TraderName(string name)
        {
            return name.ToLower().Replace("$npc_", "");
        }

        public static string TraderListKey(string name, ItemsListType type)
        {
            return $"{TraderName(name)}.{type}";
        }
        
        public static string CommonListKey(ItemsListType type)
        {
            return TraderListKey("common", type);
        }
    }
}
