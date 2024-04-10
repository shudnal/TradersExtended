using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using static TradersExtended.TradersExtended;
using GUIFramework;

namespace TradersExtended
{
    internal class StorePanel
    {
        private const float positionDelta = 35f;

        private static GameObject sellPanel;
        private static Button sellButton;

        private static TMP_Text storeName;
        private static TMP_Text playerName;

        private static ScrollRectEnsureVisible itemEnsureVisible;
        private static RectTransform listRoot;
        private static GameObject listElement;

        private static GuiInputField traderFilter;
        private static GuiInputField playerFilter;

        private static bool epicLootEnabled;

        private static readonly List<GameObject> sellItemList = new List<GameObject>();
        private static readonly List<ItemDrop.ItemData> tempItems = new List<ItemDrop.ItemData>();
        private static readonly Dictionary<string, Dictionary<int, int>> tempItemsPrice = new Dictionary<string, Dictionary<int, int>>();

        private static ItemDrop.ItemData selectedItem;
        private static int selectedItemIndex = -1;

        public static bool IsOpen()
        {
            return sellPanel != null && sellPanel.gameObject.activeInHierarchy;
        }

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
            List<ItemDrop.ItemData> availableItems = tempItems;
            for (int i = 0; i < availableItems.Count; i++)
                if (availableItems[i] == selectedItem)
                    result = i;

            return result;
        }

        public static void SelectItem(int index, bool center)
        {
            if (sellItemList.Count == 0)
                index = -1;

            center = center || index == 0;

            for (int i = 0; i < sellItemList.Count; i++)
                sellItemList[i].transform.Find("selected").gameObject.SetActive(i == index);

            if (center && index >= 0)
                itemEnsureVisible.CenterOnItem(sellItemList[index].transform as RectTransform);

            selectedItem = (index < 0) ? null : tempItems[index];
        }

        public static void SellSelectedItem(StoreGui __instance)
        {
            if (ItemPrice(selectedItem) == 0)
                return;

            ItemDrop m_coinPrefab = __instance.m_coinPrefab;
            if (m_coinPrefab == null)
            {
                logger.LogWarning($"No m_coinPrefab is setted in StoreGui");
                return;
            }

            selectedItemIndex = GetSelectedItemIndex();

            GetSellItemPriceStack(selectedItem, out int itemPrice, out int itemStack);

            Player.m_localPlayer.GetInventory().RemoveItem(selectedItem, itemStack);
            Player.m_localPlayer.GetInventory().AddItem(m_coinPrefab.gameObject.name, itemPrice, m_coinPrefab.m_itemData.m_quality, m_coinPrefab.m_itemData.m_variant, 0L, "");
            
            string text = itemStack <= 1 ? selectedItem.m_shared.m_name : $"{selectedItem.m_stack}x{selectedItem.m_shared.m_name}";

            __instance.m_sellEffects.Create(__instance.transform.position, Quaternion.identity);
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_sold", text, itemPrice.ToString()), 0, selectedItem.m_shared.m_icons[0]);
            __instance.m_trader.OnSold();
            Gogan.LogEvent("Game", "SoldItem", text, 0L);

            __instance.FillList();
        }

        private static void AddItemToSellList(TradeableItem item)
        {
            if (item.price == 0 || item.stack == 0)
                return;

            if (!string.IsNullOrEmpty(item.requiredGlobalKey) && !ZoneSystem.instance.GetGlobalKey(item.requiredGlobalKey))
                return;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
            if (prefab == null)
                return;

            string name = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name;

            // While this data structure supports several price per stack current sell list implementation does not
            // Maybe later
            if (tempItemsPrice.ContainsKey(name))
                return;

            tempItemsPrice.Add(name, new Dictionary<int, int>());

            if (!tempItemsPrice[name].ContainsKey(item.stack))
            {
                if (item.stack == 1)
                {
                    Player.m_localPlayer.GetInventory().GetAllItems(name, tempItems);
                    tempItemsPrice[name].Add(item.stack, item.price);
                }
                else if (Player.m_localPlayer.GetInventory().CountItems(name) >= item.stack)
                {
                    tempItems.Add(Player.m_localPlayer.GetInventory().GetItem(name));
                    tempItemsPrice[name].Add(item.stack, item.price);
                }
            }
        }

        public static void FillSellableList(StoreGui __instance)
        {
            foreach (GameObject item in sellItemList)
                UnityEngine.Object.Destroy(item);

            sellItemList.Clear();

            tempItems.Clear();
            tempItemsPrice.Clear();

            if (sellableItems.ContainsKey(TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)))
                sellableItems[TraderListKey(__instance.m_trader.m_name, ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            if (sellableItems.ContainsKey(CommonListKey(ItemsListType.Sell)))
                sellableItems[CommonListKey(ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            bool filterSellList = !String.IsNullOrWhiteSpace(playerFilter.text);
            for (int i = tempItems.Count - 1; i >= 0; i--)
            {
                ItemDrop.ItemData tradeItem = tempItems[i];
                if (tradeItem.m_shared.m_name == __instance.m_coinPrefab.m_itemData.m_shared.m_name)
                    tempItems.RemoveAt(i);
                else if (filterSellList && Localization.instance.Localize(tradeItem.m_shared.m_name).ToLower().IndexOf(playerFilter.text.ToLower()) == -1)
                    tempItems.RemoveAt(i);
            }

            float b = tempItems.Count * __instance.m_itemSpacing;
            b = Mathf.Max(__instance.m_itemlistBaseSize, b);
            listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, b);
            for (int i = 0; i < tempItems.Count; i++)
            {
                ItemDrop.ItemData tradeItem = tempItems[i];

                GameObject element = UnityEngine.Object.Instantiate(listElement, listRoot);
                element.SetActive(value: true);
                (element.transform as RectTransform).anchoredPosition = new Vector2(0f, (float)i * (0f - __instance.m_itemSpacing));

                GetSellItemPriceStack(tradeItem, out int itemPrice, out int itemStack);

                bool canSell = TraderCoins.CanSell(itemPrice);

                Image component = element.transform.Find("icon").GetComponent<Image>();
                component.sprite = tradeItem.GetIcon();
                component.color = (canSell ? Color.white : new Color(1f, 0f, 1f, 0f));

                string text = Localization.instance.Localize(tradeItem.m_shared.m_name);

                if (qualityMultiplier.Value >= 0f && tradeItem.m_quality > 1)
                    text += $" <color=#add8e6ff>({tradeItem.m_quality})</color>";

                if (itemStack > 1)
                    text += " x" + ItemStack(tradeItem);

                TMP_Text component2 = element.transform.Find("name").GetComponent<TMP_Text>();
                component2.SetText(text);
                element.GetComponent<UITooltip>().Set(tradeItem.m_shared.m_name, tradeItem.GetTooltip(), __instance.m_tooltipAnchor);
                TMP_Text component3 = element.transform.Find("coin_bkg").Find("price").GetComponent<TMP_Text>();
                component3.SetText(itemPrice.ToString());
                if (!canSell)
                    component3.color = Color.grey;

                element.GetComponent<Button>().onClick.AddListener(delegate
                {
                    OnSelectedItem(element);
                });

                sellItemList.Add(element);
            }

            if (selectedItemIndex == -1)
                selectedItemIndex = GetSelectedItemIndex();

            SelectItem(Mathf.Min(tempItems.Count - 1, selectedItemIndex), center: false);
        }

        private static void GetSellItemPriceStack(ItemDrop.ItemData tradeItem, out int itemPrice, out int itemStack)
        {
            itemPrice = Math.Max((int)(ItemPrice(tradeItem) * TraderCoins.GetPriceFactor(buyPrice: false)), 1);
            itemStack = ItemStack(tradeItem);
        }

        public static void UpdateSellButton()
        {
            sellButton.interactable = selectedItem != null;
        }

        private static int ItemPrice(ItemDrop.ItemData tradeItem)
        {
            if (tradeItem == null)
                return 0;

            int price = 0;
            try
            {
                KeyValuePair<int, int> stackPrice = tempItemsPrice[tradeItem.m_shared.m_name].First();
                price = (stackPrice.Key == 1) ? stackPrice.Value * tradeItem.m_stack : stackPrice.Value;
                price += Math.Max((int)(qualityMultiplier.Value * price * (tradeItem.m_quality - 1)), 0);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Cannot find item {tradeItem.m_shared.m_name} price: {ex}");
            }

            return price;
        }

        private static int ItemStack(ItemDrop.ItemData tradeItem)
        {
            try
            {
                KeyValuePair<int, int> stackPrice = tempItemsPrice[tradeItem.m_shared.m_name].First();
                return (stackPrice.Key == 1) ? tradeItem.m_stack : stackPrice.Key;
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Cannot find item {tradeItem.m_shared.m_name} stack: {ex}");
            }

            return 1;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Awake))]
        public static class StoreGui_Awake_InitializePanel
        {
            private static GuiInputField InitFilterField(Transform parent)
            {
                // Add filter field for player
                GameObject filterField = UnityEngine.Object.Instantiate(TextInput.instance.m_inputField.gameObject, parent);
                filterField.name = "FilterField";
                filterField.transform.localPosition = new Vector3(125f, -16f - positionDelta, 0);
                filterField.transform.SetSiblingIndex(filterField.transform.parent.Find("topic").GetSiblingIndex() + 1);

                RectTransform playerFilterRT = filterField.GetComponent<RectTransform>();
                playerFilterRT.anchorMin = new Vector2(1f, 0.5f);
                playerFilterRT.anchorMax = new Vector2(0f, 0.5f);
                playerFilterRT.sizeDelta -= new Vector2(0f, 10f);

                GuiInputField filter = filterField.GetComponent<GuiInputField>();
                filter.VirtualKeyboardTitle = "$keyboard_FilterField";
                filter.transform.Find("Text Area/Placeholder").GetComponent<TMP_Text>().SetText(Localization.instance.Localize("$keyboard_FilterField"));

                return filter;
            }

            [HarmonyPriority(Priority.First)]
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return;

                FillConfigLists();

                // Add copy of main panel to use as sell list
                sellPanel = UnityEngine.Object.Instantiate(__instance.m_rootPanel, __instance.m_rootPanel.transform);
                sellPanel.transform.localPosition = new Vector3(250, 0, 0);
                sellPanel.name = "StoreSell";
                sellPanel.SetActive(true);

                // Expand to fit new filter fields
                __instance.m_rootPanel.GetComponent<RectTransform>().sizeDelta += new Vector2(0f, positionDelta);
                __instance.m_listElement.GetComponent<RectTransform>().localPosition -= new Vector3(0f, positionDelta, 0f);

                // Remove redundant objects
                UnityEngine.Object.Destroy(sellPanel.transform.Find("SellPanel").gameObject);
                UnityEngine.Object.Destroy(sellPanel.transform.Find("border (1)").gameObject);
                UnityEngine.Object.Destroy(sellPanel.transform.Find("bkg").gameObject);

                __instance.m_rootPanel.transform.Find("ItemList").localPosition -= new Vector3(0f, positionDelta, 0f);
                sellPanel.transform.Find("ItemList").localPosition -= new Vector3(0f, positionDelta, 0f);
                sellPanel.transform.Find("coins").GetComponent<RectTransform>().localPosition -= new Vector3(0f, positionDelta, 0f);

                Transform items = sellPanel.transform.Find("ItemList/Items");

                // Link objects
                listRoot = items.Find("ListRoot").GetComponent<RectTransform>();
                listElement = items.Find("ItemElement").gameObject;
                itemEnsureVisible = items.GetComponent<ScrollRectEnsureVisible>();

                storeName = __instance.m_rootPanel.transform.Find("topic").GetComponent<TMP_Text>();
                playerName = sellPanel.transform.Find("topic").GetComponent<TMP_Text>();
                
                TraderCoins.playerCoins = sellPanel.transform.Find("coins/coins").GetComponent<TMP_Text>();
                TraderCoins.traderCoins = __instance.m_coinText;
                TraderCoins.traderCoinsPanel = __instance.m_rootPanel.transform.Find("coins").gameObject;

                // Prepare new sell button
                Transform sellPanelTransform = sellPanel.transform.Find("BuyButton");
                sellPanelTransform.Find("Text").GetComponent<TMP_Text>().SetText(Localization.instance.Localize("$store_sell"));
                sellPanelTransform.GetComponent<UIGamePad>().m_zinputKey = "JoyButtonX";
                sellPanelTransform.localPosition -= new Vector3(0f, positionDelta, 0f);

                // Make sell button into repair button
                GameObject repairPanel = RepairPanel.RepurposeSellButton(__instance);
                repairPanel.transform.localPosition -= new Vector3(0f, positionDelta, 0f);

                // Set handler to sell button
                sellButton = sellPanelTransform.GetComponent<Button>();
                sellButton.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.Off);
                sellButton.onClick.AddListener(delegate
                {
                    __instance.OnSellItem();
                });

                // Add filter fields
                traderFilter = InitFilterField(__instance.m_rootPanel.transform);
                traderFilter.onValueChanged.AddListener(delegate
                { 
                    __instance.FillList();
                });

                playerFilter = InitFilterField(sellPanel.transform);
                playerFilter.onValueChanged.AddListener(delegate
                {
                    FillSellableList(__instance);
                });

                // Copy gamepad hint from Craft button and replace original hint
                UIGamePad component = sellButton.GetComponent<UIGamePad>();
                Vector3 position = component.m_hint.transform.localPosition;
                UnityEngine.Object.Destroy(component.m_hint);

                component.m_hint = UnityEngine.Object.Instantiate(InventoryGui.instance.m_craftButton.GetComponent<UIGamePad>().m_hint, sellButton.transform);
                component.m_hint.transform.localPosition = position;
                component.m_hint.name = component.m_hint.name.Replace("(clone)", "");

                // Extend the borders
                __instance.m_rootPanel.transform.Find("border (1)").GetComponent<RectTransform>().anchorMax = new Vector2(2, 1);

                // Init amount dialog
                GameObject amountDialog = AmountDialog.Init(__instance);

                // Add amount dialog to block gamepad input of buttons
                __instance.m_rootPanel.transform.Find("BuyButton").GetComponent<UIGamePad>().m_blockingElements.Add(amountDialog);
                sellButton.GetComponent<UIGamePad>().m_blockingElements.Add(amountDialog);
                RepairPanel.AddButtonBlocker(amountDialog);

                // Move store to the left to create space between
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
                            __instance.m_rootPanel.transform.localPosition -= new Vector3(traderRepair.Value ? 146f : 100f, 0f, 0f);
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
            private static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
            {
                if (!modEnabled.Value)
                    return;

                if (disableVanillaItems.Value)
                    __result.Clear();

                AddAvailableItems(CommonListKey(ItemsListType.Buy), __result);

                AddAvailableItems(TraderListKey(__instance.m_name, ItemsListType.Buy), __result);
            }
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_
        {
            [HarmonyPriority(Priority.Last)]
            public static void Postfix(List<Trader.TradeItem> __result)
            {
                if (!modEnabled.Value)
                    return;

                float factor = TraderCoins.GetPriceFactor(buyPrice: true);
                for (int i = __result.Count - 1; i >= 0; i--)
                    if (!String.IsNullOrWhiteSpace(traderFilter.text) && Localization.instance.Localize(__result[i].m_prefab.m_itemData.m_shared.m_name).ToLower().IndexOf(traderFilter.text.ToLower()) == -1)
                        __result.RemoveAt(i);
                    else if (traderUseFlexiblePricing.Value)
                    {
                        __result[i] = JsonUtility.FromJson<Trader.TradeItem>(JsonUtility.ToJson(__result[i]));
                        __result[i].m_price = Math.Max((int)(__result[i].m_price * factor), 1);
                        __result[i].m_stack = Math.Min(__result[i].m_stack, __result[i].m_prefab.m_itemData.m_shared.m_maxStackSize);
                    }
            }
        }

        private static void AddAvailableItems(string listKey, List<Trader.TradeItem> __result)
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
            private static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                UpdateSellButton();

                RepairPanel.Update(__instance);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
        public static class StoreGui_Show_Patch
        {
            private static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (!IsOpen())
                    return;

                TraderCoins.UpdateTraderCoinsVisibility();

                UpdateNames();

                playerFilter.SetTextWithoutNotify("");
                traderFilter.SetTextWithoutNotify("");
            }
        }

        public static void UpdateNames()
        {
            string traderTopic = StoreGui.instance.m_trader?.GetHoverName();
            string playerTopic = Player.m_localPlayer.GetPlayerName();

            if (traderUseCoins.Value && traderUseFlexiblePricing.Value)
            {
                traderTopic += GetPriceFactorString(TraderCoins.GetPriceFactor(buyPrice: true), reversed: true);
                playerTopic += GetPriceFactorString(TraderCoins.GetPriceFactor(buyPrice: false));
            }

            storeName.SetText(traderTopic);
            playerName.SetText(playerTopic);

            string GetPriceFactorString(float factor, bool reversed = false)
            {
                if (factor == 1f)
                    return "";

                return $" · <color={((reversed && factor < 1) || (!reversed && factor > 1) ? "green" : "red")}>{(factor - 1f) * 100f:+0;-0}</color>%";
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Hide))]
        public static class StoreGui_Hide_Patch
        {
            private static bool Prefix(Trader ___m_trader, float ___m_hideDistance)
            {
                if (!modEnabled.Value)
                    return true;

                if (Vector3.Distance(___m_trader.transform.position, Player.m_localPlayer.transform.position) > ___m_hideDistance)
                    return true;

                if (AmountDialog.IsOpen())
                    return false;

                return true;
            }

            private static void Postfix()
            {
                if (!modEnabled.Value)
                    return;

                // In case hiding was stopped
                if (IsOpen())
                    return;
                
                AmountDialog.Close();

                TraderCoins.UpdateTraderCoins();
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateRecipeGamepadInput))]
        public static class StoreGui_UpdateRecipeGamepadInput_SellListGamepadNavigation
        {
            private static bool Prefix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return true;

                if (AmountDialog.IsOpen() || Console.IsVisible())
                    return false;

                if (sellItemList.Count == 0)
                    return true;

                if (ZInput.GetButtonDown("JoyRStickDown") || ZInput.GetButtonDown("JoyDPadDown") && ZInput.GetButton("JoyLTrigger"))
                {
                    SelectItem(Mathf.Min(sellItemList.Count - 1, GetSelectedItemIndex() + 1), center: true);
                    return false;
                }

                if (ZInput.GetButtonDown("JoyRStickUp") || ZInput.GetButtonDown("JoyDPadUp") && ZInput.GetButton("JoyLTrigger"))
                {
                    SelectItem(Mathf.Max(0, GetSelectedItemIndex() - 1), center: true);
                    return false;
                }

                if (ZInput.GetButtonDown("JoyButtonA") && ZInput.GetButton("JoyLTrigger"))
                {
                    AmountDialog.Open(__instance);
                    ZInput.ResetButtonStatus("JoyButtonA");
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.SellItem))]
        public static class StoreGui_SellItem_SellItemFromSellableList
        {
            private static bool Prefix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return true;

                if (!AmountDialog.IsOpen())
                    SellSelectedItem(__instance);

                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.GetSelectedItemIndex))]
        public static class StoreGui_GetSelectedItemIndex_GamePadScrollFix
        {
            private static bool Prefix(ref int __result, List<GameObject> ___m_itemList)
            {
                if (!modEnabled.Value)
                    return true;

                __result = -1;
                for (int i = 0; i < ___m_itemList.Count; i++)
                    if (___m_itemList[i].transform.Find("selected").gameObject.activeSelf)
                    {
                        __result = i;
                        break;
                    }

                return false;
            }
        }

        [HarmonyPatch(typeof(Chat), nameof(Chat.HasFocus))]
        public static class Chat_HasFocus_ImpersonateChatFocus
        {
            private static void Postfix(ref bool __result)
            {
                if (!modEnabled.Value)
                    return;

                if (playerFilter != null && traderFilter != null && StoreGui.IsVisible())
                    __result = __result || playerFilter.isFocused || traderFilter.isFocused;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.SelectItem))]
        public static class StoreGui_SelectItem_FixOutOfRangeForEmptyList
        {
            private static void Prefix(List<GameObject> ___m_itemList, ref int index, ref bool center)
            {
                if (!modEnabled.Value)
                    return;

                if (___m_itemList.Count == 0)
                    index = -1;
                else
                    index = Mathf.Clamp(index, 0, ___m_itemList.Count - 1);

                center = center || index == 0;
            }
        }        
    }
}
