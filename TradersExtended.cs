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
        public const string pluginVersion = "2.0.1";

        internal const string DefaultEditorGlobalKeys = "defeated_bonemass,defeated_gdking,defeated_goblinking,defeated_dragon,defeated_eikthyr,defeated_queen,defeated_fader,defeated_serpent,KilledTroll,killed_surtling,KilledBat,Hildir1,Hildir2,Hildir3";
        internal const string DefaultEditorPlayerKeys = "GP_Eikthyr,GP_TheElder,GP_Bonemass,GP_Moder,GP_Yagluth,GP_Queen,GP_Fader";
        internal const string DefaultEditorVisibleItemColumns = "Prefab,Name,Stack,Price,Quality,Currency,RequiredGlobalKey,BlockedGlobalKey,RequiredPlayerKey,BlockedPlayerKey";

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
        internal static ConfigEditor configEditor;

        private static ConfigEntry<bool> loggingEnabled;
        private static ConfigEntry<bool> configLocked;

        internal static ConfigEntry<KeyboardShortcut> configEditorShortcut;
        internal static ConfigEntry<Vector2> configEditorWindowPosition;
        internal static ConfigEntry<Vector2> configEditorWindowSize;
        internal static ConfigEntry<bool> configEditorBlockGameInput;
        internal static ConfigEntry<bool> configEditorUseValheimGuiScale;
        internal static ConfigEntry<float> configEditorUiScale;
        internal static ConfigEntry<int> configEditorFontSize;
        internal static ConfigEntry<float> configEditorFileListWidth;
        internal static ConfigEntry<bool> configEditorShowAllItems;
        internal static ConfigEntry<string> configEditorGlobalKeys;
        internal static ConfigEntry<string> configEditorPlayerKeys;
        internal static ConfigEntry<string> configEditorVisibleItemColumns;

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

        internal static DirectoryInfo configDirectory;
        private static readonly List<FileSystemWatcher> configWatchers = new List<FileSystemWatcher>();
        private static Coroutine configReloadCoroutine;
        private static Coroutine configLoadCoroutine;

        public static HashSet<string> _ignoreItemDiscovery = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> _tradersToRepairWeapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static HashSet<string> _tradersToRepairArmor = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            instance = this;
            logger = Logger;

            configDirectory = new DirectoryInfo(Paths.ConfigPath);

            ConfigInit();
            configEditor = new ConfigEditor();
            configSync.AddLockingConfigEntry(configLocked);
            itemConfigs.ValueChanged += StartConfigLoad;
            traderConfigFiles.ValueChanged += TraderConfigManager.LoadSyncedConfigs;

            EpicLootCompat.CheckForCompatibility();

            harmony.PatchAll();
            Game.isModded = true;
        }

        private void Start()
        {
            configEditor?.MarkGameWindowReady();
            FillConfigLists();
            TraderCurrency.RebuildOverrides();
            SetupConfigWatcher();
        }

        private void Update()
        {
            if (instance != null && instance != this)
                return;
            ConfigEditorTransport.Update();
            configEditor?.Update();
            AmountDialog.Update();
        }

        private void LateUpdate()
        {
            if (instance != null && instance != this)
                return;
            configEditor?.LateUpdate();
        }

        private void OnGUI()
        {
            if (instance != null && instance != this)
                return;
            configEditor?.OnGUI();
        }

        private void OnDestroy()
        {
            if (instance != null && instance != this)
                return;
            DisposeConfigWatchers();
            if (configReloadCoroutine != null)
                StopCoroutine(configReloadCoroutine);
            if (configLoadCoroutine != null)
                StopCoroutine(configLoadCoroutine);

            itemConfigs.ValueChanged -= StartConfigLoad;
            traderConfigFiles.ValueChanged -= TraderConfigManager.LoadSyncedConfigs;
            BuybackManager.ResetCache();
            CoinsPatches.RestoreAll();
            configEditor?.Dispose();
            configEditor = null;
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
            configEditorShortcut = config("Configuration editor", "Configuration editor shortcut", new KeyboardShortcut(KeyCode.P, KeyCode.LeftControl), "Open or close the in-game Traders Extended configuration editor. [Not synchronized with server]", false);
            configEditorWindowPosition = config("Configuration editor", "Configuration editor position", new Vector2(-1f, -1f), "Saved position of the configuration editor window. [Not synchronized with server]", false);
            configEditorWindowSize = config("Configuration editor", "Configuration editor size", new Vector2(1500f, 850f), "Saved logical size of the configuration editor window. [Not synchronized with server]", false);
            configEditorBlockGameInput = config("Configuration editor", "Block game input", true, "Block all Valheim gameplay input while the configuration editor is open. IMGUI input and the editor shortcut remain available. [Not synchronized with server]", false);
            configEditorUseValheimGuiScale = config("Configuration editor", "Use Valheim GUI scaling", true, "Multiply the editor scale by Valheim's Accessibility - Scale GUI setting. [Not synchronized with server]", false);
            configEditorUiScale = config("Configuration editor", "Scale", 1.0f, new ConfigDescription("Additional configuration editor UI scale. [Not synchronized with server]", new AcceptableValueRange<float>(0.6f, 2.0f)), false);
            configEditorFontSize = config("Configuration editor", "Font size", 13, new ConfigDescription("Base configuration editor IMGUI font size. [Not synchronized with server]", new AcceptableValueRange<int>(9, 28)), false);
            configEditorFileListWidth = config("Configuration editor", "File list width", 382f, new ConfigDescription("Logical width of the configuration file column. It can also be changed by dragging the column separator. [Not synchronized with server]", new AcceptableValueRange<float>(240f, 800f)), false);
            configEditorShowAllItems = config("Configuration editor", "Show all items", false, "Show every ObjectDB item in item pickers. When disabled, hide AI equipment and invalid or non-user-facing items. [Not synchronized with server]", false);
            configEditorVisibleItemColumns = config("Configuration editor", "Visible item columns", DefaultEditorVisibleItemColumns, "Comma-separated item editor columns to display: Prefab, Name, Stack, Price, Quality, Currency, RequiredGlobalKey, BlockedGlobalKey, RequiredPlayerKey and BlockedPlayerKey. [Not synchronized with server]", false);
            configEditorGlobalKeys = config("Configuration editor", "Global keys", DefaultEditorGlobalKeys, "Comma-separated global keys available in the configuration editor. Custom keys can be added or removed in the picker; built-in keys remain protected. [Not synchronized with server]", false);
            configEditorPlayerKeys = config("Configuration editor", "Player keys", DefaultEditorPlayerKeys, "Comma-separated player keys available in the configuration editor. Custom keys can be added or removed in the picker; built-in keys remain protected. [Not synchronized with server]", false);

            checkForDiscovery = config("Item discovery", "Sell only discovered items", true, "A trader will not sell items that the buyer has not discovered.");
            checkForDiscoveryIgnoreItems = config("Item discovery", "Undiscovered items list to sell", "", "Comma-separated prefab names that bypass the discovery check. Vanilla trader items are included by default.");
            checkForDiscoveryIgnoreItems.SettingChanged += delegate { FillConfigLists(); };

            coinsPatch = config("Item coins", "Change values", false, "Change properties of the Coins item.");
            coinsWeight = config("Item coins", "Coins weight", 0f, "Weight of one coin.");
            coinsStackSize = config("Item coins", "Coins stack size", 2000, "Maximum coin stack size.");
            coinsPatch.SettingChanged += delegate { CoinsPatches.UpdateCoinsPrefab(); };
            coinsWeight.SettingChanged += delegate { CoinsPatches.UpdateCoinsPrefab(); };
            coinsStackSize.SettingChanged += delegate { CoinsPatches.UpdateCoinsPrefab(); };

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
                !string.Equals(group, "Item coins", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(group, "Configuration editor", StringComparison.OrdinalIgnoreCase))
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
    }
}
