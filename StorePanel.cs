using HarmonyLib;
using BepInEx;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine.UI;
using UnityEngine;
using static TradersExtended.TradersExtended;
using GUIFramework;
using System.Reflection;

namespace TradersExtended
{
    internal static class StorePanel
    {
        public class ItemToSell
        {
            public enum ItemType
            {
                Single,
                Stack,
                Combined
            }

            private const string c_buybackItem = "buybackItem";

            public ItemType itemType = ItemType.Single;
            public ItemDrop.ItemData item;
            public int stack;
            public int price;
            public int amount;
            public int quality;
            public int pricePerItem;

            public ItemToSell Clone()
            {
                ItemToSell obj = MemberwiseClone() as ItemToSell;
                obj.item = item.Clone();
                return obj;
            }

            public static bool IsBuyBackItem(Trader.TradeItem item)
            {
                return item?.m_requiredGlobalKey == c_buybackItem;
            }

            public static Trader.TradeItem SetBuyBackItem(Trader.TradeItem item)
            {
                item.m_requiredGlobalKey = c_buybackItem;
                return item;
            }
        }

        private const float positionDelta = 35f;

        private static GameObject sellPanel;
        private static Button sellButton;

        private static TMP_Text storeName;
        private static TMP_Text playerName;

        private static ScrollRectEnsureVisible itemEnsureVisible;
        private static RectTransform listRoot;
        private static GameObject listElement;
        private static RectTransform tooltipAnchor;

        private static GuiInputField traderFilter;
        private static GuiInputField playerFilter;

        internal static readonly List<GameObject> sellItemList = new List<GameObject>();
        internal static readonly List<ItemToSell> tempItems = new List<ItemToSell>();
        internal static readonly Dictionary<string, Dictionary<int, int>> tempItemsPrice = new Dictionary<string, Dictionary<int, int>>();

        internal static ItemToSell selectedItem;
        private static int selectedItemIndex = -1;

        private static ItemToSell buybackItem;

        private static Func<bool> _adventureModeEnabled;
        private static Vector3 defaultStorePosition;

        public static bool AdventureModeEnabled(Trader trader) => _adventureModeEnabled != null && Utils.GetPrefabName(trader.gameObject) == "Haldor" && _adventureModeEnabled();

        public static bool IsOpen() => sellPanel != null && sellPanel.gameObject.activeInHierarchy;

        public static void OnSelectedItem(GameObject button)
        {
            int index = FindSelectedRecipe(button);
            SelectItem(index, center: false);
            AmountDialog.OnSelectedTradeableItemClick(sellDialog: true);
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
            for (int i = 0; i < tempItems.Count; i++)
                if (tempItems[i] == selectedItem)
                    result = i;

            return result;
        }

        public static void SelectItem(int index, bool center)
        {
            if (sellItemList.Count == 0 || index >= sellItemList.Count)
                index = -1;

            center = center || index == 0;

            for (int i = 0; i < sellItemList.Count; i++)
                sellItemList[i]?.transform.Find("selected")?.gameObject.SetActive(i == index);

            if (center && index >= 0 && sellItemList[index] != null)
                itemEnsureVisible?.CenterOnItem(sellItemList[index].transform as RectTransform);

            selectedItem = (index < 0) ? null : tempItems[index];

            if (index >= 0)
            {
                AmountDialog.SetSellState(sellDialog: true);
                StoreGui.instance.SelectItem(-1, center: false);
            }
        }

        public static void SellSelectedItem(StoreGui __instance)
        {
            ItemDrop m_coinPrefab = __instance.m_coinPrefab;
            if (m_coinPrefab == null)
            {
                logger.LogWarning($"No m_coinPrefab set in StoreGui");
                return;
            }

            if (selectedItem == null)
                return;

            selectedItemIndex = GetSelectedItemIndex();

            if (enableBuyBack.Value)
                buybackItem = selectedItem.Clone();

            if (selectedItem.itemType == ItemToSell.ItemType.Single)
                Player.m_localPlayer.GetInventory().RemoveItem(selectedItem.item);
            else if (selectedItem.itemType == ItemToSell.ItemType.Stack)
                Player.m_localPlayer.GetInventory().RemoveItem(selectedItem.item.m_shared.m_name, selectedItem.stack, selectedItem.quality == 0 ? -1 : selectedItem.quality);
            else if (selectedItem.itemType == ItemToSell.ItemType.Combined)
                Player.m_localPlayer.GetInventory().RemoveItem(selectedItem.item.m_shared.m_name, selectedItem.amount, selectedItem.quality == 0 ? -1 : selectedItem.quality);

            int coins = Player.m_localPlayer.GetInventory().CountItems(m_coinPrefab.m_itemData.m_shared.m_name);
            if (Player.m_localPlayer.GetInventory().AddItem(m_coinPrefab.gameObject.name, selectedItem.price, m_coinPrefab.m_itemData.m_quality, m_coinPrefab.m_itemData.m_variant, 0L, "") == null)
            {
                int spawnCoins = selectedItem.price + coins - Player.m_localPlayer.GetInventory().CountItems(m_coinPrefab.m_itemData.m_shared.m_name);
                int count = spawnCoins / m_coinPrefab.m_itemData.m_shared.m_maxStackSize;
                
                GameObject coinPrefab = ZNetScene.instance.GetPrefab(m_coinPrefab.gameObject.name);
                if (coinPrefab)
                {
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "$msg_dropped: $item_coins x" + spawnCoins);
                    while (spawnCoins > 0)
                    {
                        Vector3 vector = UnityEngine.Random.insideUnitSphere * ((count == 1) ? 0f : 0.5f);
                        GameObject gameObject = UnityEngine.Object.Instantiate(coinPrefab, Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 2f + Vector3.up + vector, Quaternion.identity);
                        ItemDrop coinDrop = gameObject.GetComponent<ItemDrop>();
                        coinDrop.m_itemData.m_stack = Mathf.Min(spawnCoins, coinDrop.m_itemData.m_shared.m_maxStackSize);
                        spawnCoins -= coinDrop.m_itemData.m_stack;
                    }
                }
            }
            
            string text = selectedItem.stack <= 1 ? selectedItem.item.m_shared.m_name : $"{selectedItem.stack}x{selectedItem.item.m_shared.m_name}";

            __instance.m_sellEffects.Create(__instance.transform.position, Quaternion.identity);
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_sold", text, selectedItem.price.ToString()), 0, selectedItem.item.GetIcon());
            __instance.m_trader.OnSold();
            Gogan.LogEvent("Game", "SoldItem", text, 0L);

            __instance.FillList();
        }

        private static void AddItemToSellList(TradeableItem item)
        {
            if (item.price == 0 || item.stack == 0)
                return;

            if (!string.IsNullOrEmpty(item.requiredGlobalKey) && item.requiredGlobalKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => !ZoneSystem.instance.GetGlobalKey(s)))
                return;

            if (!string.IsNullOrEmpty(item.notRequiredGlobalKey) && item.notRequiredGlobalKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => ZoneSystem.instance.GetGlobalKey(s)))
                return;

            if (!string.IsNullOrEmpty(item.requiredPlayerKey) && item.requiredPlayerKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => !Player.m_localPlayer.HaveUniqueKey(s)))
                return;

            if (!string.IsNullOrEmpty(item.notRequiredPlayerKey) && item.notRequiredPlayerKey.Split(',').Select(s => s.Trim()).Where(s => !s.IsNullOrWhiteSpace()).Any(s => Player.m_localPlayer.HaveUniqueKey(s)))
                return;

            if (!TryGetPriceKey(item, out string key))
                return;

            if (!tempItemsPrice.ContainsKey(key))
                tempItemsPrice[key] = new Dictionary<int, int>();

            tempItemsPrice[key][item.stack] = item.price;
        }

        private static bool TryGetPriceKey(TradeableItem item, out string key)
        {
            key = "";

            GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
            if (prefab == null)
                return false;

            ItemDrop.ItemData itemData = prefab.GetComponent<ItemDrop>().m_itemData;
            if (itemData.m_shared.m_maxStackSize == 1 && item.stack != 1)
                return false;

            key = GetPriceKey(itemData, item.quality);

            return true;
        }

        private static string GetPriceKey(ItemDrop.ItemData itemData, int quality)
        {
            return quality > 0 ? itemData.m_shared.m_name + "-" + quality : itemData.m_shared.m_name;
        }

        private static bool IgnoreItemForSell(ItemDrop.ItemData item)
        {
            if (item.m_shared.m_name == StoreGui.instance.m_coinPrefab.m_itemData.m_shared.m_name)
                return true;
            else if (!String.IsNullOrWhiteSpace(playerFilter.text) && Localization.instance.Localize(item.m_shared.m_name).ToLower().IndexOf(playerFilter.text.ToLower()) == -1)
                return true;
            else if (hideEquippedAndHotbarItems.Value && item.m_equipped) // Ignore currently equipped item
                return true;
            else if (hideEquippedAndHotbarItems.Value && item.m_gridPos.y == 0 && item.IsEquipable()) // Ignore equippable item from first row (hotbar)
                return true;
            else if (hideEquippedAndHotbarItems.Value && AzuExtendedPlayerInventory.API.GetSlots().GetItemFuncs.Where(func => func != null).Select(func => func(Player.m_localPlayer)).Contains(item))
                return true;
            else if (hideEquippedAndHotbarItems.Value && ExtraSlots.API.GetAllExtraSlotsItems().Contains(item))
                return true;

            return false;
        }

        private static Dictionary<int, int> GetStackPrices(ItemDrop.ItemData item, out int quality)
        {
            quality = item.m_quality;
            string key = GetPriceKey(item, quality);
            if (tempItemsPrice.ContainsKey(key))
                return tempItemsPrice[key];

            quality = 0;
            key = GetPriceKey(item, quality);
            if (tempItemsPrice.ContainsKey(key))
                return tempItemsPrice[key];

            return null;
        }

        private static void AddToSellList(ItemDrop.ItemData item, int itemStack, int itemPrice, float priceFactor, int quality, ItemToSell.ItemType itemType)
        {
            int price = itemPrice;

            if (itemType == ItemToSell.ItemType.Combined)
                price *= itemStack;

            if (itemType != ItemToSell.ItemType.Stack && qualityMultiplier.Value != 0 && quality == 0 && item.m_quality > 1)
                price += (int)(qualityMultiplier.Value * price * (item.m_quality - 1));

            price = Math.Max((int)(price * priceFactor), 1);

            if (itemType == ItemToSell.ItemType.Single)
                tempItems.Add(new ItemToSell()
                {
                    itemType = itemType,
                    item = item,
                    stack = itemStack,
                    price = price,
                    quality = quality
                });
            else if (itemType == ItemToSell.ItemType.Stack)
            {
                ItemToSell currentStack = tempItems.Find(tmpItem => tmpItem.itemType == itemType && 
                                                                    tmpItem.stack == itemStack && 
                                                                    tmpItem.item.m_shared.m_name == item.m_shared.m_name && 
                                                                    (quality == 0 || tmpItem.item.m_quality == quality) && 
                                                                    tmpItem.price == price);
                if (currentStack != null)
                    currentStack.amount += item.m_stack;
                else
                    tempItems.Add(new ItemToSell()
                    {
                        itemType = itemType,
                        item = item,
                        stack = itemStack,
                        price = price,
                        amount = item.m_stack,
                        quality = quality
                    });
            }
            else if (itemType == ItemToSell.ItemType.Combined)
            {
                ItemToSell currentStack = tempItems.Find(tmpItem => tmpItem.itemType == itemType && 
                                                                    tmpItem.item.m_shared.m_name == item.m_shared.m_name && 
                                                                    (quality == 0 || tmpItem.item.m_quality == quality) && 
                                                                    tmpItem.stack == 1);
                if (currentStack != null)
                {
                    currentStack.price += price;
                    currentStack.amount += itemStack;
                }
                else
                {
                    tempItems.Add(new ItemToSell()
                    {
                        itemType = itemType,
                        item = item,
                        stack = 1,
                        price = price,
                        amount = itemStack,
                        quality = quality,
                        pricePerItem = itemPrice + (qualityMultiplier.Value != 0 && quality == 0 && item.m_quality > 1 ? (int)(qualityMultiplier.Value * itemPrice * (item.m_quality - 1)) : 0)
                    });
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

            if (sellableItems.ContainsKey(TraderListKey(__instance.m_trader, ItemsListType.Sell)))
                sellableItems[TraderListKey(__instance.m_trader, ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            if (sellableItems.ContainsKey(CommonListKey(ItemsListType.Sell)))
                sellableItems[CommonListKey(ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            float priceFactor = TraderCoins.GetPriceFactor(buyPrice: false);

            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItemsSortedByName())
            {
                if (IgnoreItemForSell(item))
                    continue;

                Dictionary<int, int> stackPrices = GetStackPrices(item, out int quality);
                if (stackPrices == null)
                    continue;

                foreach (int stack in stackPrices.Keys.OrderBy(x => x))
                    if (stack == 1)
                        if (item.m_shared.m_maxStackSize == 1)
                            AddToSellList(item, 1, stackPrices[stack], priceFactor, quality, ItemToSell.ItemType.Single);
                        else
                            AddToSellList(item, item.m_stack, stackPrices[stack], priceFactor, quality, ItemToSell.ItemType.Combined);
                    else
                        AddToSellList(item, stack, stackPrices[stack], priceFactor, quality, ItemToSell.ItemType.Stack);
            }

            tempItems.RemoveAll(x => x.amount != 0 && x.amount < x.stack);

            float b = tempItems.Count * __instance.m_itemSpacing;
            b = Mathf.Max(__instance.m_itemlistBaseSize, b);
            listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, b);
            for (int i = 0; i < tempItems.Count; i++)
            {
                ItemToSell tradeItem = tempItems[i];

                GameObject element = UnityEngine.Object.Instantiate(listElement, listRoot);
                element.SetActive(value: true);

                RectTransform rectTransform = element.transform as RectTransform;
                float num2 = (__instance.m_listRoot.rect.width - rectTransform.rect.width) / 2f;
                rectTransform.anchoredPosition = new Vector2(num2, i * (0f - __instance.m_itemSpacing) - num2);

                bool canSell = TraderCoins.CanSell(tradeItem.price);

                Image component = element.transform.Find("icon").GetComponent<Image>();
                component.sprite = tradeItem.item.GetIcon();
                component.color = (canSell ? Color.white : new Color(1f, 0f, 1f, 0f));

                string text = Localization.instance.Localize(tradeItem.item.m_shared.m_name);

                if (tradeItem.quality > 1)
                    text += $" <color=#add8e6ff>({tradeItem.quality})</color>";

                if (tradeItem.stack > 1)
                    text += " x" + tradeItem.stack;

                if (tradeItem.amount > tradeItem.stack)
                    text += $" <color=#c0c0c0ff>({tradeItem.amount})</color>";

                TMP_Text component2 = element.transform.Find("name").GetComponent<TMP_Text>();
                component2.SetText(text);
                element.GetComponent<UITooltip>().Set(tradeItem.item.m_shared.m_name, tradeItem.item.GetTooltip(), tooltipAnchor);
                TMP_Text component3 = element.transform.Find("coin_bkg").Find("price").GetComponent<TMP_Text>();
                component3.SetText(tradeItem.price.ToString());
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

        public static void UpdateSellButton()
        {
            sellButton.interactable = selectedItem != null && TraderCoins.CanSell(selectedItem.price);
        }

        public static bool BuyBackItem(StoreGui store)
        {
            if (!ItemToSell.IsBuyBackItem(store.m_selectedItem))
                return false;

            bool result;
            if (buybackItem.itemType == ItemToSell.ItemType.Single)
                result = Player.m_localPlayer.GetInventory().AddItem(buybackItem.item);
            else
            {
                int variant = buybackItem.item.m_variant;
                int quality = buybackItem.quality == 0 ? buybackItem.item.m_quality : buybackItem.quality;
                int stack = buybackItem.itemType == ItemToSell.ItemType.Stack ? buybackItem.stack : buybackItem.amount;
                result = Player.m_localPlayer.GetInventory().AddItem(buybackItem.item.m_dropPrefab?.name, stack, quality, variant, buybackItem.item.m_crafterID, buybackItem.item.m_crafterName, buybackItem.item.m_pickedUp) != null;
            }
                
            if (result)
            {
                buybackItem = null;
                Player.m_localPlayer.GetInventory().RemoveItem(StoreGui.instance.m_coinPrefab.m_itemData.m_shared.m_name, store.m_selectedItem.m_price);
                store.m_selectedItem = null;
                StoreGui.instance.m_buyEffects.Create(StoreGui.instance.transform.position, Quaternion.identity);
                StoreGui.instance.FillList();
            }

            return result;
        }

        public static void SetStoreGuiPosition()
        {
            if (StoreGui.instance == null || StoreGui.instance.m_rootPanel == null)
                return;

            if (defaultStorePosition == Vector3.zero)
                defaultStorePosition = StoreGui.instance.m_rootPanel.transform.localPosition;

            if (fixedStoreGuiPosition.Value != Vector2.zero)
                StoreGui.instance.m_rootPanel.transform.localPosition = fixedStoreGuiPosition.Value;
            else
                StoreGui.instance.m_rootPanel.transform.localPosition = AdventureModeEnabled(StoreGui.instance.m_trader)
                                                                        ? defaultStorePosition - new Vector3(RepairPanel.TraderCanRepair(StoreGui.instance.m_trader) ? 146f : 100f, 0f, 0f) 
                                                                        : defaultStorePosition;
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
                filter.VirtualKeyboardTitle = "$menu_filter";
                filter.transform.Find("Text Area/Placeholder").GetComponent<TMP_Text>().SetText(Localization.instance.Localize("$menu_filter"));

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
                tooltipAnchor = sellPanel.transform.Find("TooltipAnchor").GetComponent<RectTransform>();

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
                if (epicLootPlugin != null)
                {
                    MethodInfo IsAdventureModeEnabledMethod = AccessTools.Method(epicLootPlugin.GetType(), "IsAdventureModeEnabled");
                    if (IsAdventureModeEnabledMethod != null)
                        _adventureModeEnabled = () => (bool)IsAdventureModeEnabledMethod.Invoke(null, null);
                }

                // Move original tooltip anchor to the side
                __instance.m_tooltipAnchor.anchorMax += new Vector2(1f, 0f);

                LogInfo($"StoreGui panel patched");

                SetupConfigWatcher();

                buybackItem = null;
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

                AddAvailableItems(TraderListKey(__instance, ItemsListType.Buy), __result);
            }
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_FilterAndFlexiblePrices
        {
            public static bool ItemIsValid(ItemDrop item)
            {
                try
                {
                    return item?.m_itemData?.GetIcon() != null;
                }
                catch
                {
                    return false;
                }
            }

            [HarmonyPriority(Priority.Last)]
            public static void Postfix(List<Trader.TradeItem> __result)
            {
                if (!modEnabled.Value)
                    return;

                float factor = TraderCoins.GetPriceFactor(buyPrice: true);
                for (int i = __result.Count - 1; i >= 0; i--)
                     if (!ItemIsValid(__result[i].m_prefab))
                         __result.RemoveAt(i);
                    else if(!String.IsNullOrWhiteSpace(traderFilter.text) && Localization.instance.Localize(__result[i].m_prefab.m_itemData.m_shared.m_name).ToLower().IndexOf(traderFilter.text.ToLower()) == -1)
                        __result.RemoveAt(i);
                    else
                    {
                        __result[i] = JsonUtility.FromJson<Trader.TradeItem>(JsonUtility.ToJson(__result[i]));
                        if (traderUseFlexiblePricing.Value)
                        {
                            __result[i].m_price = Math.Max((int)(__result[i].m_price * factor), 1);
                            TradeableItem.NormalizeStack(__result[i]);
                        }
                    }

                if (enableBuyBack.Value && buybackItem != null)
                    __result.Insert(0, ItemToSell.SetBuyBackItem(new Trader.TradeItem()
                    {
                        m_prefab = null,
                        m_stack = 0,
                        m_price = buybackItem.price
                    }));
            }
        }

        private static void AddAvailableItems(string listKey, List<Trader.TradeItem> __result)
        {
            if (tradeableItems.ContainsKey(listKey))
                tradeableItems[listKey].DoIf(item => item.IsItemToSell(), item => __result.Add(item.ToTradeItem()));
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

                SetStoreGuiPosition();
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

                return $" · <color=#{((reversed && factor < 1) || (!reversed && factor > 1) ? "80ff80fc" : "ff6464fc")}>{(factor - 1f) * 100f:+0;-0}</color>%";
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
            private static bool Prefix(StoreGui __instance, List<GameObject> ___m_itemList)
            {
                if (!modEnabled.Value)
                    return true;

                if (AmountDialog.IsOpen() || Console.IsVisible())
                    return false;

                if (ZInput.GetButtonDown("JoyButtonA") && ZInput.GetButton("JoyAltKeys"))
                {
                    AmountDialog.Open();
                    ZInput.ResetButtonStatus("JoyButtonA");
                    return false;
                }

                if (ZInput.GetButtonDown("JoyDPadDown"))
                {
                    if (GetSelectedItemIndex() != -1)
                        SelectItem(Mathf.Min(sellItemList.Count - 1, GetSelectedItemIndex() + 1), center: true);
                    else if (__instance.GetSelectedItemIndex() != -1)
                        __instance.SelectItem(Mathf.Min(___m_itemList.Count - 1, __instance.GetSelectedItemIndex() + 1), center: true);
                }

                if (ZInput.GetButtonDown("JoyDPadUp"))
                {
                    if (GetSelectedItemIndex() != -1)
                        SelectItem(Mathf.Max(0, GetSelectedItemIndex() - 1), center: true);
                    else if (__instance.GetSelectedItemIndex() != -1)
                        __instance.SelectItem(Mathf.Max(0, __instance.GetSelectedItemIndex() - 1), center: true);
                }

                if (___m_itemList.Count > 0)
                {
                    if (ZInput.GetButtonDown("JoyLStickDown"))
                    {
                        __instance.SelectItem(Mathf.Min(___m_itemList.Count - 1, __instance.GetSelectedItemIndex() + 1), center: true);
                    }

                    if (ZInput.GetButtonDown("JoyLStickUp"))
                    {
                        __instance.SelectItem(Mathf.Max(0, __instance.GetSelectedItemIndex() - 1), center: true);
                    }

                    if (ZInput.GetButtonDown("JoyDPadLeft") || ZInput.GetButtonDown("JoyRStickLeft"))
                    {
                        __instance.SelectItem(Mathf.Min(___m_itemList.Count - 1, Math.Max(__instance.GetSelectedItemIndex(), 0)), center: true);
                    }
                }

                if (sellItemList.Count > 0)
                {
                    if (ZInput.GetButtonDown("JoyDPadRight") || ZInput.GetButtonDown("JoyRStickRight"))
                    {
                        SelectItem(Mathf.Min(sellItemList.Count - 1, Math.Max(GetSelectedItemIndex(), 0)), center: true);
                    }

                    if (ZInput.GetButtonDown("JoyRStickDown"))
                    {
                        SelectItem(Mathf.Min(sellItemList.Count - 1, GetSelectedItemIndex() + 1), center: true);
                    }

                    if (ZInput.GetButtonDown("JoyRStickUp"))
                    {
                        SelectItem(Mathf.Max(0, GetSelectedItemIndex() - 1), center: true);
                    }
                }

                return false;
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
            private static void Prefix(StoreGui __instance, ref int index, ref bool center)
            {
                if (!modEnabled.Value)
                    return;

                if (index >= 0)
                {
                    if (__instance.m_itemList.Count == 0)
                        index = -1;
                    else
                        index = Mathf.Clamp(index, 0, __instance.m_itemList.Count - 1);
                }

                center = center || index == 0;

                if (index >= 0)
                {
                    AmountDialog.SetSellState(sellDialog: false);
                    SelectItem(-1, center: false);
                }
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
        public static class StoreGui_FillList_FillSellableList
        {
            private static readonly List<Trader.TradeItem> availableItems = new List<Trader.TradeItem>();

            public static bool Prefix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return true;

                int playerCoins = __instance.GetPlayerCoins();
                int num = __instance.GetSelectedItemIndex();
                
                availableItems.Clear();
                availableItems.AddRange(__instance.m_trader.GetAvailableItems());
                foreach (GameObject item in __instance.m_itemList)
                    UnityEngine.Object.Destroy(item);

                __instance.m_itemList.Clear();
                float b = availableItems.Count * __instance.m_itemSpacing;
                b = Mathf.Max(__instance.m_itemlistBaseSize, b);
                __instance.m_listRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, b);
                for (int i = 0; i < availableItems.Count; i++)
                {
                    Trader.TradeItem tradeItem = availableItems[i];

                    bool isBuyback = ItemToSell.IsBuyBackItem(tradeItem);

                    ItemDrop.ItemData itemData = isBuyback ? buybackItem.item : tradeItem.m_prefab.m_itemData;
                    int price = isBuyback ? buybackItem.price : tradeItem.m_price;
                    TradeableItem.GetStackQualityFromStack(tradeItem.m_stack, out int stack, out int quality);
                    if (isBuyback)
                    {
                        if (buybackItem.itemType == ItemToSell.ItemType.Single)
                        {
                            stack = itemData.m_stack;
                            quality = itemData.m_quality;
                        }
                        else if (buybackItem.itemType == ItemToSell.ItemType.Stack)
                        {
                            stack = buybackItem.stack;
                            quality = buybackItem.quality;
                        }
                        else if (buybackItem.itemType == ItemToSell.ItemType.Combined)
                        {
                            stack = buybackItem.amount;
                            quality = buybackItem.quality;
                        }
                    }

                    GameObject element = UnityEngine.Object.Instantiate(__instance.m_listElement, __instance.m_listRoot);
                    element.SetActive(value: true);
                    RectTransform rectTransform = element.transform as RectTransform;
                    float num2 = (__instance.m_listRoot.rect.width - rectTransform.rect.width) / 2f;
                    rectTransform.anchoredPosition = new Vector2(num2, i * (0f - __instance.m_itemSpacing) - num2);
                    bool available = price <= playerCoins;
                    Image component = element.transform.Find("icon").GetComponent<Image>();
                    component.sprite = itemData.GetIcon();
                    component.color = available ? (isBuyback ? colorBuybackHighlighted.Value : Color.white) : new Color(1f, 0f, 1f, 0f);
                    string text = Localization.instance.Localize(itemData.m_shared.m_name);

                    if (quality > 1)
                        text += $" <color=#add8e6ff>({quality})</color>";

                    if (stack > 1)
                        text += " x" + stack;

                    TMP_Text component2 = element.transform.Find("name").GetComponent<TMP_Text>();
                    component2.text = text;
                    component2.color = available ? (isBuyback ? colorBuybackText.Value : Color.white) : Color.grey;

                    string tooltip = ItemDrop.ItemData.GetTooltip(itemData, quality == 0 ? itemData.m_quality : quality, crafting: false, itemData.m_worldLevel);

                    element.GetComponent<UITooltip>().Set(itemData.m_shared.m_name, tooltip, __instance.m_tooltipAnchor);
                    TMP_Text component3 = Utils.FindChild(element.transform, "price").GetComponent<TMP_Text>();
                    component3.text = price.ToString();
                    if (!available)
                        component3.color = Color.grey;

                    element.GetComponent<Button>().onClick.AddListener(delegate
                    {
                        __instance.OnSelectedItem(element);
                    });

                    if (isBuyback)
                    {
                        ColorBlock colors = element.GetComponent<Button>().colors;
                        colors.normalColor = colorBuybackNormal.Value;
                        colors.highlightedColor = colorBuybackHighlighted.Value;
                        element.GetComponent<Button>().colors = colors;
                    }

                    __instance.m_itemList.Add(element);
                }

                FillSellableList(__instance);

                __instance.SelectItem(Mathf.Clamp(num, 0, __instance.m_itemList.Count - 1), center: false);

                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.BuySelectedItem))]
        public static class StoreGui_BuySelectedItem_TraderCoinsUpdate
        {
            public static bool isCalled = false;

            [HarmonyPriority(Priority.First)]
            public static bool Prefix(StoreGui __instance, ref Tuple<int, int> __state)
            {
                if (!modEnabled.Value)
                    return true;

                isCalled = true;

                if (__instance.m_selectedItem != null && __instance.CanAfford(__instance.m_selectedItem))
                {
                    if (BuyBackItem(__instance))
                        return false;

                    TradeableItem.GetStackQualityFromStack(__instance.m_selectedItem.m_stack, out int stack, out int quality);
                    if (quality != 0)
                    {
                        __state = Tuple.Create(__instance.m_selectedItem.m_stack, __instance.m_selectedItem.m_prefab.m_itemData.m_quality);
                        __instance.m_selectedItem.m_stack = stack;
                        __instance.m_selectedItem.m_prefab.m_itemData.m_quality = quality;
                    }
                }

                return true;
            }

            [HarmonyPriority(Priority.First)]
            public static void Postfix(StoreGui __instance, Tuple<int, int> __state)
            {
                isCalled = false;
                if (__state != null && __instance.m_selectedItem?.m_prefab?.m_itemData != null)
                {
                    __instance.m_selectedItem.m_stack = __state.Item1;
                    __instance.m_selectedItem.m_prefab.m_itemData.m_quality = __state.Item2;
                }
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ShowPickupMessage))]
        public static class Character_ShowPickupMessage_FixIncorrectStackMessage
        {
            private static void Prefix(ItemDrop.ItemData item, ref int amount)
            {
                if (!modEnabled.Value)
                    return;

                if (StoreGui.instance != null && StoreGui_BuySelectedItem_TraderCoinsUpdate.isCalled && StoreGui.instance.m_selectedItem != null && item == StoreGui.instance.m_selectedItem.m_prefab?.m_itemData)
                    amount = StoreGui.instance.m_selectedItem.m_stack;
            }
        }
    }
}
