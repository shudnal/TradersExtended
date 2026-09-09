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
    internal static partial class StorePanel
    {
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

        internal static ItemToSell selectedItem;
        private static bool sellPaneActive;
        private static int lastSellIndex;


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

            for (int i = 0; i < sellItemList.Count; i++)
                sellItemList[i]?.transform.Find("selected")?.gameObject.SetActive(i == index);

            if (center && index >= 0 && sellItemList[index] != null)
                itemEnsureVisible?.CenterOnItem(sellItemList[index].transform as RectTransform);

            selectedItem = (index < 0) ? null : tempItems[index];

            if (index >= 0)
            {
                sellPaneActive = true;
                lastSellIndex = index;
                AmountDialog.SetSellState(sellDialog: true);
                StoreGui.instance.SelectItem(-1, center: false);
            }
        }

        public static void FillSellableList(StoreGui __instance)
        {
            ItemToSell previousSelection = selectedItem;
            int previousIndex = GetSelectedItemIndex();
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
                if (item.m_worldLevel < Game.m_worldLevel || IgnoreItemForSell(item, __instance.m_trader))
                    continue;

                Dictionary<int, SellPrice> stackPrices = GetStackPrices(item, out int quality);
                if (stackPrices == null)
                    continue;

                foreach (int stack in stackPrices.Keys.OrderBy(x => x))
                {
                    SellPrice sellPrice = stackPrices[stack];
                    ItemDrop currency = TraderCurrency.GetCurrency(sellPrice.CurrencyPrefab, __instance);
                    if (currency == null || string.Equals(TradeInventory.PrefabName(item), Utils.GetPrefabName(currency.gameObject), StringComparison.Ordinal))
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

            if (previousIndex < 0 && sellPaneActive)
                previousIndex = lastSellIndex;
            SelectItem(RestoreSelectionIndex(tempItems, previousSelection, previousIndex, SameSellOffer), center: false);
        }

        private static bool SameSellOffer(ItemToSell first, ItemToSell second)
        {
            return first != null && second != null && first.itemType == second.itemType &&
                (first.itemType != ItemToSell.ItemType.Single || ReferenceEquals(first.item, second.item)) &&
                first.stack == second.stack && first.quality == second.quality &&
                TradeInventory.PrefabName(first.item) == TradeInventory.PrefabName(second.item) &&
                SameCurrency(first.currency, second.currency);
        }

        private static bool SameBuyOffer(Trader.TradeItem first, Trader.TradeItem second)
        {
            return first != null && second != null && first.m_prefab == second.m_prefab &&
                first.m_stack == second.m_stack && first.m_price == second.m_price &&
                first.m_requiredGlobalKey == second.m_requiredGlobalKey &&
                SameCurrency(TraderCurrency.GetCurrency(first, StoreGui.instance), TraderCurrency.GetCurrency(second, StoreGui.instance));
        }

        // Prefer the same row when duplicate offers exist, then look for the same offer elsewhere.
        // An exhausted sale falls back to its nearest remaining neighbour, never to the buy list.
        private static int RestoreSelectionIndex<T>(IList<T> items, T previous, int previousIndex, Func<T, T, bool> sameOffer)
        {
            if (previousIndex < 0 || items.Count == 0)
                return -1;
            if (previousIndex < items.Count && sameOffer(items[previousIndex], previous))
                return previousIndex;
            for (int index = 0; index < items.Count; index++)
                if (sameOffer(items[index], previous))
                    return index;
            return Math.Min(previousIndex, items.Count - 1);
        }

        public static void UpdateSellButton()
        {
            int lots = selectedItem?.itemType == ItemToSell.ItemType.Combined ? selectedItem.amount : 1;
            sellButton.interactable = TryGetSellQuote(selectedItem, lots, out _, out int price) && TraderCoins.CanSell(price);
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

            RectTransform root = StoreGui.instance.m_rootPanel.GetComponent<RectTransform>();
            if (positionedStoreRoot != root)
            {
                positionedStoreRoot = root;
                defaultStorePosition = root.localPosition;
                previewStoreOffset = null;
            }

            Vector2 configuredPosition = TraderConfigManager.Get(StoreGui.instance.m_trader).FixedStoreGuiPosition;
            Vector3 position = configuredPosition != Vector2.zero ? (Vector3)configuredPosition : defaultStorePosition;
            if (configuredPosition == Vector2.zero && AdventureModeEnabled(StoreGui.instance.m_trader))
                position.x -= RepairPanel.TraderCanRepair(StoreGui.instance.m_trader) ? 146f : 100f;

            // Local dragging is an offset, so per-trader defaults and EpicLoot positioning still apply.
            root.localPosition = position + (Vector3)GetStorePanelOffset();
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
                for (int index = 0; index < sellButton.onClick.GetPersistentEventCount(); index++)
                    sellButton.onClick.SetPersistentListenerState(index, UnityEngine.Events.UnityEventCallState.Off);
                sellButton.onClick.RemoveAllListeners();
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

                ConfigureStorePanelDragging(__instance);

                // Move original tooltip anchor to the side
                __instance.m_tooltipAnchor.anchorMax += new Vector2(1f, 0f);

                LogInfo($"StoreGui panel patched");

                buybackItem = null;
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
                if (__instance.m_trader != trader || !__instance.m_rootPanel.activeSelf)
                    PreparePanelPositionsForOpen(__instance);

                if (__instance.m_trader != trader || !StoreGui.IsVisible())
                {
                    playerFilter.SetTextWithoutNotify("");
                    traderFilter.SetTextWithoutNotify("");
                    selectedItem = null;
                    sellPaneActive = false;
                    lastSellIndex = 0;
                    __instance.m_selectedItem = null;
                }
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
        }

        internal static string GetPriceFactorString(float factor, bool reversed = false)
        {
            if (factor == 1f)
                return string.Empty;

            return $" · <color=#{((reversed && factor < 1) || (!reversed && factor > 1) ? "80ff80fc" : "ff6464fc")}>{(factor - 1f) * 100f:+0;-0}</color>%";
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
            private static bool Prefix(StoreGui __instance, int index, bool center)
            {
                List<Trader.TradeItem> offers = StoreGui_FillList_FillSellableList.availableItems;
                if (index < 0 || offers.Count == 0 || __instance.m_itemList.Count == 0)
                    index = -1;
                else
                    index = Math.Min(index, Math.Min(offers.Count, __instance.m_itemList.Count) - 1);
                for (int i = 0; i < __instance.m_itemList.Count; i++)
                    __instance.m_itemList[i]?.transform.Find("selected")?.gameObject.SetActive(i == index);
                __instance.m_selectedItem = index < 0 ? null : offers[index];
                if (center && index >= 0)
                    __instance.m_itemEnsureVisible.CenterOnItem(__instance.m_itemList[index].transform as RectTransform);
                if (index >= 0)
                {
                    sellPaneActive = false;
                    AmountDialog.SetSellState(sellDialog: false);
                    SelectItem(-1, center: false);
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
        public static class StoreGui_FillList_FillSellableList
        {
            internal static readonly List<Trader.TradeItem> availableItems = new List<Trader.TradeItem>();

            public static bool Prefix(StoreGui __instance)
            {
                if (__instance.m_trader == null || Player.m_localPlayer == null)
                    return false;
                bool keepSellFocus = sellPaneActive;
                Trader.TradeItem previousBuySelection = __instance.m_selectedItem;
                int num = previousBuySelection == null ? -1 : __instance.GetSelectedItemIndex();
                TraderCurrency.ApplyTraderCurrency(__instance);
                
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

                if (keepSellFocus)
                    __instance.SelectItem(-1, center: false);
                else
                    __instance.SelectItem(previousBuySelection == null ? 0 :
                        RestoreSelectionIndex(availableItems, previousBuySelection, num, SameBuyOffer), center: false);

                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateBuyButton))]
        private static class StoreGui_UpdateBuyButton_Capacity
        {
            private static void Postfix(StoreGui __instance)
            {
                Trader.TradeItem item = __instance.m_selectedItem;
                if (item == null)
                    return;
                if (ItemToSell.IsBuyBackItem(item))
                {
                    // Preflight the cached receipt against the same post-payment state used by BuyBackItem.
                    ItemToSell receipt = buybackItem;
                    Inventory inventory = Player.m_localPlayer?.GetInventory();
                    List<TradeInventory.Removal> payment = null;
                    bool affordable = receipt?.soldItems != null && receipt.currency != null && receipt.price > 0 && inventory != null &&
                        TradeInventory.PlanRemoval(inventory,
                            inventory.GetAllItems().Where(existing => TradeInventory.MatchesCurrency(existing, receipt.currency)),
                            receipt.price, out payment);
                    bool fits = affordable && TradeInventory.CanAddSavedItemsAfterRemoval(inventory, receipt.soldItems, payment);
                    __instance.m_buyButton.interactable = affordable && fits;
                    __instance.m_buyButton.GetComponent<UITooltip>().m_text = affordable && fits ? string.Empty :
                        Localization.instance.Localize(affordable ? "$inventory_full" : "$msg_missingrequirement");
                    return;
                }
                bool canAfford = __instance.CanAfford(item);
                bool canBuy = GetMaximumBuyLots(__instance, item) > 0;
                __instance.m_buyButton.interactable = canBuy;
                __instance.m_buyButton.GetComponent<UITooltip>().m_text = canBuy ? string.Empty :
                    Localization.instance.Localize(canAfford ? "$inventory_full" : "$msg_missingrequirement");
            }
        }
    }
}
