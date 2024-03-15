using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal class StorePanel
    {
        private static GameObject sellPanel;
        private static Button sellButton;
        private static TMP_Text storeName;
        private static TMP_Text playerName;

        private static bool epicLootEnabled;

        private static readonly List<GameObject> sellItemList = new List<GameObject>();
        private static readonly List<ItemDrop.ItemData> m_tempItems = new List<ItemDrop.ItemData>();
        private static readonly Dictionary<string, Dictionary<int, int>> m_tempItemsPrice = new Dictionary<string, Dictionary<int, int>>();

        private static ItemDrop.ItemData selectedItem;
        private static int selectedItemIndex = -1;

        public static void OnSelectedItem(GameObject button)
        {
            int index = FindSelectedRecipe(button);
            SelectItem(index, center: false);
        }

        public static int FindSelectedRecipe(GameObject button)
        {
            for (int i = 0; i < sellItemList.Count; i++)
                if (sellItemList[i] == button)
                    return i;

            return -1;
        }

        public static int GetSelectedItemIndex()
        {
            int result = -1;
            List<ItemDrop.ItemData> availableItems = m_tempItems;
            for (int i = 0; i < availableItems.Count; i++)
                if (availableItems[i] == selectedItem)
                    result = i;

            return result;
        }

        public static void SelectItem(int index, bool center)
        {
            LogInfo("Setting selected item " + index);
            for (int i = 0; i < sellItemList.Count; i++)
            {
                bool active = i == index;
                sellItemList[i].transform.Find("selected").gameObject.SetActive(active);
            }

            if (center && index >= 0)
                StoreGui.instance.m_itemEnsureVisible.CenterOnItem(sellItemList[index].transform as RectTransform);

            if (index < 0)
                selectedItem = null;
            else
                selectedItem = m_tempItems[index];
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
                __instance.m_sellEffects.Create(__instance.transform.position, Quaternion.identity);
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
                UnityEngine.Object.Destroy(item);

            sellItemList.Clear();

            Transform items = sellPanel.transform.Find("ItemList").Find("Items");

            RectTransform m_listRoot = items.Find("ListRoot").GetComponent<RectTransform>();
            GameObject m_listElement = items.Find("ItemElement").gameObject;

            m_tempItems.Clear();
            m_tempItemsPrice.Clear();

            if (sellableItems.ContainsKey(TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)))
                sellableItems[TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            if (sellableItems.ContainsKey(CommonListKey(ItemsListType.Sell)))
                sellableItems[CommonListKey(ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

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

                GameObject element = UnityEngine.Object.Instantiate(m_listElement, m_listRoot);
                element.SetActive(value: true);
                (element.transform as RectTransform).anchoredPosition = new Vector2(0f, (float)i * (0f - __instance.m_itemSpacing));
                Image component = element.transform.Find("icon").GetComponent<Image>();
                component.sprite = tradeItem.GetIcon();
                string text = Localization.instance.Localize(tradeItem.m_shared.m_name);

                if (ItemStack(tradeItem) > 1)
                {
                    text = text + " x" + ItemStack(tradeItem);
                }

                TMP_Text component2 = element.transform.Find("name").GetComponent<TMP_Text>();
                component2.text = text;
                element.GetComponent<UITooltip>().Set(tradeItem.m_shared.m_name, tradeItem.GetTooltip(), __instance.m_tooltipAnchor);
                TMP_Text component3 = element.transform.Find("coin_bkg").Find("price").GetComponent<TMP_Text>();
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

            sellPanel.transform.Find("coins").Find("coins").GetComponent<TMP_Text>().text = coins.ToString();

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

                FillConfigLists();

                // Add copy of main panel
                sellPanel = UnityEngine.Object.Instantiate(__instance.m_rootPanel, __instance.m_rootPanel.transform);
                sellPanel.transform.localPosition = new Vector3(250, 0, 0);

                // Remove redundant objects
                UnityEngine.Object.Destroy(sellPanel.transform.Find("SellPanel").gameObject);
                UnityEngine.Object.Destroy(sellPanel.transform.Find("border (1)").gameObject);
                UnityEngine.Object.Destroy(sellPanel.transform.Find("bkg").gameObject);

                // Set trader and player names
                RectTransform topic = __instance.m_rootPanel.transform.Find("topic").GetComponent<RectTransform>();
                storeName = topic.GetComponent<TMP_Text>();
                playerName = sellPanel.transform.Find("topic").GetComponent<TMP_Text>();

                // Prepare new sell button
                Transform sellPanelTransform = sellPanel.transform.Find("BuyButton");
                sellPanelTransform.Find("Text").GetComponent<TMP_Text>().SetText(Localization.instance.Localize("$store_sell"));
                sellPanelTransform.GetComponent<UIGamePad>().m_zinputKey = "JoyButtonX";

                // Make sell button to repair button
                RepairPanel.RepurposeSellButton(__instance);

                // Set handler to sell button
                sellButton = sellPanelTransform.GetComponent<Button>();
                sellButton.onClick.RemoveAllListeners();
                sellButton.onClick.AddListener(delegate
                {
                    __instance.OnSellItem();
                });

                // Extend the border 
                __instance.m_rootPanel.transform.Find("border (1)").GetComponent<RectTransform>().anchorMax = new Vector2(2, 1);

                /*topic.anchorMin = new Vector2(0.5f, 1);
                topic.anchorMax = new Vector2(1.5f, 1);*/

                AmountDialog.Init(__instance);

                epicLootEnabled = epicLootPlugin != null;
                if (epicLootEnabled)
                {
                    var EpicLootPluginType = epicLootPlugin.GetType();
                    var IsAdventureModeEnabledMethod = AccessTools.Method(EpicLootPluginType, "IsAdventureModeEnabled");
                    if (IsAdventureModeEnabledMethod != null)
                    {
                        bool isAdventureModeEnabled = (bool)MethodInvoker.GetHandler(IsAdventureModeEnabledMethod)(null);
                        LogInfo($"EpicLoot found. Adventure mode: {isAdventureModeEnabled}");
                        if (isAdventureModeEnabled)
                        {
                            Vector3 storePos = __instance.m_rootPanel.transform.localPosition;
                            storePos.x -= 100;
                            __instance.m_rootPanel.transform.localPosition = storePos;
                        }
                    }
                }

                LogInfo($"StoreGui panel patched");

                SetupConfigWatcher();
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
        public static class StoreGui_FillList_FillSellableList
        {
            static void Postfix(StoreGui __instance, Trader ___m_trader, List<GameObject> ___m_itemList)
            {
                if (!modEnabled.Value) return;

                if (epicLootEnabled)
                {
                    List<Trader.TradeItem> itemList = ___m_trader.GetAvailableItems();
                    if (itemList.Count == ___m_itemList.Count)
                        for (int i = 0; i < itemList.Count; i++)
                        {
                            Image component = ___m_itemList[i].transform.Find("icon").GetComponent<Image>();
                            component.sprite = itemList[i].m_prefab.m_itemData.GetIcon();
                        }
                }

                FillSellableList(__instance);
            }
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_FillBuyableItems
        {
            [HarmonyPriority(Priority.First)]
            static void Postfix(Trader __instance, ref List<Trader.TradeItem> __result)
            {
                if (!modEnabled.Value) return;

                AddAvailableItems(CommonListKey(ItemsListType.Buy), ref __result);

                AddAvailableItems(TraderListKey(__instance.m_name, ItemsListType.Buy), ref __result);
            }
        }

        private static void AddAvailableItems(string listKey, ref List<Trader.TradeItem> __result)
        {
            if (!tradeableItems.ContainsKey(listKey))
                return;

            foreach (TradeableItem item in tradeableItems[listKey])
            {
                if (!IsItemToSell(item, out ItemDrop prefab))
                    continue;

                if (__result.Exists(x => x.m_prefab == prefab))
                {
                    Trader.TradeItem itemTrader = __result.First(x => x.m_prefab == prefab);
                    itemTrader.m_price = item.price;
                    itemTrader.m_stack = item.stack;
                    itemTrader.m_requiredGlobalKey = item.requiredGlobalKey;
                }
                else
                {
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

        private static bool IsItemToSell(TradeableItem item, out ItemDrop itemDrop)
        {
            itemDrop = null;
            if (!string.IsNullOrEmpty(item.requiredGlobalKey) && !ZoneSystem.instance.GetGlobalKey(item.requiredGlobalKey))
                return false;

            GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(item.prefab);

            if (itemPrefab == null)
                return false;

            itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
                return false;

            return !checkForDiscovery.Value || IgnoreItemDiscovery(item.prefab.ToLower()) || Player.m_localPlayer.IsMaterialKnown(itemDrop.m_itemData.m_shared.m_name);
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateSellButton))]
        public static class StoreGui_UpdateSellButton_Patch
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                UpdateSellButton(__instance);

                RepairPanel.Update(__instance);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
        public static class StoreGui_Show_Patch
        {
            static void Postfix(StoreGui __instance, Trader trader)
            {
                if (!modEnabled.Value)
                    return;

                if (!__instance.m_rootPanel.activeSelf)
                    return;

                storeName.SetText(trader.GetHoverName());
                playerName.SetText(Player.m_localPlayer.GetPlayerName());

                sellPanel.SetActive(value: true);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Hide))]
        public static class StoreGui_Hide_Patch
        {
            static void Postfix()
            {
                if (!modEnabled.Value)
                    return;

                sellPanel.SetActive(value: false);
                AmountDialog.Close();
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateRecipeGamepadInput))]
        public static class StoreGui_UpdateRecipeGamepadInput_Patch
        {
            static void Postfix()
            {
                if (!modEnabled.Value) return;

                if (sellItemList.Count > 0)
                {
                    if (ZInput.GetButtonDown("JoyRStickDown") || ZInput.GetButtonDown("JoyDPadDown") && ZInput.GetButtonDown("JoyAltPlace"))
                    {
                        SelectItem(Mathf.Min(sellItemList.Count - 1, GetSelectedItemIndex() + 1), center: true);
                    }

                    if (ZInput.GetButtonDown("JoyRStickUp") || ZInput.GetButtonDown("JoyDPadUp") && ZInput.GetButtonDown("JoyAltPlace"))
                    {
                        SelectItem(Mathf.Max(0, GetSelectedItemIndex() - 1), center: true);
                    }
                }
            }
        }

    }
}
