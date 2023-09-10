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
    public class TradersExtended : BaseUnityPlugin
    {
        private const string pluginID = "shudnal.TradersExtended";
        private const string pluginName = "Traders Extended";
        private const string pluginVersion = "1.0.1";

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
        private static Dictionary<string, int> m_tempItemsPrice = new Dictionary<string, int>();

        private static ItemDrop.ItemData selectedItem;

        private static readonly Dictionary<string, List<TradeableItem>> tradeableItems = new Dictionary<string, List<TradeableItem>>();
        private static readonly Dictionary<string, List<SellableItem>> sellableItems = new Dictionary<string, List<SellableItem>>();

        private static readonly CustomSyncedValue<Dictionary<string, string>> configsJSON = new CustomSyncedValue<Dictionary<string, string>>(configSync, "JSON configs", new Dictionary<string, string>());

        private static DirectoryInfo pluginFolder;

        [Serializable]
        public class TradeableItem
        {
            public string prefab;

            public int stack = 1;

            public int price = 100;

            public string requiredGlobalKey;
        }

        [Serializable]
        public class SellableItem
        {
            public string prefab;

            public int price = 0;
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
                int index = GetSelectedItemIndex();

                int stack = m_tempItemsPrice[selectedItem.m_shared.m_name] * selectedItem.m_stack;
                Player.m_localPlayer.GetInventory().RemoveItem(selectedItem);
                Player.m_localPlayer.GetInventory().AddItem(m_coinPrefab.gameObject.name, stack, m_coinPrefab.m_itemData.m_quality, m_coinPrefab.m_itemData.m_variant, 0L, "");
                string text = "";
                text = ((selectedItem.m_stack <= 1) ? selectedItem.m_shared.m_name : (selectedItem.m_stack + "x" + selectedItem.m_shared.m_name));
                __instance.m_sellEffects.Create((__instance as MonoBehaviour).transform.position, Quaternion.identity);
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_sold", text, stack.ToString()), 0, selectedItem.m_shared.m_icons[0]);
                __instance.m_trader.OnSold();
                Gogan.LogEvent("Game", "SoldItem", text, 0L);

                FillSellableList(__instance, index);
            }
        }

        public static void FillSellableList(StoreGui __instance, int selectedItemIndex = -1)
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
                sellableItems[CommonListKey(ItemsListType.Sell)].ForEach(item =>
                {
                    GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
                    if (prefab != null)
                    {
                        string name = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                        Player.m_localPlayer.GetInventory().GetAllItems(name, m_tempItems);
                        if (!m_tempItemsPrice.ContainsKey(name))
                        {
                            m_tempItemsPrice.Add(name, item.price);

                        }
                    }
                });

            if (sellableItems.ContainsKey(TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)))
                sellableItems[TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)].ForEach(item =>
                {
                    GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
                    if (prefab != null)
                    {
                        string name = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;
                        Player.m_localPlayer.GetInventory().GetAllItems(name, m_tempItems);
                        if (!m_tempItemsPrice.ContainsKey(name))
                        {
                            m_tempItemsPrice.Add(name, item.price);
                        }
                    }
                });

            for (int i = m_tempItems.Count - 1; i >= 0; i--)
            {
                ItemDrop.ItemData tradeItem = m_tempItems[i];
                if (tradeItem.m_shared.m_name == __instance.m_coinPrefab.m_itemData.m_shared.m_name)
                    m_tempItems.RemoveAt(i);
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
                if (tradeItem.m_stack > 1)
                {
                    text = text + " x" + tradeItem.m_stack;
                }

                Text component2 = element.transform.Find("name").GetComponent<Text>();
                component2.text = text;
                element.GetComponent<UITooltip>().Set(tradeItem.m_shared.m_name, tradeItem.GetTooltip(), __instance.m_tooltipAnchor);
                Text component3 = Utils.FindChild(element.transform, "price").GetComponent<Text>();
                component3.text = (m_tempItemsPrice[tradeItem.m_shared.m_name] * tradeItem.m_stack).ToString();

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
                    coins += m_tempItemsPrice[tradeItem.m_shared.m_name] * tradeItem.m_stack;
            }

            sellPanel.transform.Find("coins").Find("coins").GetComponent<Text>().text = coins.ToString();

            sellButton.interactable = selectedItem != null;
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
            List<SellableItem> valuableItems = new List<SellableItem>();
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                if (!prefab.TryGetComponent<ItemDrop>(out ItemDrop itemDrop))
                    continue;

                if (itemDrop.m_itemData.m_shared.m_value > 0)
                    valuableItems.Add(new SellableItem()
                    {
                        prefab = itemDrop.m_itemData.m_dropPrefab.name,
                        price = itemDrop.m_itemData.m_shared.m_value
                    });
            }

            string listKey = CommonListKey(ItemsListType.Sell);

            if (!sellableItems.ContainsKey(listKey))
                sellableItems.Add(listKey, new List<SellableItem>());

            List<SellableItem> items = valuableItems.Concat(sellableItems[listKey]).Distinct().ToList();

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

            List<TradeableItem> items = tradeableItems[listKey].Concat(itemsFromFile).Distinct().ToList();

            logger.LogInfo($"Loaded {itemsFromFile.Count} item at {listKey}");

            tradeableItems[listKey] = items;
        }

        private static void LoadSellableItems(string JSON, string trader)
        {
            string listKey = TraderListKey(trader, ItemsListType.Sell);

            if (!sellableItems.ContainsKey(listKey))
                sellableItems.Add(listKey, new List<SellableItem>());

            List<SellableItem> itemsFromFile;

            try
            {
                itemsFromFile = JsonConvert.DeserializeObject<List<SellableItem>>(JSON);
            }
            catch (Exception e)
            {
                logger.LogWarning($"Error parsing items ({listKey})! Error: {e.Message}");
                return;
            }

            List<SellableItem> items = sellableItems[listKey].Concat(itemsFromFile).Distinct().ToList();

            logger.LogInfo($"Loaded {itemsFromFile.Count} item at {listKey}");

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

            }
        }

    }
}
