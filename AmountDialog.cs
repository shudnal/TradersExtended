using HarmonyLib;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static class AmountDialog
    {
        private static float clickTime;

        private static GameObject amountDialog;
        private static Slider sliderDialog;
        private static TMP_Text sliderTitle;
        private static TMP_Text sliderAmountText;
        private static TMP_Text sliderAmountCoinsText;
        private static TMP_Text sliderButtonOk;
        private static Image sliderImage;
        private static Image sliderCurrencyImage;
        private static string sliderTitleText;
        private static string sliderButtonText;
        private static StoreGui storeGui;

        private const float m_splitNumInputTimeoutSec = 0.5f;
        private static string m_splitInput = "";
        private static DateTime m_lastSplitInput;
        private static bool isSellDialog;

        public static GameObject Init(StoreGui store)
        {
            storeGui = store;

            amountDialog = UnityEngine.Object.Instantiate(InventoryGui.instance.m_splitPanel.gameObject, storeGui.m_rootPanel.transform.parent);
            amountDialog.name = "AmountDialog";

            Transform win_bkg = amountDialog.transform.Find("win_bkg");

            sliderTitle = win_bkg.Find("Text").GetComponent<TMP_Text>();
            sliderDialog = win_bkg.Find("Slider").GetComponent<Slider>();
            sliderAmountText = win_bkg.Find("amount").GetComponent<TMP_Text>();
            sliderImage = win_bkg.Find("Icon_bkg/Icon").GetComponent<Image>();
            sliderButtonOk = win_bkg.Find("Button_ok/Text").GetComponent<TMP_Text>();

            Transform icon = win_bkg.Find("Icon_bkg");

            GameObject sliderCoinsIcon = UnityEngine.Object.Instantiate(icon.gameObject, win_bkg);
            sliderCoinsIcon.name = "Coins_bkg";
            sliderCoinsIcon.transform.SetSiblingIndex(icon.GetSiblingIndex() + 1);
            sliderCurrencyImage = sliderCoinsIcon.transform.Find("Icon").GetComponent<Image>();
            UpdateCurrencyIcon(storeGui.m_coinPrefab);

            RectTransform rtCoins = sliderCoinsIcon.GetComponent<RectTransform>();
            rtCoins.anchorMax += new Vector2(0.15f, 0);
            rtCoins.anchorMin += new Vector2(0.15f, 0);

            RectTransform rtItem = icon.GetComponent<RectTransform>();
            rtItem.anchorMax -= new Vector2(0.15f, 0);
            rtItem.anchorMin -= new Vector2(0.15f, 0);

            GameObject sliderAmountCoins = UnityEngine.Object.Instantiate(sliderAmountText.gameObject, win_bkg);
            sliderAmountCoins.name = "amount_coins";
            sliderAmountCoins.transform.SetSiblingIndex(sliderAmountText.transform.GetSiblingIndex() + 1);

            sliderAmountCoinsText = sliderAmountCoins.GetComponent<TMP_Text>();
            
            RectTransform rtCoinsAmount = sliderAmountText.GetComponent<RectTransform>();
            rtCoinsAmount.anchorMax -= new Vector2(0.15f, 0);
            rtCoinsAmount.anchorMin -= new Vector2(0.15f, 0);

            RectTransform rtItemAmount = sliderAmountCoins.GetComponent<RectTransform>();
            rtItemAmount.anchorMax += new Vector2(0.15f, 0);
            rtItemAmount.anchorMin += new Vector2(0.15f, 0);

            GameObject sliderEqual = UnityEngine.Object.Instantiate(sliderTitle.gameObject, win_bkg);
            sliderEqual.name = "equal";
            sliderEqual.transform.SetSiblingIndex(sliderTitle.transform.GetSiblingIndex() + 1);

            RectTransform rtEqual = sliderEqual.GetComponent<RectTransform>();
            rtEqual.anchorMin -= new Vector2(0f, 0.5f);

            sliderEqual.GetComponent<TMP_Text>().SetText("=");

            sliderDialog.onValueChanged.AddListener(OnSplitSliderChanged);
            win_bkg.Find("Button_ok").GetComponent<Button>().onClick.AddListener(OnOkClick);
            win_bkg.Find("Button_cancel").GetComponent<Button>().onClick.AddListener(Close);

            return amountDialog;
        }

        public static void UpdateCurrencyIcon(ItemDrop currency)
        {
            if (sliderCurrencyImage != null && currency != null)
                sliderCurrencyImage.sprite = currency.m_itemData.GetIcon();
        }

        public static void OnOkClick()
        {
            BuySelectedItem();
        }

        public static void SetSellState(bool sellDialog)
        {
            isSellDialog = sellDialog;
        }

        public static void OnSelectedTradeableItemClick(bool sellDialog)
        {
            SetSellState(sellDialog);
            if (Time.time - clickTime < 0.3f)
            {
                Open();
                clickTime = 0f;
            }
            else
            {
                clickTime = Time.time;
            }
        }

        public static bool IsOpen()
        {
            return amountDialog != null && amountDialog.activeInHierarchy;
        }

        public static void Update()
        {
            if (amountDialog == null)
                return;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.IsDead() || localPlayer.InCutscene() || localPlayer.IsTeleporting())
            {
                Close();
                return;
            }

            if (sliderDialog == null)
                return;

            if (!sliderDialog.gameObject.activeInHierarchy)
                return;

            // Gamepad compatibility
            sliderTitle.SetText(sliderTitleText);
            sliderButtonOk.SetText(sliderButtonText);

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

            if (ZInput.GetButtonDown("JoyLTrigger") && sliderDialog.value > 10f)
            {
                sliderDialog.value -= 10f;
                OnSplitSliderChanged();
            }

            if (ZInput.GetButtonDown("JoyRTrigger") && sliderDialog.value < sliderDialog.maxValue)
            {
                sliderDialog.value += 10f;
                OnSplitSliderChanged();
            }

            if (ZInput.GetButtonDown("JoyLBumper") && sliderDialog.value > 5f)
            {
                sliderDialog.value -= 5f;
                OnSplitSliderChanged();
            }

            if (ZInput.GetButtonDown("JoyRBumper") && sliderDialog.value < sliderDialog.maxValue)
            {
                sliderDialog.value += 5f;
                OnSplitSliderChanged();
            }

            if (ZInput.GetKeyDown(KeyCode.KeypadEnter) || ZInput.GetKeyDown(KeyCode.Return))
            {
                BuySelectedItem();
            }

            if ((Chat.instance == null || !Chat.instance.HasFocus()) && !Console.IsVisible() && !Menu.IsVisible() && (bool)TextViewer.instance && !TextViewer.instance.IsVisible() && !localPlayer.InCutscene() && (ZInput.GetButtonDown("JoyButtonB") || ZInput.GetKeyDown(KeyCode.Escape)))
            {
                ZInput.ResetButtonStatus("JoyButtonB");
                Close();
            }
        }

        public static void Close()
        {
            amountDialog?.SetActive(value: false);
        }

        public static void Open()
        {
            if (amountDialog == null)
                return;

            LogInfo(isSellDialog);
            if (isSellDialog)
            {
                StorePanel.ItemToSell selectedItem = StorePanel.selectedItem;

                if (selectedItem == null)
                    return;

                if (selectedItem.itemType != StorePanel.ItemToSell.ItemType.Combined)
                    return;

                if (selectedItem.pricePerItem == 0)
                    return;

                if (!Player.m_localPlayer.GetInventory().HaveItem(selectedItem.item.m_shared.m_name))
                    return;

                int maxStack = Math.Min(selectedItem.amount, selectedItem.item.m_shared.m_maxStackSize);
                if (TraderConfigManager.Get(storeGui.m_trader).TradersUseCoins)
                {
                    maxStack = GetMaximumAffordableSellAmount(TraderCoins.GetTraderCoins(), maxStack);
                    if (maxStack < 1)
                        return;
                }

                SetDialogAndOpen(selectedItem.item, maxStack, selectedItem.currency ?? storeGui.m_coinPrefab);
            }
            else
            {
                if (!Player.m_localPlayer.GetInventory().HaveEmptySlot())
                    return;

                Trader.TradeItem selectedItem = storeGui.m_selectedItem;

                if (selectedItem == null || StorePanel.ItemToSell.IsBuyBackItem(selectedItem))
                    return;

                if (selectedItem.m_prefab == null || selectedItem.m_price <= 0)
                    return;

                int currencyAmount = TraderCurrency.GetPlayerCurrencyAmount(selectedItem, storeGui);

                if (TradeableItem.GetStackFromStack(selectedItem.m_stack) != 1)
                    return;

                if (selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize == 1)
                    return;

                if (currencyAmount < selectedItem.m_price)
                    return;

                SetDialogAndOpen(
                    selectedItem.m_prefab.m_itemData,
                    currencyAmount / selectedItem.m_price,
                    TraderCurrency.GetCurrency(selectedItem, storeGui));
            }
        }

        private static void SetDialogAndOpen(ItemDrop.ItemData item, int maxValue, ItemDrop currency)
        {
            sliderTitleText = Localization.instance.Localize(item.m_shared.m_name);

            sliderButtonText = Localization.instance.Localize(isSellDialog ? "$store_sell" : "$store_buy");

            sliderDialog.value = 1f;
            sliderDialog.minValue = 1f;
            sliderDialog.maxValue = Math.Min(item.m_shared.m_maxStackSize, maxValue);

            sliderImage.sprite = item.GetIcon();
            UpdateCurrencyIcon(currency);

            OnSplitSliderChanged();

            amountDialog.SetActive(value: true);
        }

        public static void OnSplitSliderChanged(float value = 0f)
        {
            sliderAmountText.SetText(((int)sliderDialog.value).ToString());
            sliderAmountCoinsText.SetText(GetPrice().ToString());
        }

        private static void BuySelectedItem()
        {
            if (isSellDialog)
            {
                if (TraderCanAffordSelectedItem())
                {
                    int amount = Mathf.CeilToInt(sliderDialog.value);
                    int price = GetPrice();
                    StorePanel.selectedItem.amount = amount;
                    StorePanel.selectedItem.price = price;
                    StorePanel.SellSelectedItem(storeGui);
                }
            }
            else
            {
                if (CanAffordSelectedItem())
                {
                    Trader.TradeItem selectedItem = storeGui.m_selectedItem;
                    int originalStack = selectedItem.m_stack;
                    int originalPrice = selectedItem.m_price;
                    try
                    {
                        TradeableItem.GetStackQualityFromStack(originalStack, out int _, out int quality);
                        int stack = Mathf.Min(Mathf.CeilToInt(sliderDialog.value), selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize);
                        selectedItem.m_stack = TradeableItem.GetStackFromStackQuality(stack, quality);
                        long totalPrice = (long)originalPrice * stack;
                        selectedItem.m_price = totalPrice >= int.MaxValue ? int.MaxValue : (int)totalPrice;
                        storeGui.BuySelectedItem();
                    }
                    finally
                    {
                        selectedItem.m_stack = originalStack;
                        selectedItem.m_price = originalPrice;
                    }
                }
            }

            Close();
        }

        private static bool TraderCanAffordSelectedItem()
        {
            if (StorePanel.selectedItem == null)
                return false;

            return TraderCoins.CanSell(GetPrice());
        }

        private static bool CanAffordSelectedItem()
        {
            if (storeGui.m_selectedItem == null)
                return false;

            int playerCurrency = TraderCurrency.GetPlayerCurrencyAmount(storeGui.m_selectedItem, storeGui);
            return GetPrice() <= playerCurrency && Player.m_localPlayer.GetInventory().HaveEmptySlot();
        }

        private static int GetPricePerItem()
        {
            return isSellDialog ? StorePanel.selectedItem.pricePerItem : storeGui.m_selectedItem.m_price;
        }

        internal static int GetPrice()
        {
            return GetPriceForAmount(Mathf.CeilToInt(sliderDialog.value));
        }

        private static int GetPriceForAmount(int amount)
        {
            if (amount <= 0)
                return 0;

            if (isSellDialog)
                return StorePanel.CalculateSellPrice(GetPricePerItem(), amount);

            double price = amount * (double)GetPricePerItem();
            if (double.IsNaN(price) || price >= int.MaxValue)
                return int.MaxValue;

            return Math.Max((int)Math.Ceiling(price), 1);
        }

        private static int GetMaximumAffordableSellAmount(int balance, int maximumAmount)
        {
            int low = 0;
            int high = Math.Max(maximumAmount, 0);
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (GetPriceForAmount(middle) <= balance)
                    low = middle;
                else
                    high = middle - 1;
            }

            return low;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.OnSelectedItem))]
        public static class StoreGui_OnSelectedItem_SelectItem
        {
            static void Postfix() => OnSelectedTradeableItemClick(sellDialog: false);
        }
    }
}
