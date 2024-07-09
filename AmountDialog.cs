﻿using HarmonyLib;
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
        private static TMP_Text sliderButtonOk;
        private static Image sliderImage;
        private static string sliderTitleText;
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

            sliderDialog.onValueChanged.AddListener(OnSplitSliderChanged);
            win_bkg.Find("Button_ok").GetComponent<Button>().onClick.AddListener(OnOkClick);
            win_bkg.Find("Button_cancel").GetComponent<Button>().onClick.AddListener(Close);

            return amountDialog;
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
                if (ZInput.GetButton("JoyButtonA") && !IsOpen())
                    Open();
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

            sliderTitle.SetText(sliderTitleText);

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

            if (isSellDialog)
            {
                StorePanel.ItemToSell selectedItem = StorePanel.selectedItem;

                if (selectedItem == null)
                    return;

                if (selectedItem.itemType != StorePanel.ItemToSell.ItemType.Combined)
                    return;

                int price = GetSellPrice(selectedItem);
                if (price == 0)
                    return;

                if (!Player.m_localPlayer.GetInventory().HaveItem(selectedItem.item.m_shared.m_name))
                    return;

                int maxStack = selectedItem.amount;
                if (traderUseCoins.Value)
                {
                    int coins = TraderCoins.GetTraderCoins();

                    if (coins < price)
                        return;

                    maxStack = Mathf.Min(coins / price, maxStack);
                }

                SetDialogAndOpen(selectedItem.item, maxStack);
            }
            else
            {
                if (!Player.m_localPlayer.GetInventory().HaveEmptySlot())
                    return;

                Trader.TradeItem selectedItem = storeGui.m_selectedItem;

                if (selectedItem == null)
                    return;

                int coins = storeGui.GetPlayerCoins();

                if (TradeableItem.GetStackFromStack(selectedItem.m_stack) != 1)
                    return;

                if (selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize == 1)
                    return;

                if (coins < selectedItem.m_price)
                    return;

                SetDialogAndOpen(selectedItem.m_prefab.m_itemData, Mathf.CeilToInt(coins / selectedItem.m_price));
            }
        }

        private static void SetDialogAndOpen(ItemDrop.ItemData item, int maxValue)
        {
            sliderTitleText = Localization.instance.Localize(item.m_shared.m_name);

            sliderButtonOk.SetText(Localization.instance.Localize(isSellDialog ? "$store_sell" : "$store_buy"));

            sliderDialog.value = 1f;
            sliderDialog.minValue = 1f;
            sliderDialog.maxValue = Math.Min(item.m_shared.m_maxStackSize, maxValue);

            sliderImage.sprite = item.GetIcon();

            OnSplitSliderChanged();

            amountDialog.SetActive(value: true);
        }

        public static void OnSplitSliderChanged(float value = 0f)
        {
            sliderAmountText.SetText(((int)sliderDialog.value).ToString());
        }

        private static void BuySelectedItem()
        {
            if (isSellDialog)
            {
                if (TraderCanAffordSelectedItem())
                {
                    StorePanel.selectedItem.amount = Mathf.CeilToInt(sliderDialog.value);
                    StorePanel.selectedItem.price = StorePanel.selectedItem.amount * GetSellPrice(StorePanel.selectedItem);
                    StorePanel.SellSelectedItem(storeGui);
                }
            }
            else
            {
                if (CanAffordSelectedItem())
                {
                    TradeableItem.GetStackQualityFromStack(storeGui.m_selectedItem.m_stack, out int stack, out int quality);
                    stack = Mathf.Min(Mathf.CeilToInt(sliderDialog.value), storeGui.m_selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize);
                    storeGui.m_selectedItem.m_stack = TradeableItem.GetStackFromStackQuality(stack, quality);
                    storeGui.m_selectedItem.m_price *= stack;
                    storeGui.BuySelectedItem();
                }
            }

            Close();
        }

        private static bool TraderCanAffordSelectedItem()
        {
            if (StorePanel.selectedItem == null)
                return false;

            return TraderCoins.CanSell(GetSellPrice(StorePanel.selectedItem) * (int)sliderDialog.value) && Player.m_localPlayer.GetInventory().HaveEmptySlot();
        }

        private static bool CanAffordSelectedItem()
        {
            if (storeGui.m_selectedItem == null)
                return false;

            int playerCoins = storeGui.GetPlayerCoins();
            return storeGui.m_selectedItem.m_price * (int)sliderDialog.value <= playerCoins && Player.m_localPlayer.GetInventory().HaveEmptySlot();
        }

        private static int GetSellPrice(StorePanel.ItemToSell itemToSell)
        {
            return itemToSell.price == 0 ? 0 : itemToSell.amount / itemToSell.price;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.OnSelectedItem))]
        public static class StoreGui_OnSelectedItem_SelectItem
        {
            static void Postfix()
            {
                if (!modEnabled.Value)
                    return;

                OnSelectedTradeableItemClick(sellDialog: false);
            }
        }


    }
}
