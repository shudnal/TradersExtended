using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ServerSync;
using System.IO;
using System;
using Newtonsoft.Json;
using System.Linq;

namespace TradersExtended
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("randyknapp.mods.auga")]
    public class TradersExtended : BaseUnityPlugin
    {
        private const string pluginID = "shudnal.TradersExtended";
        private const string pluginName = "Traders Extended";
        private const string pluginVersion = "1.0.6";

        private Harmony _harmony;

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        public static ManualLogSource logger;

        internal static TradersExtended instance;

        private static ConfigEntry<bool> modEnabled;
        private static ConfigEntry<bool> configLocked;

        private static GameObject sellPanel;
        private static Button sellButton;

        private static List<GameObject> sellItemList = new List<GameObject>();
        private static List<ItemDrop.ItemData> m_tempItems = new List<ItemDrop.ItemData>();
        private static Dictionary<string, Dictionary<int, int>> m_tempItemsPrice = new Dictionary<string, Dictionary<int, int>>();

        private static ItemDrop.ItemData selectedItem;
        private static int selectedItemIndex = -1;

        private static readonly Dictionary<string, List<TradeableItem>> tradeableItems = new Dictionary<string, List<TradeableItem>>();
        private static readonly Dictionary<string, List<TradeableItem>> sellableItems = new Dictionary<string, List<TradeableItem>>();

        private static readonly CustomSyncedValue<Dictionary<string, string>> configsJSON = new CustomSyncedValue<Dictionary<string, string>>(configSync, "JSON configs", new Dictionary<string, string>());

        private static DirectoryInfo pluginFolder;

        private static float m_leftClickTime;
        private static GameObject amountDialog;
        private static Slider sliderDialog;
        private static Text sliderTitle;
        private static Text sliderAmountText;
        private static Image sliderImage;

        private static string m_splitInput = "";
        private static DateTime m_lastSplitInput;
        private static float m_splitNumInputTimeoutSec = 0.5f;

        private static bool epicLootIsAdventureModeEnabled = false;

        [Serializable]
        public class TradeableItem
        {
            public string prefab;

            public int stack = 1;

            public int price = 1;

            public string requiredGlobalKey = "";

        }

        public enum ItemsListType
        {
            Buy,
            Sell
        }

        internal void Awake()
        {
            _harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), pluginID);

            instance = this;

            logger = Logger;

            pluginFolder = new DirectoryInfo(Assembly.GetExecutingAssembly().Location).Parent;

            ConfigInit();
            _ = configSync.AddLockingConfigEntry(configLocked);

            configsJSON.ValueChanged += new Action(LoadConfigs);

            var EpicLootPlugin = GetComponent("EpicLoot");
            if (EpicLootPlugin != null)
            {
                var EpicLootPluginType = EpicLootPlugin.GetType();
                var IsAdventureModeEnabledMethod = AccessTools.Method(EpicLootPluginType, "IsAdventureModeEnabled");
                if (IsAdventureModeEnabledMethod != null)
                {
                    epicLootIsAdventureModeEnabled = (bool)MethodInvoker.GetHandler(IsAdventureModeEnabledMethod)(null);
                    logger.LogInfo($"EpicLoot found. Adventure mode: {epicLootIsAdventureModeEnabled}");
                }
            }
        }

        public void Update()
        {
            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.IsDead() || localPlayer.InCutscene() || localPlayer.IsTeleporting())
            {
                CloseAmountDialog();
                return;
            }
            UpdateSplitDialog();
        }

        public void UpdateSplitDialog()
        {

            if (sliderDialog == null) return;

            if (!sliderDialog.gameObject.activeInHierarchy) return;

            for (int i = 0; i < 10; i++)
            {
                if (ZInput.GetKeyDown((KeyCode)(256 + i)) || ZInput.GetKeyDown((KeyCode)(48 + i)))
                {
                    if (m_lastSplitInput + TimeSpan.FromSeconds(m_splitNumInputTimeoutSec) < DateTime.Now)
                    {
                        m_splitInput = "";
                    }

                    m_lastSplitInput = DateTime.Now;
                    m_splitInput += i;
                    if (int.TryParse(m_splitInput, out int result))
                    {
                        sliderDialog.value = Mathf.Clamp(result, 1f, sliderDialog.maxValue);
                        OnSplitSliderChanged();
                    }
                }
            }

            if (ZInput.GetKeyDown(KeyCode.LeftArrow) && sliderDialog.value > 1f)
            {
                sliderDialog.value -= 1f;
                OnSplitSliderChanged();
            }

            if (ZInput.GetKeyDown(KeyCode.RightArrow) && sliderDialog.value < sliderDialog.maxValue)
            {
                sliderDialog.value += 1f;
                OnSplitSliderChanged();
            }

            if (ZInput.GetKeyDown(KeyCode.KeypadEnter) || ZInput.GetKeyDown(KeyCode.Return))
            {
                BuySelectedItem(StoreGui.instance);
            }
        }

        private void ConfigInit()
        {
            config("General", "NexusID", 2509, "Nexus mod ID for updates", false);
            modEnabled = config("General", "Enabled", defaultValue: true, "Enable this mod. Reload the game to take effect.");
            configLocked = config("General", "Lock Configuration", defaultValue: true, "Configuration is locked and can be changed by server admins only.");
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        private void OnDestroy() => _harmony?.UnpatchSelf();

        public static void OnSelectedItem(GameObject button)
        {
            int index = FindSelectedRecipe(button);
            SelectItem(index, center: false);
        }

        public static int FindSelectedRecipe(GameObject button)
        {
            for (int i = 0; i < sellItemList.Count; i++)
            {
                if (sellItemList[i] == button)
                {
                    return i;
                }
            }

            return -1;
        }

        public static int GetSelectedItemIndex()
        {
            int result = -1;
            List<ItemDrop.ItemData> availableItems = m_tempItems;
            for (int i = 0; i < availableItems.Count; i++)
            {
                if (availableItems[i] == selectedItem)
                {
                    result = i;
                }
            }

            return result;
        }

        public static void SelectItem(int index, bool center)
        {
            logger.LogInfo("Setting selected item " + index);
            for (int i = 0; i < sellItemList.Count; i++)
            {
                bool active = i == index;
                sellItemList[i].transform.Find("selected").gameObject.SetActive(active);
            }

            if (center && index >= 0)
            {
                StoreGui.instance.m_itemEnsureVisible.CenterOnItem(sellItemList[index].transform as RectTransform);
            }

            if (index < 0)
            {
                selectedItem = null;
            }
            else
            {
                selectedItem = m_tempItems[index];
            }
        }

        public static void SellSelectedItem(StoreGui __instance)
        {
            if (selectedItem != null)
            {
                ItemDrop m_coinPrefab = __instance.m_coinPrefab;
                if (m_coinPrefab == null)
                {
                    logger.LogWarning($"No m_coinPrefab is setted in StoreGui");
                    return;
                }

                if (ItemPrice(selectedItem) == 0)
                    return;

                selectedItemIndex = GetSelectedItemIndex();

                KeyValuePair<int, int> stackPrice = m_tempItemsPrice[selectedItem.m_shared.m_name].First();
                string text;
                int stackCoins = stackPrice.Value;

                if (stackPrice.Key == 1)
                {
                    stackCoins *= selectedItem.m_stack;
                    Player.m_localPlayer.GetInventory().RemoveItem(selectedItem);
                    text = ((selectedItem.m_stack <= 1) ? selectedItem.m_shared.m_name : (selectedItem.m_stack + "x" + selectedItem.m_shared.m_name));
                }
                else
                {
                    Player.m_localPlayer.GetInventory().RemoveItem(selectedItem, stackPrice.Key);
                    text = $"{stackPrice.Key}x{selectedItem.m_shared.m_name}";
                }

                Player.m_localPlayer.GetInventory().AddItem(m_coinPrefab.gameObject.name, stackCoins, m_coinPrefab.m_itemData.m_quality, m_coinPrefab.m_itemData.m_variant, 0L, "");
                __instance.m_sellEffects.Create((__instance as MonoBehaviour).transform.position, Quaternion.identity);
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_sold", text, stackCoins.ToString()), 0, selectedItem.m_shared.m_icons[0]);
                __instance.m_trader.OnSold();
                Gogan.LogEvent("Game", "SoldItem", text, 0L);

                __instance.FillList();
            }
        }

        private static void AddItemToSellList(TradeableItem item)
        {
            if (item.price > 0 && item.stack > 0)
            {
                if (string.IsNullOrEmpty(item.requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(item.requiredGlobalKey))
                {
                    GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
                    if (prefab != null)
                    {
                        string name = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

                        // While this data structure supports several price per stack current sell list implementation does not
                        // Maybe later
                        if (!m_tempItemsPrice.ContainsKey(name))
                        {
                            m_tempItemsPrice.Add(name, new Dictionary<int, int>());

                            if (!m_tempItemsPrice[name].ContainsKey(item.stack))
                            {
                                if (item.stack == 1)
                                {
                                    Player.m_localPlayer.GetInventory().GetAllItems(name, m_tempItems);
                                    m_tempItemsPrice[name].Add(item.stack, item.price);
                                }
                                else if (Player.m_localPlayer.GetInventory().CountItems(name) >= item.stack)
                                {
                                    m_tempItems.Add(Player.m_localPlayer.GetInventory().GetItem(name));
                                    m_tempItemsPrice[name].Add(item.stack, item.price);
                                }
                            }
                        }
                    }
                }
            }
        }

        public static void FillSellableList(StoreGui __instance)
        {
            foreach (GameObject item in sellItemList)
            {
                Destroy(item);
            }
            sellItemList.Clear();

            Transform items = sellPanel.transform.Find("ItemList").Find("Items");

            RectTransform m_listRoot = items.Find("ListRoot").GetComponent<RectTransform>();
            GameObject m_listElement = items.Find("ItemElement").gameObject;

            m_tempItems.Clear();
            m_tempItemsPrice.Clear();

            if (sellableItems.ContainsKey(CommonListKey(ItemsListType.Sell)))
                sellableItems[CommonListKey(ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            if (sellableItems.ContainsKey(TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)))
                sellableItems[TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            if (__instance.m_coinPrefab != null)
            {
                for (int i = m_tempItems.Count - 1; i >= 0; i--)
                {
                    ItemDrop.ItemData tradeItem = m_tempItems[i];
                    if (tradeItem.m_shared.m_name == __instance.m_coinPrefab.m_itemData.m_shared.m_name)
                        m_tempItems.RemoveAt(i);
                }
            }

            float b = (float)m_tempItems.Count * __instance.m_itemSpacing;
            b = Mathf.Max(__instance.m_itemlistBaseSize, b);
            m_listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, b);
            for (int i = 0; i < m_tempItems.Count; i++)
            {
                ItemDrop.ItemData tradeItem = m_tempItems[i];

                GameObject element = Instantiate(m_listElement, m_listRoot);
                element.SetActive(value: true);
                (element.transform as RectTransform).anchoredPosition = new Vector2(0f, (float)i * (0f - __instance.m_itemSpacing));
                Image component = element.transform.Find("icon").GetComponent<Image>();
                component.sprite = tradeItem.m_shared.m_icons[0];
                string text = Localization.instance.Localize(tradeItem.m_shared.m_name);

                if (ItemStack(tradeItem) > 1)
                {
                    text = text + " x" + ItemStack(tradeItem);
                }

                Text component2 = element.transform.Find("name").GetComponent<Text>();
                component2.text = text;
                element.GetComponent<UITooltip>().Set(tradeItem.m_shared.m_name, tradeItem.GetTooltip(), __instance.m_tooltipAnchor);
                Text component3 = Utils.FindChild(element.transform, "price").GetComponent<Text>();
                component3.text = ItemPrice(tradeItem).ToString();

                element.GetComponent<Button>().onClick.AddListener(delegate
                {
                    OnSelectedItem(element);
                });

                sellItemList.Add(element);
            }

            if (selectedItemIndex == -1)
                selectedItemIndex = GetSelectedItemIndex();

            SelectItem(Mathf.Min(m_tempItems.Count - 1, selectedItemIndex), center: false);
        }

        public static void UpdateSellButton(StoreGui __instance)
        {
            int coins = 0;
            for (int i = m_tempItems.Count - 1; i >= 0; i--)
            {
                ItemDrop.ItemData tradeItem = m_tempItems[i];
                if (tradeItem.m_shared.m_name != __instance.m_coinPrefab.m_itemData.m_shared.m_name)
                    coins += ItemPrice(tradeItem) * Mathf.CeilToInt(Player.m_localPlayer.GetInventory().CountItems(tradeItem.m_shared.m_name) / ItemStack(tradeItem));
            }

            sellPanel.transform.Find("coins").Find("coins").GetComponent<Text>().text = coins.ToString();

            sellButton.interactable = selectedItem != null;
        }

        private static int ItemPrice(ItemDrop.ItemData tradeItem)
        {
            try
            {
                KeyValuePair<int, int> stackPrice = m_tempItemsPrice[tradeItem.m_shared.m_name].First();
                return (stackPrice.Key == 1) ? stackPrice.Value * tradeItem.m_stack : stackPrice.Value;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Cannot find item {tradeItem.m_shared.m_name} price: {ex}");
            }

            return 0;
        }

        private static int ItemStack(ItemDrop.ItemData tradeItem)
        {
            try
            {
                KeyValuePair<int, int> stackPrice = m_tempItemsPrice[tradeItem.m_shared.m_name].First();
                return (stackPrice.Key == 1) ? tradeItem.m_stack : stackPrice.Key;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Cannot find item {tradeItem.m_shared.m_name} stack: {ex}");
            }

            return 1;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.SellItem))]
        public static class StoreGui_SellItem_Patch
        {
            static bool Prefix(StoreGui __instance)
            {
                if (!modEnabled.Value) return true;

                SellSelectedItem(__instance);

                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Awake))]
        public static class StoreGui_Awake_Patch
        {
            [HarmonyPriority(Priority.Last)]
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                sellPanel = Instantiate(__instance.m_rootPanel, __instance.m_rootPanel.transform);
                sellPanel.transform.localPosition = new Vector3(250, 0, 0);

                Destroy(sellPanel.transform.Find("SellPanel").gameObject);
                Destroy(sellPanel.transform.Find("border (1)").gameObject);
                Destroy(sellPanel.transform.Find("topic").gameObject);
                Destroy(sellPanel.transform.Find("bkg").gameObject);

                sellPanel.transform.Find("BuyButton").Find("Text").GetComponent<Text>().text = Localization.instance.Localize("$store_sell");

                sellButton = sellPanel.transform.Find("BuyButton").GetComponent<Button>();

                sellButton.onClick = __instance.m_rootPanel.transform.Find("SellPanel").Find("SellButton").GetComponent<Button>().onClick;

                __instance.m_rootPanel.transform.Find("SellPanel").gameObject.SetActive(false);

                __instance.m_rootPanel.transform.Find("border (1)").GetComponent<RectTransform>().anchorMax = new Vector2(2, 1);

                RectTransform topic = __instance.m_rootPanel.transform.Find("topic").GetComponent<RectTransform>();
                topic.anchorMin = new Vector2(0.5f, 1);
                topic.anchorMax = new Vector2(1.5f, 1);

                amountDialog = Instantiate(InventoryGui.instance.m_splitPanel.gameObject, __instance.m_rootPanel.transform.parent);

                Transform win_bkg = amountDialog.transform.Find("win_bkg");

                sliderTitle = win_bkg.Find("Text").GetComponent<Text>();
                sliderDialog = win_bkg.Find("Slider").GetComponent<Slider>();
                sliderAmountText = win_bkg.Find("amount").GetComponent<Text>();
                sliderImage = win_bkg.Find("Icon_bkg").Find("Icon").GetComponent<Image>();

                sliderDialog.onValueChanged.AddListener(delegate
                {
                    OnSplitSliderChanged();
                });

                win_bkg.Find("Button_ok").GetComponent<Button>().onClick.AddListener(delegate
                {
                    BuySelectedItem(__instance);
                });

                win_bkg.Find("Button_cancel").GetComponent<Button>().onClick.AddListener(delegate
                {
                    CloseAmountDialog();
                });

                if (epicLootIsAdventureModeEnabled)
                {
                    Vector3 storePos = __instance.m_rootPanel.transform.localPosition;
                    storePos.x -= 100;
                    __instance.m_rootPanel.transform.localPosition = storePos;
                }

                logger.LogInfo($"StoreGui panel patched");

                SetupConfigWatcher();
            }
        }

        private static void SetupConfigWatcher()
        {
            string filter = $"{pluginID}.*.json";

            FileSystemWatcher fileSystemWatcher1 = new FileSystemWatcher(pluginFolder.FullName, filter);
            fileSystemWatcher1.Changed += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcher1.Created += new FileSystemEventHandler(ReadConfigs);
            fileSystemWatcher1.Renamed += new RenamedEventHandler(ReadConfigs);
            fileSystemWatcher1.IncludeSubdirectories = true;
            fileSystemWatcher1.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            fileSystemWatcher1.EnableRaisingEvents = true;

            ReadConfigs(null, null);
        }

        private static void ReadConfigs(object sender, FileSystemEventArgs eargs)
        {
            Dictionary<string, string> localConfig = new Dictionary<string, string>();

            foreach (FileInfo file in pluginFolder.GetFiles("*.json", SearchOption.AllDirectories))
            {
                string[] filename = file.Name.Split('.');

                if (filename.Length != 5)
                    continue;

                if (!file.Name.ToLower().StartsWith(pluginID.ToLower()))
                    continue;

                if (!Enum.TryParse(filename[3], true, out ItemsListType list))
                    continue;

                logger.LogInfo($"Found {file.FullName}");

                string listKey = TraderListKey(filename[2].ToLower(), list);

                try
                {
                    localConfig.Add(listKey, File.ReadAllText(file.FullName));
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

                logger.LogInfo($"Found resource {list}");

                try
                {
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        localConfig.Add(list, reader.ReadToEnd());
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning($"Error reading resource ({resource})! Error: {e.Message}");
                }
            }

            configsJSON.AssignLocalValue(localConfig);
        }

        private static void AddCommonValuableItems()
        {
            List<TradeableItem> valuableItems = new List<TradeableItem>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (!prefab.TryGetComponent<ItemDrop>(out ItemDrop itemDrop))
                    continue;

                if (itemDrop.m_itemData.m_shared.m_value > 0)
                    valuableItems.Add(new TradeableItem()
                    {
                        prefab = prefab.name,
                        price = itemDrop.m_itemData.m_shared.m_value
                    });
            }

            string listKey = CommonListKey(ItemsListType.Sell);

            if (!sellableItems.ContainsKey(listKey))
                sellableItems.Add(listKey, new List<TradeableItem>());

            int itemsCount = sellableItems[listKey].Count;

            List<TradeableItem> items = valuableItems.Concat(sellableItems[listKey]).GroupBy(item => item.prefab).Select(g => g.First()).ToList();

            logger.LogInfo($"Loaded {items.Count - itemsCount} common valuable items from ObjectDB");

            sellableItems[listKey] = items;
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

            List<TradeableItem> items = tradeableItems[listKey].Concat(itemsFromFile).GroupBy(item => item.prefab).Select(g => g.First()).ToList();

            logger.LogInfo($"Loaded {itemsFromFile.Count} tradeable item from {listKey}");

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

            List<TradeableItem> items = sellableItems[listKey].Concat(itemsFromFile).GroupBy(item => item.prefab).Select(g => g.First()).ToList();

            logger.LogInfo($"Loaded {itemsFromFile.Count} sellable item from {listKey}");

            sellableItems[listKey] = items;
        }

        private static string TraderListKey(string name, ItemsListType type)
        {
            return $"{name.ToLower().Replace("$npc_", "")}.{type}";
        }

        private static string CommonListKey(ItemsListType type)
        {
            return TraderListKey("common", type);
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
        public static class StoreGui_FillList_Patch
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                FillSellableList(__instance);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.OnSelectedItem))]
        public static class StoreGui_SelectItem_Patch
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                if (Time.time - m_leftClickTime < 0.3f)
                {
                    OnSelectedTradeableItemDblClick(__instance);
                    m_leftClickTime = 0f;
                }
                else
                {
                    m_leftClickTime = Time.time;
                }
            }
        }

        private static void OnSelectedTradeableItemDblClick(StoreGui __instance)
        {
            Trader.TradeItem selectedItem = __instance.m_selectedItem;

            int playerCoins = __instance.GetPlayerCoins();

            if (selectedItem == null) return;

            if (selectedItem.m_stack != 1) return;

            if (selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize == 1) return;

            if (playerCoins < selectedItem.m_price) return;

            sliderDialog.minValue = 1f;
            sliderDialog.maxValue = Math.Min(selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize, Mathf.CeilToInt(playerCoins / selectedItem.m_price));
            sliderDialog.value = 1;

            sliderTitle.text = $"{Localization.instance.Localize("$store_buy")} {Localization.instance.Localize(selectedItem.m_prefab.m_itemData.m_shared.m_name)}";
            sliderImage.sprite = selectedItem.m_prefab.m_itemData.GetIcon();

            OnSplitSliderChanged();

            amountDialog.SetActive(value: true);
        }

        public static void OnSplitSliderChanged()
        {
            sliderAmountText.text = ((int)sliderDialog.value).ToString();
        }

        private static void BuySelectedItem(StoreGui __instance)
        {
            if (__instance.m_selectedItem != null && CanAffordSelectedItem(__instance))
            {
                int stack = Mathf.Min(Mathf.CeilToInt(sliderDialog.value), __instance.m_selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize);
                int quality = __instance.m_selectedItem.m_prefab.m_itemData.m_quality;
                int variant = __instance.m_selectedItem.m_prefab.m_itemData.m_variant;
                if (Player.m_localPlayer.GetInventory().AddItem(__instance.m_selectedItem.m_prefab.name, stack, quality, variant, 0L, "") != null)
                {
                    Player.m_localPlayer.GetInventory().RemoveItem(__instance.m_coinPrefab.m_itemData.m_shared.m_name, __instance.m_selectedItem.m_price * stack);
                    __instance.m_trader.OnBought(__instance.m_selectedItem);
                    __instance.m_buyEffects.Create((__instance as MonoBehaviour).transform.position, Quaternion.identity);
                    Player.m_localPlayer.ShowPickupMessage(__instance.m_selectedItem.m_prefab.m_itemData, stack);
                    __instance.FillList();
                    Gogan.LogEvent("Game", "BoughtItem", __instance.m_selectedItem.m_prefab.name, 0L);
                }
            }
            CloseAmountDialog();
        }

        private static bool CanAffordSelectedItem(StoreGui __instance)
        {
            int playerCoins = __instance.GetPlayerCoins();
            return __instance.m_selectedItem.m_price * sliderDialog.value <= playerCoins;
        }

        private static void CloseAmountDialog()
        {
            if (amountDialog != null)
                amountDialog.SetActive(value: false);
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_Patch
        {
            static void Postfix(Trader __instance, ref List<Trader.TradeItem> __result)
            {
                if (!modEnabled.Value) return;

                string listKey = CommonListKey(ItemsListType.Buy);

                if (tradeableItems.ContainsKey(listKey))
                    foreach (TradeableItem item in tradeableItems[listKey])
                    {
                        if (string.IsNullOrEmpty(item.requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(item.requiredGlobalKey))
                        {
                            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item.prefab);

                            if (itemPrefab == null)
                                continue;

                            ItemDrop prefab = itemPrefab.GetComponent<ItemDrop>();

                            if (__result.Exists(x => x.m_prefab == prefab))
                                continue;

                            __result.Add(new Trader.TradeItem
                            {
                                m_prefab = prefab,
                                m_price = item.price,
                                m_stack = item.stack,
                                m_requiredGlobalKey = item.requiredGlobalKey
                            });
                        }
                    }

                listKey = TraderListKey(__instance.m_name, ItemsListType.Buy);

                if (tradeableItems.ContainsKey(listKey))
                    foreach (TradeableItem item in tradeableItems[listKey])
                    {
                        if (string.IsNullOrEmpty(item.requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(item.requiredGlobalKey))
                        {
                            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item.prefab);

                            if (itemPrefab == null)
                                continue;

                            ItemDrop prefab = itemPrefab.GetComponent<ItemDrop>();

                            if (__result.Exists(x => x.m_prefab == prefab))
                                continue;

                            __result.Add(new Trader.TradeItem
                            {
                                m_prefab = prefab,
                                m_price = item.price,
                                m_stack = item.stack,
                                m_requiredGlobalKey = item.requiredGlobalKey
                            });
                        }
                    }
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateSellButton))]
        public static class StoreGui_UpdateSellButton_Patch
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                UpdateSellButton(__instance);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
        public static class StoreGui_Show_Patch
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                sellPanel.SetActive(value: true);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Hide))]
        public static class StoreGui_Hide_Patch
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                sellPanel.SetActive(value: false);
                CloseAmountDialog();
            }
        }

        [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
        public static class Terminal_Patch
        {
            public static void Postfix()
            {
                new Terminal.ConsoleCommand("tradersextendedsave", "Save every item from ObjectDB into file ObjectDB.list.json next to dll", args =>
                {
                    SaveFromObjectDB(args.Context);
                });
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

                string filename = Path.Combine(pluginFolder.FullName, "ObjectDB.list.json");

                File.WriteAllText(filename, JSON);

                context.AddString($"Saved {allItems.Count} items to {filename}");
            }
        }
    }
}
