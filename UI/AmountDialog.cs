using HarmonyLib;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static class AmountDialog
    {
        private static float clickTime;
        private static object lastClickedOffer;
        private static bool lastClickWasSell;
        private static Trader dialogTrader;
        private static Trader.TradeItem buyOffer;
        private static StorePanel.ItemToSell sellOffer;
        private static int lotSize;

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
        private static RectTransform dialogBackground;
        private static Vector2 defaultDialogPosition;
        private static Vector2? previewDialogOffset;

        private const float m_splitNumInputTimeoutSec = 0.5f;
        private static string m_splitInput = "";
        private static DateTime m_lastSplitInput;
        private static bool isSellDialog;

        public static GameObject Init(StoreGui store)
        {
            storeGui = store;

            SplitDialog template = InventoryGui.instance.m_splitDialog;
            SplitDialog clone = UnityEngine.Object.Instantiate(template, storeGui.m_rootPanel.transform.parent);
            amountDialog = clone.gameObject;
            amountDialog.name = "AmountDialog";
            amountDialog.SetActive(false);
            // OnEnable would reattach native listeners, overwrite the amount and reset the saved position.
            clone.enabled = false;

            dialogBackground = clone.m_panel;
            Transform win_bkg = dialogBackground.transform;
            defaultDialogPosition = clone.m_panelNormalPosition != null
                ? clone.m_panelNormalPosition.anchoredPosition : dialogBackground.anchoredPosition;
            previewDialogOffset = null;

            // The visible heading is separate from the inactive Icon_bkg/res_name item label.
            sliderTitle = win_bkg.Find("Text").GetComponent<TMP_Text>();
            ConfigureDynamicText(sliderTitle, 18f);
            sliderTitle.enableWordWrapping = false;
            sliderTitle.SetText(string.Empty);
            clone.m_splitIconName.gameObject.SetActive(false);
            sliderDialog = clone.m_splitSlider;
            sliderAmountText = clone.m_splitAmount;
            sliderImage = clone.m_splitIcon;
            Button confirm = clone.m_splitOkButton;
            Button cancel = clone.m_splitCancelButton;
            sliderButtonOk = confirm.GetComponentInChildren<TMP_Text>(true);

            // Move the entire icon group, not just its Image, so no orphaned background remains.
            RectTransform itemIconGroup = sliderImage.transform.parent as RectTransform;
            RectTransform currencyIconGroup = UnityEngine.Object.Instantiate(itemIconGroup, win_bkg);
            currencyIconGroup.name = "AmountCurrencyIcon";
            currencyIconGroup.SetSiblingIndex(itemIconGroup.GetSiblingIndex() + 1);
            sliderCurrencyImage = currencyIconGroup.Find(sliderImage.name).GetComponent<Image>();
            UpdateCurrencyIcon(storeGui.m_coinPrefab);

            float columnOffset = dialogBackground.rect.width * 0.2f;
            float iconY = itemIconGroup.anchoredPosition.y;
            SetCenteredRect(itemIconGroup, new Vector2(-columnOffset, iconY), itemIconGroup.sizeDelta);
            SetCenteredRect(currencyIconGroup, new Vector2(columnOffset, iconY), itemIconGroup.sizeDelta);
            itemIconGroup.gameObject.SetActive(true);
            currencyIconGroup.gameObject.SetActive(true);

            sliderAmountCoinsText = UnityEngine.Object.Instantiate(sliderAmountText, win_bkg);
            sliderAmountCoinsText.name = "amount_coins";
            sliderAmountCoinsText.transform.SetSiblingIndex(sliderAmountText.transform.GetSiblingIndex() + 1);

            // Each amount owns a separate column; long lot descriptions must not overlap the price.
            Vector2 amountSize = new Vector2(columnOffset * 2f - 12f, sliderAmountText.rectTransform.sizeDelta.y);
            float amountY = sliderAmountText.rectTransform.anchoredPosition.y;
            SetCenteredRect(sliderAmountText.rectTransform, new Vector2(-columnOffset, amountY), amountSize);
            SetCenteredRect(sliderAmountCoinsText.rectTransform, new Vector2(columnOffset, amountY), amountSize);
            ConfigureDynamicText(sliderAmountText, 12f);
            ConfigureDynamicText(sliderAmountCoinsText, 12f);
            sliderAmountText.enableWordWrapping = true;
            sliderAmountCoinsText.enableWordWrapping = false;

            // res_name is inactive in the native prefab; cloning it also hid the equality sign.
            TMP_Text equal = UnityEngine.Object.Instantiate(sliderAmountText, win_bkg);
            equal.name = "equal";
            SetCenteredRect(equal.rectTransform, new Vector2(0f, iconY), new Vector2(36f, itemIconGroup.sizeDelta.y));
            equal.enableAutoSizing = false;
            equal.fontSize = 32f;
            equal.enableWordWrapping = false;
            equal.SetText("=");
            equal.gameObject.SetActive(true);

            // The cloned panel must never call InventoryGui's inventory-splitting handlers.
            DisablePersistentListeners(sliderDialog.onValueChanged);
            DisablePersistentListeners(confirm.onClick);
            DisablePersistentListeners(cancel.onClick);
            sliderDialog.onValueChanged.RemoveAllListeners();
            confirm.onClick.RemoveAllListeners();
            cancel.onClick.RemoveAllListeners();
            sliderDialog.wholeNumbers = true;
            sliderDialog.onValueChanged.AddListener(OnSplitSliderChanged);
            confirm.onClick.AddListener(OnOkClick);
            cancel.onClick.AddListener(Close);

            // Disabling SplitDialog does not disable persistent UnityEvent calls to its methods.
            Button backgroundButton = amountDialog.transform.Find("CloseButton")?.GetComponent<Button>();
            if (backgroundButton != null)
            {
                DisablePersistentListeners(backgroundButton.onClick);
                backgroundButton.onClick.RemoveAllListeners();
                backgroundButton.onClick.AddListener(OnBackgroundClick);
            }

            StorePanel.DragHandle.Configure(StorePanel.DragHandle.CreateBackground(dialogBackground), dialogBackground, IsOpen,
                GetPanelOffset, PreviewPanelOffset, CommitPanelOffset);
            SetPanelPosition();

            return amountDialog;
        }

        private static void SetCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void ConfigureDynamicText(TMP_Text text, float minimumFontSize)
        {
            text.fontSizeMax = text.fontSize;
            text.fontSizeMin = Mathf.Min(minimumFontSize, text.fontSizeMax);
            text.enableAutoSizing = true;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            text.gameObject.SetActive(true);
        }

        private static void OnBackgroundClick()
        {
            if (ZInput.IsTouchActive())
                Close();
        }

        private static Vector2 GetPanelOffset() =>
            StorePanel.FinitePanelOffset(previewDialogOffset ?? amountDialogOffset.Value);

        private static void PreviewPanelOffset(Vector2 offset)
        {
            previewDialogOffset = offset;
            SetPanelPosition();
        }

        private static void CommitPanelOffset(Vector2 offset)
        {
            previewDialogOffset = null;
            amountDialogOffset.Value = StorePanel.FinitePanelOffset(offset);
            SetPanelPosition();
        }

        internal static void SetPanelPosition()
        {
            if (dialogBackground != null && amountDialogOffset != null)
                dialogBackground.anchoredPosition = defaultDialogPosition + GetPanelOffset();
        }

        private static void DisablePersistentListeners(UnityEventBase unityEvent)
        {
            for (int index = 0; index < unityEvent.GetPersistentEventCount(); index++)
                unityEvent.SetPersistentListenerState(index, UnityEventCallState.Off);
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
            object offer = sellDialog ? (object)StorePanel.selectedItem : storeGui?.m_selectedItem;
            bool doubleClick = offer != null && ReferenceEquals(offer, lastClickedOffer) &&
                lastClickWasSell == sellDialog && Time.unscaledTime - clickTime < 0.3f;
            SetSellState(sellDialog);
            lastClickedOffer = offer;
            lastClickWasSell = sellDialog;
            clickTime = Time.unscaledTime;
            if (doubleClick)
            {
                Open();
                lastClickedOffer = null;
            }
        }

        public static bool IsOpen()
        {
            return amountDialog != null && amountDialog.activeInHierarchy;
        }

        public static void Update()
        {
            if (!IsOpen())
                return;

            Player localPlayer = Player.m_localPlayer;
            if (localPlayer == null || localPlayer.IsDead() || localPlayer.InCutscene() || localPlayer.IsTeleporting())
            {
                Close();
                return;
            }

            if (!IsCurrentOffer())
            {
                Close();
                return;
            }

            int maximum = GetMaximumLots();
            if (maximum < 2)
            {
                Close();
                return;
            }
            sliderDialog.maxValue = maximum;

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
                return;
            }

            if ((Chat.instance == null || !Chat.instance.HasFocus()) && !Console.IsVisible() && !Menu.IsVisible() && (bool)TextViewer.instance && !TextViewer.instance.IsVisible() && !localPlayer.InCutscene() && (ZInput.GetButtonDown("JoyButtonB") || ZInput.GetKeyDown(KeyCode.Escape)))
            {
                ZInput.ResetButtonStatus("JoyButtonB");
                Close();
            }
        }

        public static void Close()
        {
            if (amountDialog != null)
                amountDialog.SetActive(false);
            buyOffer = null;
            sellOffer = null;
            dialogTrader = null;
            lastClickedOffer = null;
            m_splitInput = string.Empty;
        }

        private static bool IsCurrentOffer()
        {
            return storeGui != null && storeGui.m_rootPanel.activeInHierarchy && dialogTrader != null &&
                storeGui.m_trader == dialogTrader && Player.m_localPlayer != null &&
                (isSellDialog ? ReferenceEquals(StorePanel.selectedItem, sellOffer) && sellOffer != null :
                    ReferenceEquals(storeGui.m_selectedItem, buyOffer) && buyOffer != null);
        }

        private static int GetMaximumLots()
        {
            return isSellDialog ? StorePanel.GetMaximumSellLotsCached(storeGui, sellOffer) : StorePanel.GetMaximumBuyLotsCached(storeGui, buyOffer);
        }

        public static void Open()
        {
            if (amountDialog == null || storeGui?.m_trader == null || Player.m_localPlayer == null)
                return;

            dialogTrader = storeGui.m_trader;
            sellOffer = isSellDialog ? StorePanel.selectedItem : null;
            buyOffer = isSellDialog ? null : storeGui.m_selectedItem;
            if (!IsCurrentOffer() || (!isSellDialog &&
                (StorePanel.ItemToSell.IsBuyBackItem(buyOffer) || StorePanel.HasPlayerKeyReward(buyOffer) || buyOffer.m_prefab == null)))
                return;

            int maximum = GetMaximumLots();
            if (maximum < 2)
                return;

            lotSize = isSellDialog ? StorePanel.GetSellLotSize(sellOffer) : TradeableItem.GetStackFromStack(buyOffer.m_stack);
            ItemDrop.ItemData item = isSellDialog ? sellOffer.item : buyOffer.m_prefab.m_itemData;
            ItemDrop currency = isSellDialog ? sellOffer.currency : TraderCurrency.GetCurrency(buyOffer, storeGui);
            sliderTitleText = Localization.instance.Localize(isSellDialog ? item.m_shared.m_name : StorePanel.GetBuyOfferName(buyOffer));
            ResolvedTraderConfig config = TraderConfigManager.Get(dialogTrader);
            if (config.TradersUseCoins && config.TradersUseFlexiblePricing)
                sliderTitleText += StorePanel.GetPriceFactorString(
                    isSellDialog ? sellOffer.priceFactor : TraderCoins.GetPriceFactor(buyPrice: true), reversed: !isSellDialog);
            sliderTitle.SetText(sliderTitleText);
            sliderButtonText = Localization.instance.Localize(isSellDialog ? "$store_sell" : "$store_buy");
            sliderButtonOk.SetText(sliderButtonText);
            sliderDialog.minValue = 1f;
            sliderDialog.maxValue = maximum;
            sliderDialog.value = 1f;
            m_splitInput = string.Empty;
            sliderImage.sprite = item.GetIcon();
            UpdateCurrencyIcon(currency ?? storeGui.m_coinPrefab);
            OnSplitSliderChanged();
            SetPanelPosition();
            amountDialog.SetActive(true);
            // Apply dynamic labels after activation as well, before the first visible frame.
            sliderTitle.SetText(sliderTitleText);
            sliderButtonOk.SetText(sliderButtonText);
        }

        public static void OnSplitSliderChanged(float value = 0f)
        {
            if (sliderDialog == null || !IsCurrentOffer())
                return;
            int lots = Mathf.RoundToInt(sliderDialog.value);
            if (!TryGetQuote(lots, out int amount, out int price))
                return;
            // The slider counts whole configured lots; the preview always shows delivered item count.
            sliderAmountText.SetText(lotSize > 1 ? $"{amount} ({lots} x {lotSize})" : amount.ToString());
            sliderAmountCoinsText.SetText(price.ToString());
        }

        private static bool TryGetQuote(int lots, out int amount, out int price)
        {
            amount = price = 0;
            if (isSellDialog)
                return StorePanel.TryGetSellQuote(sellOffer, lots, out amount, out price);
            return buyOffer != null && TradeAmounts.TryGetItemCount(lotSize, lots, out amount) &&
                StorePanel.TryGetBuyPrice(buyOffer, lots, out price);
        }

        private static void BuySelectedItem()
        {
            if (!IsOpen() || !IsCurrentOffer())
            {
                Close();
                return;
            }
            int lots = Mathf.RoundToInt(sliderDialog.value);
            bool canTrade = lots >= 1 && lots <= GetMaximumLots() && TryGetQuote(lots, out _, out _);
            // Close before the trade refreshes/replaces the selected offer and invokes external callbacks.
            Close();
            if (!canTrade)
                return;
            if (isSellDialog)
                StorePanel.SellSelectedItem(storeGui, lots);
            else
                StorePanel.BuyLots(storeGui, lots);
        }

        internal static int GetPrice()
        {
            return sliderDialog != null && TryGetQuote(Mathf.RoundToInt(sliderDialog.value), out _, out int price) ? price : 0;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.OnSelectedItem))]
        public static class StoreGui_OnSelectedItem_SelectItem
        {
            static void Postfix() => OnSelectedTradeableItemClick(sellDialog: false);
        }
    }
}
