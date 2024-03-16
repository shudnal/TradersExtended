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
        private static float leftClickTime;

        private static GameObject amountDialog;
        private static Slider sliderDialog;
        private static TMP_Text sliderTitle;
        private static TMP_Text sliderAmountText;
        private static Image sliderImage;
        private static string sliderTitleText;

        private const float m_splitNumInputTimeoutSec = 0.5f;
        private static string m_splitInput = "";
        private static DateTime m_lastSplitInput;

        public static GameObject Init(StoreGui storeGui)
        {
            amountDialog = UnityEngine.Object.Instantiate(InventoryGui.instance.m_splitPanel.gameObject, storeGui.m_rootPanel.transform.parent);

            Transform win_bkg = amountDialog.transform.Find("win_bkg");

            sliderTitle = win_bkg.Find("Text").GetComponent<TMP_Text>();
            sliderDialog = win_bkg.Find("Slider").GetComponent<Slider>();
            sliderAmountText = win_bkg.Find("amount").GetComponent<TMP_Text>();
            sliderImage = win_bkg.Find("Icon_bkg/Icon").GetComponent<Image>();

            sliderDialog.onValueChanged.AddListener(delegate
            {
                OnSplitSliderChanged();
            });

            win_bkg.Find("Button_ok").GetComponent<Button>().onClick.AddListener(delegate
            {
                BuySelectedItem(storeGui);
            });

            win_bkg.Find("Button_cancel").GetComponent<Button>().onClick.AddListener(delegate
            {
                Close();
            });

            return amountDialog;
        }

        public static void OnSelectedTradeableItemClick(StoreGui storeGui)
        {
            if (Time.time - leftClickTime < 0.3f)
            {
                Open(storeGui);
                leftClickTime = 0f;
            }
            else
            {
                leftClickTime = Time.time;
                if (ZInput.GetButton("JoyButtonA") && !IsOpen())
                    Open(storeGui);
            }
        }

        public static bool IsOpen()
        {
            return amountDialog.activeSelf;
        }

        public static void Update()
        {
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

            //if ()

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
                BuySelectedItem(StoreGui.instance);
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

        public static void Open(StoreGui __instance)
        {
            Trader.TradeItem selectedItem = __instance.m_selectedItem;

            int playerCoins = __instance.GetPlayerCoins();

            if (selectedItem == null)
                return;

            if (selectedItem.m_stack != 1)
                return;

            if (selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize == 1)
                return;

            if (playerCoins < selectedItem.m_price)
                return;

            sliderDialog.minValue = 1f;
            sliderDialog.maxValue = Math.Min(selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize, Mathf.CeilToInt(playerCoins / selectedItem.m_price));
            sliderDialog.value = 1;

            sliderImage.sprite = selectedItem.m_prefab.m_itemData.GetIcon();
            sliderTitleText = $"{Localization.instance.Localize("$store_buy")} {Localization.instance.Localize(selectedItem.m_prefab.m_itemData.m_shared.m_name)}";

            OnSplitSliderChanged();

            amountDialog.SetActive(value: true);
        }

        public static void OnSplitSliderChanged()
        {
            sliderAmountText.SetText(((int)sliderDialog.value).ToString());
        }

        private static void BuySelectedItem(StoreGui __instance)
        {
            if (__instance.m_selectedItem != null && CanAffordSelectedItem(__instance))
            {
                int stack = Mathf.Min(Mathf.CeilToInt(sliderDialog.value), __instance.m_selectedItem.m_prefab.m_itemData.m_shared.m_maxStackSize);
                int quality = __instance.m_selectedItem.m_prefab.m_itemData.m_quality;
                int variant = __instance.m_selectedItem.m_prefab.m_itemData.m_variant;
                if (Player.m_localPlayer.GetInventory().AddItem(__instance.m_selectedItem.m_prefab.name, stack, quality, variant, 0L, "") != null)
                {
                    int coins = __instance.m_selectedItem.m_price * stack;

                    Player.m_localPlayer.GetInventory().RemoveItem(__instance.m_coinPrefab.m_itemData.m_shared.m_name, coins);
                    
                    StorePanel.UpdateTraderCoins(coins);

                    __instance.m_trader.OnBought(__instance.m_selectedItem);
                    __instance.m_buyEffects.Create((__instance as MonoBehaviour).transform.position, Quaternion.identity);
                    Player.m_localPlayer.ShowPickupMessage(__instance.m_selectedItem.m_prefab.m_itemData, stack);
                    __instance.FillList();
                    Gogan.LogEvent("Game", "BoughtItem", __instance.m_selectedItem.m_prefab.name, 0L);
                }
            }
            Close();
        }

        private static bool CanAffordSelectedItem(StoreGui __instance)
        {
            int playerCoins = __instance.GetPlayerCoins();
            return __instance.m_selectedItem.m_price * sliderDialog.value <= playerCoins;
        }

        [HarmonyPatch(typeof(StoreGui), nameof(StoreGui.OnSelectedItem))]
        public static class StoreGui_OnSelectedItem_SelectItem
        {
            static void Postfix(StoreGui __instance)
            {
                if (!modEnabled.Value) return;

                OnSelectedTradeableItemClick(__instance);
            }
        }


    }
}
