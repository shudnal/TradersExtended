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

        private static TMP_Text playerCoins;
        private static TMP_Text traderCoins;
        private static GameObject traderCoinsPanel;
        private static ScrollRectEnsureVisible m_itemEnsureVisible;

        private static bool epicLootEnabled;

        private static readonly List<GameObject> sellItemList = new List<GameObject>();
        private static readonly List<ItemDrop.ItemData> m_tempItems = new List<ItemDrop.ItemData>();
        private static readonly Dictionary<string, Dictionary<int, int>> m_tempItemsPrice = new Dictionary<string, Dictionary<int, int>>();

        private static ItemDrop.ItemData selectedItem;
        private static int selectedItemIndex = -1;

        private static ZNetView traderNetView;
        
        public static readonly int s_traderCoins = "traderCoins".GetStableHashCode();
        public static readonly int s_traderCoinsReplenished = "traderCoinsReplenished".GetStableHashCode();

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
                m_itemEnsureVisible.CenterOnItem(sellItemList[index].transform as RectTransform);

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
                    text = (selectedItem.m_stack <= 1) ? selectedItem.m_shared.m_name : (selectedItem.m_stack + "x" + selectedItem.m_shared.m_name);
                }
                else
                {
                    Player.m_localPlayer.GetInventory().RemoveItem(selectedItem, stackPrice.Key);
                    text = $"{stackPrice.Key}x{selectedItem.m_shared.m_name}";
                }

                stackCoins = (int)(stackCoins * GetTraderSellPriceFactor(GetTraderCoins()));

                Player.m_localPlayer.GetInventory().AddItem(m_coinPrefab.gameObject.name, stackCoins, m_coinPrefab.m_itemData.m_quality, m_coinPrefab.m_itemData.m_variant, 0L, "");

                UpdateTraderCoins(-stackCoins);

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

            int traderCurrentCoins = GetTraderCoins();

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

                int itemPrice = Math.Max((int)(ItemPrice(tradeItem) * GetTraderSellPriceFactor(traderCurrentCoins)), 1);
                int itemStack = ItemStack(tradeItem);
                
                bool canSell = itemPrice <= traderCurrentCoins;

                Image component = element.transform.Find("icon").GetComponent<Image>();
                component.sprite = tradeItem.GetIcon();
                component.color = (canSell ? Color.white : new Color(1f, 0f, 1f, 0f));

                string text = Localization.instance.Localize(tradeItem.m_shared.m_name);

                if (itemStack > 1)
                    text = text + " x" + ItemStack(tradeItem);

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

            SelectItem(Mathf.Min(m_tempItems.Count - 1, selectedItemIndex), center: false);
        }

        public static void UpdateSellButton(StoreGui __instance)
        {
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

                // Link trader and player names and coins labels
                storeName = __instance.m_rootPanel.transform.Find("topic").GetComponent<TMP_Text>();
                playerName = sellPanel.transform.Find("topic").GetComponent<TMP_Text>();
                playerCoins = sellPanel.transform.Find("coins/coins").GetComponent<TMP_Text>();
                traderCoins = __instance.m_coinText;
                traderCoinsPanel = __instance.m_rootPanel.transform.Find("coins").gameObject;

                // Link ScrollRectEnsureVisible component
                m_itemEnsureVisible = sellPanel.transform.Find("ItemList/Items").GetComponent<ScrollRectEnsureVisible>();

                // Prepare new sell button
                Transform sellPanelTransform = sellPanel.transform.Find("BuyButton");
                sellPanelTransform.Find("Text").GetComponent<TMP_Text>().SetText(Localization.instance.Localize("$store_sell"));
                sellPanelTransform.GetComponent<UIGamePad>().m_zinputKey = "JoyButtonX";

                // Make sell button into repair button
                RepairPanel.RepurposeSellButton(__instance);

                // Set handler to sell button
                sellButton = sellPanelTransform.GetComponent<Button>();
                sellButton.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.Off);
                sellButton.onClick.AddListener(delegate
                {
                    __instance.OnSellItem();
                });
                
                // Copy gamepad hint from Craft button and replace original hint
                UIGamePad component = sellButton.GetComponent<UIGamePad>();
                Vector3 position = component.m_hint.transform.localPosition;
                UnityEngine.Object.Destroy(component.m_hint);

                component.m_hint = UnityEngine.Object.Instantiate(InventoryGui.instance.m_craftButton.GetComponent<UIGamePad>().m_hint, sellButton.transform);
                component.m_hint.transform.localPosition = position;

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
            static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
            {
                if (!modEnabled.Value) return;

                AddAvailableItems(CommonListKey(ItemsListType.Buy), __result);

                AddAvailableItems(TraderListKey(__instance.m_name, ItemsListType.Buy), __result);
            }
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
        public static class Trader_GetAvailableItems_
        {
            [HarmonyPriority(Priority.Last)]
            static void Postfix(Trader __instance, List<Trader.TradeItem> __result)
            {
                if (!modEnabled.Value)
                    return;

                if (!traderUseFlexiblePricing.Value)
                    return;

                // Make copy to not alter original prices
                for (int i = 0; i < __result.Count; i++)
                    __result[i] = JsonUtility.FromJson<Trader.TradeItem>(JsonUtility.ToJson(__result[i]));

                float factor = GetTraderBuyPriceFactor(GetTraderCoins());
                foreach (Trader.TradeItem item in __result)
                    item.m_price = Math.Max((int)(item.m_price * factor), 1);
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

                sellPanel.SetActive(value: true);
                traderCoinsPanel.SetActive(traderUseCoins.Value);

                traderNetView = trader.GetComponent<ZNetView>();

                UpdateNames();
            }
        }

        private static void UpdateNames()
        {
            string traderTopic = StoreGui.instance.m_trader?.GetHoverName();
            string playerTopic = Player.m_localPlayer.GetPlayerName();

            if (traderUseCoins.Value && traderUseFlexiblePricing.Value)
            {
                int coins = GetTraderCoins();
                traderTopic += GetPriceFactorString(GetTraderBuyPriceFactor(coins), reversed:true);
                playerTopic += GetPriceFactorString(GetTraderSellPriceFactor(coins));
            }

            storeName.SetText(traderTopic);
            playerName.SetText(playerTopic);
        }

        private static string GetPriceFactorString(float factor, bool reversed = false)
        {
            if (factor == 1f)
                return "";

            return $" · <color={((reversed && factor < 1) || (!reversed && factor > 1) ? "green" : "red")}>{(factor - 1f) * 100f:+0;-0}</color>%";
        }

        private static float GetTraderBuyPriceFactor(int coins)
        {
            if (!traderUseFlexiblePricing.Value)
                return 1f;

            if (coins < traderCoinsMinimumAmount.Value)
                return RoundFactorToPercent(Mathf.Lerp(traderMarkup.Value, 1f, (float)coins / traderCoinsMinimumAmount.Value));

            return RoundFactorToPercent(Mathf.Lerp(1f, traderDiscount.Value, (float)(coins - traderCoinsMinimumAmount.Value) / (traderCoinsMaximumAmount.Value - traderCoinsMinimumAmount.Value)));
        }

        public static float GetTraderSellPriceFactor(int coins)
        {
            if (!traderUseFlexiblePricing.Value)
                return 1f;

            if (coins < traderCoinsMinimumAmount.Value)
                return RoundFactorToPercent(Mathf.Lerp(traderDiscount.Value, 1f, (float)coins / traderCoinsMinimumAmount.Value));

            return RoundFactorToPercent(Mathf.Lerp(1f, traderMarkup.Value, (float)(coins - traderCoinsMinimumAmount.Value) / (traderCoinsMaximumAmount.Value - traderCoinsMinimumAmount.Value)));
        }

        private static float RoundFactorToPercent(float factor)
        {
            return Mathf.Round(factor * 100f) / 100f;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Hide))]
        public static class StoreGui_Hide_Patch
        {
            private static bool Prefix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return true;

                if (AmountDialog.IsOpen())
                    return false;

                return true;
            }

            private static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (__instance.m_rootPanel.activeSelf)
                    return;

                sellPanel.SetActive(value: false);
                
                AmountDialog.Close();

                UpdateTraderCoins();

                traderNetView = null;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.UpdateRecipeGamepadInput))]
        public static class StoreGui_UpdateRecipeGamepadInput_SellListGamepadNavigation
        {
            static bool Prefix(StoreGui __instance)
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

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Update))]
        public static class StoreGui_Update_CoinsUpdate
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return;

                if (!__instance.m_rootPanel.activeSelf)
                    return;

                playerCoins.SetText(__instance.GetPlayerCoins().ToString());

                if (traderUseCoins.Value && traderNetView != null && traderNetView.IsValid())
                    traderCoins.SetText(GetTraderCoins().ToString());

                UpdateNames();
            }
        }

        public static void UpdateTraderCoins(int amount = 0)
        {
            if (!traderUseCoins.Value)
                return;

            if (traderNetView == null)
                traderNetView = StoreGui.instance.m_trader?.GetComponent<ZNetView>();

            if (!traderNetView.IsValid())
                return;

            traderNetView.GetZDO().Set(s_traderCoins, GetTraderCoins() + amount);

            if (StoreGui.instance.m_rootPanel.activeSelf)
                UpdateNames();
        }

        public static int GetTraderCoins()
        {
            if (traderNetView == null)
                traderNetView = StoreGui.instance.m_trader?.GetComponent<ZNetView>();

            if (traderNetView == null || !traderNetView.IsValid())
                return traderCoinsMinimumAmount.Value;

            return traderNetView.GetZDO().GetInt(s_traderCoins, traderCoinsMinimumAmount.Value);
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.SellItem))]
        public static class StoreGui_SellItem_Patch
        {
            static bool Prefix(StoreGui __instance)
            {
                if (!modEnabled.Value)
                    return true;

                if (!AmountDialog.IsOpen())
                    SellSelectedItem(__instance);

                return false;
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.BuySelectedItem))]
        public static class StoreGui_BuySelectedItem_TraderCoinsUpdate
        {
            public static bool isCalled = false;

            public static bool Prefix()
            {
                if (!modEnabled.Value)
                    return true;

                if (AmountDialog.IsOpen())
                    return false;

                isCalled = true;
                return true;
            }

            public static void Postfix() => isCalled = false;
        }

        [HarmonyPatch(typeof(Trader), nameof(Trader.OnBought))]
        public static class StoreGui_OnBought_TraderCoinsUpdate
        {
            public static void Postfix(Trader.TradeItem item) 
            {
                if (StoreGui_BuySelectedItem_TraderCoinsUpdate.isCalled)
                    UpdateTraderCoins(item.m_price);
            }
        }

        [HarmonyPatch(typeof(EnvMan), nameof(EnvMan.OnMorning))]
        public static class EnvMan_OnMorning_TraderCoinsUpdate
        {
            public static void Postfix(EnvMan __instance)
            {
                if (!traderUseCoins.Value)
                    return;

                MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, "$store_topic. $msg_added: $item_coins");

                if (!ZNet.instance.IsServer())
                    return;

                HashSet<int> traderPrefabs = new HashSet<int>(tradersCustomPrefabs.Value.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(selector => selector.GetStableHashCode()).ToList())
                {
                    "Haldor".GetStableHashCode(),
                    "Hildir".GetStableHashCode()
                };

                foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values.Where(zdo => traderPrefabs.Contains(zdo.GetPrefab())))
                {
                    int coinsReplenished = zdo.GetInt(s_traderCoinsReplenished);
                    if (__instance.GetCurrentDay() - coinsReplenished < traderCoinsReplenishmentRate.Value)
                        continue;

                    int current = zdo.GetInt(s_traderCoins);
                    if (current >= traderCoinsMaximumAmount.Value)
                        continue;

                    int newAmount = Mathf.Clamp(current + traderCoinsIncreaseAmount.Value, traderCoinsMinimumAmount.Value, traderCoinsMaximumAmount.Value);
                    zdo.Set(s_traderCoins, newAmount);
                    zdo.Set(s_traderCoinsReplenished, __instance.GetCurrentDay());
                    LogInfo($"{zdo} coins updated {current} -> {newAmount}");
                }
            }
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.GetSelectedItemIndex))]
        public static class StoreGui_GetSelectedItemIndex_GamePadScrollFix
        {
            public static bool Prefix(ref int __result, List<GameObject> ___m_itemList)
            {
                if (!modEnabled.Value)
                    return true;

                __result = 0;
                for (int i = 0; i < ___m_itemList.Count; i++)
                    if (___m_itemList[i].transform.Find("selected").gameObject.activeSelf)
                    {
                        __result = i;
                        break;
                    }

                return false;
            }

        }
    }
}
