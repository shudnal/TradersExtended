using BepInEx;
using GUIFramework;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TradersExtended.Compatibility;
using static TradersExtended.TradersExtended;

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
            public ItemDrop currency;

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

        private sealed class SellPrice
        {
            internal int Price;
            internal string CurrencyPrefab;
        }

        private const float positionDelta = 35f;
        private const string nonTeleportableIconName = "TradersExtended_NonTeleportable";

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
        private static readonly Dictionary<string, Dictionary<int, SellPrice>> tempItemsPrice = new Dictionary<string, Dictionary<int, SellPrice>>();

        internal static ItemToSell selectedItem;
        private static int selectedItemIndex = -1;

        private static ItemToSell buybackItem;

        private static Vector3 defaultStorePosition;

        public static bool AdventureModeEnabled(Trader trader) =>
            trader != null && TraderConfigManager.Get(trader).ShiftStoreGuiForEpicLoot && EpicLootCompat.IsAdventureModeEnabled();

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

        public static void SellSelectedItem(StoreGui storeGui)
        {
            if (selectedItem == null || Player.m_localPlayer == null)
                return;

            ItemDrop currency = selectedItem.currency ?? storeGui?.m_coinPrefab;
            if (currency == null)
            {
                logger.LogWarning("No currency prefab is available for the selected sell item");
                return;
            }

            selectedItemIndex = GetSelectedItemIndex();

            Inventory inventory = Player.m_localPlayer.GetInventory();
            int quality = selectedItem.quality == 0 ? -1 : selectedItem.quality;
            int amountToRemove = selectedItem.itemType == ItemToSell.ItemType.Stack
                ? selectedItem.stack
                : selectedItem.itemType == ItemToSell.ItemType.Combined
                    ? selectedItem.amount
                    : selectedItem.item.m_stack;
            bool canRemove = selectedItem.itemType == ItemToSell.ItemType.Single
                ? inventory.ContainsItem(selectedItem.item)
                : inventory.CountItems(selectedItem.item.m_shared.m_name, quality) >= amountToRemove;
            if (!canRemove)
            {
                storeGui.FillList();
                return;
            }

            ItemToSell pendingBuyback = TraderConfigManager.Get(storeGui.m_trader).EnableBuybackForLastItemSold
                ? selectedItem.Clone()
                : null;
            ItemDrop originalCurrency = storeGui.m_coinPrefab;
            storeGui.m_coinPrefab = currency;
            try
            {
                using (TraderCoins.BeginBalanceTransaction(TraderCoins.BalanceOperation.Receive))
                {
                    if (selectedItem.itemType == ItemToSell.ItemType.Single)
                    {
                        if (!inventory.RemoveItem(selectedItem.item))
                            return;
                    }
                    else
                    {
                        inventory.RemoveItem(selectedItem.item.m_shared.m_name, amountToRemove, quality);
                    }

                    if (pendingBuyback != null)
                    {
                        buybackItem = pendingBuyback;
                        BuybackManager.Set(storeGui.m_trader, pendingBuyback);
                    }

                    int currencyBefore = inventory.CountItems(currency.m_itemData.m_shared.m_name);
                    if (inventory.AddItem(currency.gameObject.name, selectedItem.price, currency.m_itemData.m_quality, currency.m_itemData.m_variant, 0L, "") == null)
                    {
                        int remainingCurrency = selectedItem.price + currencyBefore - inventory.CountItems(currency.m_itemData.m_shared.m_name);
                        int maximumStackSize = Math.Max(currency.m_itemData.m_shared.m_maxStackSize, 1);
                        int stackCount = Mathf.CeilToInt((float)remainingCurrency / maximumStackSize);

                        GameObject currencyPrefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(currency.gameObject.name) : null;
                        if (currencyPrefab == null)
                            currencyPrefab = currency.gameObject;

                        if (currencyPrefab != null && remainingCurrency > 0)
                        {
                            TraderCoins.UpdateTraderCoins(-remainingCurrency);
                            string currencyName = TraderCurrency.GetCurrencyName(currency);
                            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, $"$msg_dropped: {currencyName} x{remainingCurrency}");
                            while (remainingCurrency > 0)
                            {
                                Vector3 offset = UnityEngine.Random.insideUnitSphere * (stackCount == 1 ? 0f : 0.5f);
                                GameObject droppedObject = UnityEngine.Object.Instantiate(
                                    currencyPrefab,
                                    Player.m_localPlayer.transform.position + Player.m_localPlayer.transform.forward * 2f + Vector3.up + offset,
                                    Quaternion.identity);
                                ItemDrop droppedCurrency = droppedObject.GetComponent<ItemDrop>();
                                droppedCurrency.m_itemData.m_stack = Mathf.Min(remainingCurrency, maximumStackSize);
                                remainingCurrency -= droppedCurrency.m_itemData.m_stack;
                            }
                        }
                    }

                    string itemText = amountToRemove <= 1
                        ? selectedItem.item.m_shared.m_name
                        : $"{amountToRemove}x{selectedItem.item.m_shared.m_name}";

                    storeGui.m_sellEffects.Create(storeGui.transform.position, Quaternion.identity);
                    string soldMessage = Localization.instance.Localize("$msg_sold", itemText, selectedItem.price.ToString());
                    if (currency.m_itemData.m_shared.m_name != CoinsPatches.itemDropNameCoins)
                        soldMessage = $"{Localization.instance.Localize(itemText)}: {selectedItem.price} {TraderCurrency.GetCurrencyName(currency)}";
                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, soldMessage, 0, selectedItem.item.GetIcon());
                    storeGui.m_trader.OnSold();
                    Gogan.LogEvent("Game", "SoldItem", itemText, 0L);
                }
            }
            finally
            {
                storeGui.m_coinPrefab = originalCurrency;
            }

            storeGui.FillList();
        }

        private static void AddItemToSellList(TradeableItem item)
        {
            if (item == null || item.price <= 0 || item.stack <= 0 || !item.RequirementsMet())
                return;

            if (!TryGetPriceKey(item, out string key))
                return;

            if (!tempItemsPrice.ContainsKey(key))
                tempItemsPrice[key] = new Dictionary<int, SellPrice>();

            tempItemsPrice[key][item.stack] = new SellPrice
            {
                Price = item.price,
                CurrencyPrefab = item.currency
            };
        }

        private static bool TryGetPriceKey(TradeableItem item, out string key)
        {
            key = "";
            if (item == null || ObjectDB.instance == null || string.IsNullOrWhiteSpace(item.prefab))
                return false;

            GameObject prefab = ObjectDB.instance.GetItemPrefab(item.prefab);
            if (prefab == null || !prefab.TryGetComponent(out ItemDrop itemDrop))
                return false;

            ItemDrop.ItemData itemData = itemDrop.m_itemData;
            if (itemData.m_shared.m_maxStackSize == 1 && item.stack != 1)
                return false;

            key = GetPriceKey(itemData, item.quality);

            return true;
        }

        private static string GetPriceKey(ItemDrop.ItemData itemData, int quality)
        {
            return quality > 0 ? itemData.m_shared.m_name + "-" + quality : itemData.m_shared.m_name;
        }

        private static bool IgnoreItemForSell(ItemDrop.ItemData item, Trader trader)
        {
            if (!string.IsNullOrWhiteSpace(playerFilter.text) && Localization.instance.Localize(item.m_shared.m_name).IndexOf(playerFilter.text, StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            if (!TraderConfigManager.Get(trader).HideEquippedAndHotbarItems)
                return false;

            if (item.m_equipped) // Ignore currently equipped item
                return true;
            if (item.m_gridPos.y == 0 && item.IsEquipable()) // Ignore equippable item from first row (hotbar)
                return true;
            if (AzuExtendedPlayerInventory.API.GetSlots().GetItemFuncs.Where(func => func != null).Select(func => func(Player.m_localPlayer)).Contains(item))
                return true;
            if (ExtraSlots.API.GetAllExtraSlotsItems().Contains(item))
                return true;

            return false;
        }

        private static Dictionary<int, SellPrice> GetStackPrices(ItemDrop.ItemData item, out int quality)
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

        internal static int CalculateSellPrice(int basePrice, int amount)
        {
            return CalculateSellPrice(basePrice, amount, TraderCoins.GetPriceFactor(buyPrice: false));
        }

        private static int CalculateSellPrice(int basePrice, int amount, float priceFactor)
        {
            if (basePrice <= 0 || amount <= 0)
                return 0;

            double price = basePrice * (double)amount * priceFactor;
            if (double.IsNaN(price) || price >= int.MaxValue)
                return int.MaxValue;

            return Math.Max((int)Math.Ceiling(price), 1);
        }

        private static void AddToSellList(ItemDrop.ItemData item, int itemStack, int itemPrice, float priceFactor, int configuredQuality, ItemToSell.ItemType itemType, ItemDrop currency)
        {
            int effectiveQuality = configuredQuality > 0 ? configuredQuality : item.m_quality;
            int adjustedItemPrice = itemPrice;
            float configuredQualityMultiplier = TraderConfigManager.Get(StoreGui.instance?.m_trader).QualityMultiplier;
            if (itemType != ItemToSell.ItemType.Stack && configuredQualityMultiplier != 0 && configuredQuality == 0 && item.m_quality > 1)
                adjustedItemPrice += (int)(configuredQualityMultiplier * adjustedItemPrice * (item.m_quality - 1));

            int price = CalculateSellPrice(adjustedItemPrice, itemType == ItemToSell.ItemType.Combined ? itemStack : 1, priceFactor);

            if (itemType == ItemToSell.ItemType.Single)
                tempItems.Add(new ItemToSell()
                {
                    itemType = itemType,
                    item = item,
                    stack = itemStack,
                    price = price,
                    quality = effectiveQuality,
                    currency = currency
                });
            else if (itemType == ItemToSell.ItemType.Stack)
            {
                ItemToSell currentStack = tempItems.Find(tmpItem => tmpItem.itemType == itemType &&
                                                                    tmpItem.stack == itemStack &&
                                                                    tmpItem.item.m_shared.m_name == item.m_shared.m_name &&
                                                                    tmpItem.quality == effectiveQuality &&
                                                                    tmpItem.price == price &&
                                                                    SameCurrency(tmpItem.currency, currency));
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
                        quality = effectiveQuality,
                        currency = currency
                    });
            }
            else if (itemType == ItemToSell.ItemType.Combined)
            {
                ItemToSell currentStack = tempItems.Find(tmpItem => tmpItem.itemType == itemType &&
                                                                    tmpItem.item.m_shared.m_name == item.m_shared.m_name &&
                                                                    tmpItem.quality == effectiveQuality &&
                                                                    tmpItem.stack == 1 &&
                                                                    tmpItem.pricePerItem == adjustedItemPrice &&
                                                                    SameCurrency(tmpItem.currency, currency));
                if (currentStack != null)
                {
                    currentStack.amount += itemStack;
                    currentStack.price = CalculateSellPrice(currentStack.pricePerItem, currentStack.amount, priceFactor);
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
                        quality = effectiveQuality,
                        pricePerItem = adjustedItemPrice,
                        currency = currency
                    });
                }
            }
        }

        private static bool SameCurrency(ItemDrop first, ItemDrop second)
        {
            if (ReferenceEquals(first, second))
                return true;
            if (first == null || second == null)
                return false;

            return string.Equals(first.m_itemData.m_shared.m_name, second.m_itemData.m_shared.m_name, StringComparison.Ordinal);
        }

        public static void FillSellableList(StoreGui __instance)
        {
            foreach (GameObject item in sellItemList)
                UnityEngine.Object.Destroy(item);

            sellItemList.Clear();

            tempItems.Clear();
            tempItemsPrice.Clear();

            if (sellableItems.TryGetValue(CommonListKey(ItemsListType.Sell), out List<TradeableItem> commonItems))
            {
                bool includeAutomaticItems = TraderConfigManager.IsAutomaticCommonItemEnabled(__instance.m_trader);
                foreach (TradeableItem item in commonItems)
                    if (!item.automatic || includeAutomaticItems)
                        AddItemToSellList(item);
            }

            if (sellableItems.ContainsKey(TraderListKey(__instance.m_trader, ItemsListType.Sell)))
                sellableItems[TraderListKey(__instance.m_trader, ItemsListType.Sell)].ForEach(item => AddItemToSellList(item));

            float priceFactor = TraderCoins.GetPriceFactor(buyPrice: false);

            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItemsSortedByName())
            {
                if (IgnoreItemForSell(item, __instance.m_trader))
                    continue;

                Dictionary<int, SellPrice> stackPrices = GetStackPrices(item, out int quality);
                if (stackPrices == null)
                    continue;

                foreach (int stack in stackPrices.Keys.OrderBy(x => x))
                {
                    SellPrice sellPrice = stackPrices[stack];
                    ItemDrop currency = TraderCurrency.GetCurrency(sellPrice.CurrencyPrefab, __instance);
                    if (currency == null || string.Equals(item.m_shared.m_name, currency.m_itemData.m_shared.m_name, StringComparison.Ordinal))
                        continue;

                    if (stack == 1)
                    {
                        if (item.m_shared.m_maxStackSize == 1)
                            AddToSellList(item, 1, sellPrice.Price, priceFactor, quality, ItemToSell.ItemType.Single, currency);
                        else
                            AddToSellList(item, item.m_stack, sellPrice.Price, priceFactor, quality, ItemToSell.ItemType.Combined, currency);
                    }
                    else
                    {
                        AddToSellList(item, stack, sellPrice.Price, priceFactor, quality, ItemToSell.ItemType.Stack, currency);
                    }
                }
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
                SetCurrencyIcon(element, tradeItem.currency ?? __instance.m_coinPrefab);
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

            buybackItem = BuybackManager.Get(store.m_trader);
            if (buybackItem == null)
            {
                store.m_selectedItem = null;
                store.FillList();
                return false;
            }

            Inventory inventory = Player.m_localPlayer.GetInventory();
            bool result;
            if (buybackItem.itemType == ItemToSell.ItemType.Single)
            {
                if (!inventory.CanAddItem(buybackItem.item, buybackItem.item.m_stack))
                    return false;

                result = inventory.AddItem(buybackItem.item);
            }
            else
            {
                int variant = buybackItem.item.m_variant;
                int quality = buybackItem.quality == 0 ? buybackItem.item.m_quality : buybackItem.quality;
                int stack = buybackItem.itemType == ItemToSell.ItemType.Stack ? buybackItem.stack : buybackItem.amount;
                if (buybackItem.item.m_dropPrefab == null || !inventory.CanAddItem(buybackItem.item, stack))
                    return false;

                result = inventory.AddItem(buybackItem.item.m_dropPrefab.name, stack, quality, variant, buybackItem.item.m_crafterID, buybackItem.item.m_crafterName, buybackItem.item.m_pickedUp) != null;
            }
                
            if (result)
            {
                buybackItem = null;
                BuybackManager.Remove(store.m_trader);
                Player.m_localPlayer.GetInventory().RemoveItem(StoreGui.instance.m_coinPrefab.m_itemData.m_shared.m_name, store.m_selectedItem.m_price);
                store.m_selectedItem = null;
                StoreGui.instance.m_buyEffects.Create(StoreGui.instance.transform.position, Quaternion.identity);
                StoreGui.instance.FillList();
            }

            return result;
        }

        internal static void UpdateCurrencyVisuals(StoreGui storeGui)
        {
            if (storeGui == null || storeGui.m_coinPrefab == null)
                return;

            SetCurrencyIcon(storeGui.m_rootPanel, storeGui.m_coinPrefab);
            SetCurrencyIcon(sellPanel, storeGui.m_coinPrefab);
            AmountDialog.UpdateCurrencyIcon(storeGui.m_coinPrefab);
        }

        private static void SetCurrencyIcon(GameObject root, ItemDrop currency)
        {
            if (root == null || currency == null)
                return;

            Image[] images = root.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image == null)
                    continue;

                string objectName = image.gameObject.name;
                if (objectName.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    objectName.IndexOf("bkg", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    image.sprite = currency.m_itemData.GetIcon();
                }
            }
        }

        private static void InitializeNonTeleportableIcon(GameObject itemTemplate)
        {
            if (itemTemplate == null || itemTemplate.transform.Find(nonTeleportableIconName) != null || InventoryGui.instance == null)
                return;

            GameObject inventoryElement = InventoryGui.instance.m_playerGrid?.m_elementPrefab;
            if (inventoryElement == null)
                return;

            Transform source = inventoryElement.transform.Find("noteleport") ?? inventoryElement.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child != null && child.name.IndexOf("teleport", StringComparison.OrdinalIgnoreCase) >= 0);
            if (source == null)
                return;

            GameObject icon = UnityEngine.Object.Instantiate(source.gameObject, itemTemplate.transform);
            icon.name = nonTeleportableIconName;
            icon.SetActive(true);

            RectTransform rectTransform = icon.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = new Vector2(0f, 1f);
                rectTransform.anchorMax = new Vector2(0f, 1f);
                rectTransform.pivot = new Vector2(0f, 1f);
                rectTransform.anchoredPosition = new Vector2(4f, -4f);
                rectTransform.localScale = Vector3.one;
            }

            Image image = icon.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = false;
                image.raycastTarget = false;
            }
            else
            {
                icon.SetActive(false);
            }
        }

        private static void SetNonTeleportableIcon(GameObject element, bool visible)
        {
            Transform icon = element != null ? element.transform.Find(nonTeleportableIconName) : null;
            if (icon == null)
                return;

            Image image = icon.GetComponent<Image>();
            if (image != null)
            {
                icon.gameObject.SetActive(true);
                image.enabled = visible;
            }
            else
            {
                icon.gameObject.SetActive(visible);
            }
        }

        public static void SetStoreGuiPosition()
        {
            if (StoreGui.instance == null || StoreGui.instance.m_rootPanel == null)
                return;

            if (defaultStorePosition == Vector3.zero)
                defaultStorePosition = StoreGui.instance.m_rootPanel.transform.localPosition;

            Vector2 configuredPosition = TraderConfigManager.Get(StoreGui.instance.m_trader).FixedStoreGuiPosition;
            if (configuredPosition != Vector2.zero)
                StoreGui.instance.m_rootPanel.transform.localPosition = configuredPosition;
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
                TraderCurrency.CaptureVanillaCurrency(__instance);
                InitializeNonTeleportableIcon(__instance.m_listElement);
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
                InitializeNonTeleportableIcon(listElement);
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

                // Move original tooltip anchor to the side
                __instance.m_tooltipAnchor.anchorMax += new Vector2(1f, 0f);

                LogInfo($"StoreGui panel patched");

                buybackItem = null;
            }
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_FinalizeItems
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

            [HarmonyFinalizer]
            [HarmonyPriority(Priority.Last)]
            public static Exception Finalizer(Exception __exception, Trader __instance, ref List<Trader.TradeItem> __result)
            {
                if (__exception != null)
                    return __exception;

                if (__instance == null)
                    return null;

                if (__result == null)
                    __result = new List<Trader.TradeItem>();

                ResolvedTraderConfig config = TraderConfigManager.Get(__instance);
                if (config.DisableOtherModsItems)
                {
                    __result.Clear();

                    if (!config.DisableVanillaItems)
                        AddVanillaAvailableItems(__instance, __result);
                }
                else if (config.DisableVanillaItems)
                {
                    RemoveVanillaItems(__instance, __result);
                }

                AddAvailableItems(CommonListKey(ItemsListType.Buy), __instance, __result);
                AddAvailableItems(TraderListKey(__instance, ItemsListType.Buy), __instance, __result);

                ApplyFilterAndFlexiblePrices(__instance, __result);

                buybackItem = BuybackManager.Get(__instance);
                if (buybackItem != null)
                {
                    Trader.TradeItem buybackTradeItem = ItemToSell.SetBuyBackItem(new Trader.TradeItem()
                    {
                        m_prefab = null,
                        m_stack = 0,
                        m_price = buybackItem.price
                    });
                    TraderCurrency.RegisterCurrency(buybackTradeItem, buybackItem.currency, __instance);
                    __result.Insert(0, buybackTradeItem);
                }

                return null;
            }

            private static void AddVanillaAvailableItems(Trader trader, List<Trader.TradeItem> result)
            {
                List<Trader.TradeItem> vanillaItems = trader.m_items;

                if (vanillaItems == null || vanillaItems.Count == 0)
                    return;

                for (int i = 0; i < vanillaItems.Count; i++)
                {
                    Trader.TradeItem item = vanillaItems[i];

                    if (item == null)
                        continue;

                    if (string.IsNullOrEmpty(item.m_requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(item.m_requiredGlobalKey))
                        result.Add(item);
                }
            }

            private static void RemoveVanillaItems(Trader trader, List<Trader.TradeItem> result)
            {
                if (result == null || result.Count == 0)
                    return;

                List<Trader.TradeItem> vanillaItems = trader.m_items;

                if (vanillaItems == null || vanillaItems.Count == 0)
                    return;

                if (result.Count == vanillaItems.Count)
                {
                    bool sameItems = true;

                    for (int i = 0; i < result.Count; i++)
                    {
                        if (!ReferenceEquals(result[i], vanillaItems[i]))
                        {
                            sameItems = false;
                            break;
                        }
                    }

                    if (sameItems)
                    {
                        result.Clear();
                        return;
                    }
                }

                HashSet<Trader.TradeItem> vanillaSet = new HashSet<Trader.TradeItem>(vanillaItems);

                int writeIndex = 0;

                for (int readIndex = 0; readIndex < result.Count; readIndex++)
                {
                    Trader.TradeItem item = result[readIndex];

                    if (item == null || vanillaSet.Contains(item))
                        continue;

                    if (writeIndex != readIndex)
                        result[writeIndex] = item;

                    writeIndex++;
                }

                if (writeIndex < result.Count)
                    result.RemoveRange(writeIndex, result.Count - writeIndex);
            }

            private static void ApplyFilterAndFlexiblePrices(Trader trader, List<Trader.TradeItem> result)
            {
                if (result == null || result.Count == 0)
                    return;

                float factor = TraderCoins.GetPriceFactor(buyPrice: true);

                string filterText = traderFilter?.text;
                bool filterEnabled = !string.IsNullOrWhiteSpace(filterText);

                for (int i = result.Count - 1; i >= 0; i--)
                {
                    Trader.TradeItem tradeItem = result[i];

                    if (tradeItem == null || !ItemIsValid(tradeItem.m_prefab))
                    {
                        result.RemoveAt(i);
                        continue;
                    }

                    if (filterEnabled)
                    {
                        string itemName = Localization.instance.Localize(tradeItem.m_prefab.m_itemData.m_shared.m_name);

                        if (itemName.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            result.RemoveAt(i);
                            continue;
                        }
                    }

                    if (TraderConfigManager.Get(trader).TradersUseFlexiblePricing)
                    {
                        tradeItem = CloneTradeItem(tradeItem);

                        tradeItem.m_price = Math.Max((int)(tradeItem.m_price * factor), 1);
                        TradeableItem.NormalizeStack(tradeItem);

                        result[i] = tradeItem;
                    }
                }
            }

            private static Trader.TradeItem CloneTradeItem(Trader.TradeItem item)
            {
                Trader.TradeItem clone = new Trader.TradeItem()
                {
                    m_prefab = item.m_prefab,
                    m_stack = item.m_stack,
                    m_price = item.m_price,
                    m_requiredGlobalKey = item.m_requiredGlobalKey
                };
                TraderCurrency.CopyCurrency(item, clone);
                return clone;
            }
        }

        private static void AddAvailableItems(string listKey, Trader trader, List<Trader.TradeItem> result)
        {
            if (!tradeableItems.TryGetValue(listKey, out List<TradeableItem> items))
                return;

            foreach (TradeableItem item in items)
            {
                if (!item.IsItemToSell(trader))
                    continue;

                Trader.TradeItem tradeItem = item.ToTradeItem();
                if (tradeItem == null)
                    continue;

                TraderCurrency.RegisterCurrency(tradeItem, item.currency, trader);
                result.Add(tradeItem);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateSellButton))]
        public static class StoreGui_UpdateSellButton_Patch
        {
            private static void Postfix(StoreGui __instance)
            {
                UpdateSellButton();

                RepairPanel.Update(__instance);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
        public static class StoreGui_Show_Patch
        {
            private static void Prefix(StoreGui __instance, Trader trader)
            {
                TraderCoins.ResetCurrentTrader(trader);
                TraderCurrency.ApplyTraderCurrency(__instance, trader);
                buybackItem = BuybackManager.Get(trader);
            }

            private static void Postfix(StoreGui __instance)
            {
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

            ResolvedTraderConfig config = TraderConfigManager.Get(StoreGui.instance.m_trader);
            if (config.TradersUseCoins && config.TradersUseFlexiblePricing)
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
                if (___m_trader == null || Player.m_localPlayer == null)
                    return true;

                if (Vector3.Distance(___m_trader.transform.position, Player.m_localPlayer.transform.position) > ___m_hideDistance)
                    return true;

                if (AmountDialog.IsOpen())
                    return false;

                return true;
            }

            private static void Postfix()
            {
                // In case hiding was stopped
                if (IsOpen())
                    return;
                
                AmountDialog.Close();

                TraderCoins.UpdateTraderCoins();
                TraderCoins.ResetCurrentTrader(null);
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateRecipeGamepadInput))]
        public static class StoreGui_UpdateRecipeGamepadInput_SellListGamepadNavigation
        {
            private static bool Prefix(StoreGui __instance, List<GameObject> ___m_itemList)
            {
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
                if (playerFilter != null && traderFilter != null && StoreGui.IsVisible())
                    __result = __result || playerFilter.isFocused || traderFilter.isFocused;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.SelectItem))]
        public static class StoreGui_SelectItem_FixOutOfRangeForEmptyList
        {
            private static void Prefix(StoreGui __instance, ref int index, ref bool center)
            {
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
                TraderCurrency.ApplyTraderCurrency(__instance);
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
                    ItemDrop currency = TraderCurrency.GetCurrency(tradeItem, __instance);
                    int playerCurrency = TraderCurrency.GetPlayerCurrencyAmount(tradeItem, __instance);
                    bool available = price <= playerCurrency;
                    Image component = element.transform.Find("icon").GetComponent<Image>();
                    component.sprite = itemData.GetIcon();
                    ResolvedTraderConfig config = TraderConfigManager.Get(__instance.m_trader);
                    component.color = available ? (isBuyback ? config.BuybackItemHighlightedColor : Color.white) : new Color(1f, 0f, 1f, 0f);
                    bool showNonTeleportable = !itemData.m_shared.m_teleportable &&
                                               (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(GlobalKeys.TeleportAll));
                    SetNonTeleportableIcon(element, showNonTeleportable);
                    string text = Localization.instance.Localize(itemData.m_shared.m_name);

                    if (quality > 1)
                        text += $" <color=#add8e6ff>({quality})</color>";

                    if (stack > 1)
                        text += " x" + stack;

                    TMP_Text component2 = element.transform.Find("name").GetComponent<TMP_Text>();
                    component2.text = text;
                    component2.color = available ? (isBuyback ? config.BuybackItemFontColor : Color.white) : Color.grey;

                    string tooltip = ItemDrop.ItemData.GetTooltip(itemData, quality == 0 ? itemData.m_quality : quality, crafting: false, itemData.m_worldLevel);

                    element.GetComponent<UITooltip>().Set(itemData.m_shared.m_name, tooltip, __instance.m_tooltipAnchor);
                    SetCurrencyIcon(element, currency);
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
                        colors.normalColor = config.BuybackItemBackgroundColor;
                        colors.highlightedColor = config.BuybackItemHighlightedColor;
                        element.GetComponent<Button>().colors = colors;
                    }

                    __instance.m_itemList.Add(element);
                }

                FillSellableList(__instance);

                __instance.SelectItem(Mathf.Clamp(num, 0, __instance.m_itemList.Count - 1), center: false);

                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.CanAfford))]
        public static class StoreGui_CanAfford_CustomCurrency
        {
            private static bool Prefix(StoreGui __instance, Trader.TradeItem item, ref bool __result)
            {
                if (item == null || TraderCurrency.UsesStoreCurrency(item, __instance))
                    return true;

                __result = TraderCurrency.GetPlayerCurrencyAmount(item, __instance) >= item.m_price;
                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.BuySelectedItem))]
        public static class StoreGui_BuySelectedItem_TraderCoinsUpdate
        {
            private sealed class PurchaseState
            {
                internal ItemDrop OriginalCurrency;
                internal Trader.TradeItem TradeItem;
                internal int OriginalStack;
                internal int OriginalQuality;
                internal IDisposable BalanceTransaction;
            }

            public static bool isCalled;

            [HarmonyPriority(Priority.First)]
            private static bool Prefix(StoreGui __instance, ref PurchaseState __state)
            {
                isCalled = true;
                Trader.TradeItem selectedItem = __instance.m_selectedItem;
                if (selectedItem == null)
                    return true;

                __state = new PurchaseState
                {
                    OriginalCurrency = __instance.m_coinPrefab,
                    TradeItem = selectedItem,
                    OriginalStack = selectedItem.m_stack,
                    OriginalQuality = selectedItem.m_prefab != null ? selectedItem.m_prefab.m_itemData.m_quality : 0,
                    BalanceTransaction = TraderCoins.BeginBalanceTransaction(TraderCoins.BalanceOperation.Spend)
                };

                ItemDrop currency = TraderCurrency.GetCurrency(selectedItem, __instance);
                if (currency != null)
                    __instance.m_coinPrefab = currency;

                if (!__instance.CanAfford(selectedItem))
                    return true;

                if (ItemToSell.IsBuyBackItem(selectedItem))
                {
                    BuyBackItem(__instance);
                    return false;
                }

                if (selectedItem.m_prefab != null)
                {
                    TradeableItem.GetStackQualityFromStack(selectedItem.m_stack, out int stack, out int quality);
                    if (quality != 0)
                    {
                        selectedItem.m_stack = stack;
                        selectedItem.m_prefab.m_itemData.m_quality = quality;
                    }
                }

                return true;
            }

            private static void Restore(StoreGui storeGui, PurchaseState state)
            {
                isCalled = false;
                if (state == null)
                    return;

                state.BalanceTransaction?.Dispose();
                state.BalanceTransaction = null;

                if (storeGui != null && state.OriginalCurrency != null)
                    storeGui.m_coinPrefab = state.OriginalCurrency;

                if (state.TradeItem != null)
                {
                    state.TradeItem.m_stack = state.OriginalStack;
                    if (state.TradeItem.m_prefab != null)
                        state.TradeItem.m_prefab.m_itemData.m_quality = state.OriginalQuality;
                }
            }

            [HarmonyPriority(Priority.First)]
            private static void Postfix(StoreGui __instance, PurchaseState __state)
            {
                Restore(__instance, __state);
            }

            private static Exception Finalizer(StoreGui __instance, PurchaseState __state, Exception __exception)
            {
                Restore(__instance, __state);
                return __exception;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ShowPickupMessage))]
        public static class Character_ShowPickupMessage_FixIncorrectStackMessage
        {
            private static void Prefix(ItemDrop.ItemData item, ref int amount)
            {
                if (StoreGui.instance != null && StoreGui_BuySelectedItem_TraderCoinsUpdate.isCalled && StoreGui.instance.m_selectedItem != null && item == StoreGui.instance.m_selectedItem.m_prefab?.m_itemData)
                    amount = StoreGui.instance.m_selectedItem.m_stack;
            }
        }
    }
}
