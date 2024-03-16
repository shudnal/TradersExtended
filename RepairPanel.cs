using System;
using System.Collections.Generic;
using static ItemDrop;
using UnityEngine;
using UnityEngine.UI;
using static TradersExtended.TradersExtended;
using HarmonyLib;

namespace TradersExtended
{
    internal static class RepairPanel
    {
        private const string s_stationEffectName = "piece_workbench";

        private static GameObject repairPanel;
        private static Button repairButton;
        private static EffectList repairItemDoneEffects;

        public static void RepurposeSellButton(StoreGui storeGui)
        {
            repairPanel = storeGui.m_rootPanel.transform.Find("SellPanel").gameObject;
            repairPanel.transform.localPosition = new Vector3(592, -603, 0);

            repairButton = repairPanel.transform.Find("SellButton").GetComponent<Button>();
            repairButton.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.Off);
            repairButton.onClick.AddListener(delegate
            {
                OnRepairPressed(storeGui);
            });

            ButtonImageColor buttonColor = repairButton.GetComponent<ButtonImageColor>();
            buttonColor.m_defaultColor = new Color(0f, 0f, 0f, 0.85f);
            buttonColor.m_disabledColor = new Color(0f, 0f, 0f, 0.5f);

            Image image = repairButton.transform.Find("Image").GetComponent<Image>();
            image.overrideSprite = InventoryGui.instance.m_repairButton.transform.Find("Image").GetComponent<Image>().sprite;
            image.transform.localScale = Vector3.one * 0.85f;
            image.transform.localPosition = new Vector3(1f, -1.5f, 0f);

            repairButton.GetComponent<UITooltip>().Set("", "$inventory_repairbutton");

            Update(storeGui);
        }

        public static void AddButtonBlocker(GameObject blocker)
        {
            repairButton.GetComponent<UIGamePad>().m_blockingElements.Add(blocker);
        }

        public static void Update(StoreGui storeGui)
        {
            if (!StoreGui.IsVisible())
                return;

            repairPanel.SetActive(traderRepair.Value);
            repairButton.interactable = HaveRepairableItems(storeGui);
        }

        public static void OnRepairPressed(StoreGui storeGui)
        {
            RepairOneItem(storeGui);
            Update(storeGui);
        }

        public static void RepairOneItem(StoreGui storeGui)
        {
            if (Player.m_localPlayer == null)
                return;

            ItemData item = GetItemToRepair(storeGui);
            if (item == null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Player.m_localPlayer.GetPlayerName() + " $msg_doesnotneedrepair");
            else if (storeGui.GetPlayerCoins() < Math.Abs(traderRepairCost.Value))
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$msg_missingrequirement: $item_coins");
            else
            {
                item.m_durability = item.GetMaxDurability();

                if (traderRepairCost.Value != 0)
                    Player.m_localPlayer.GetInventory().RemoveItem(storeGui.m_coinPrefab.m_itemData.m_shared.m_name, Math.Abs(traderRepairCost.Value));

                repairItemDoneEffects?.Create(Player.m_localPlayer.transform.position, Quaternion.identity);

                StorePanel.UpdateTraderCoins(Math.Abs(traderRepairCost.Value));

                storeGui.m_sellEffects.Create(storeGui.transform.position, Quaternion.identity);
                
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_repaired", item.m_shared.m_name));
            }
        }

        public static bool HaveRepairableItems(StoreGui storeGui)
        {
            return GetItemToRepair(storeGui) != null;
        }

        public static ItemData GetItemToRepair(StoreGui storeGui)
        {
            if (Player.m_localPlayer == null || storeGui == null || storeGui.m_trader == null)
                return null;

            InventoryGui.instance.m_tempWornItems.Clear();
            Player.m_localPlayer.GetInventory().GetWornItems(InventoryGui.instance.m_tempWornItems);
            foreach (ItemData tempWornItem in InventoryGui.instance.m_tempWornItems)
                if (tempWornItem.m_shared.m_canBeReparied && IsCapableOfRepair(storeGui.m_trader, tempWornItem))
                    return tempWornItem;

            return null;
        }

        private static bool IsCapableOfRepair(Trader trader, ItemData item)
        {
            if (item.m_shared.m_itemType == ItemData.ItemType.OneHandedWeapon ||
                item.m_shared.m_itemType == ItemData.ItemType.Attach_Atgeir ||
                item.m_shared.m_itemType == ItemData.ItemType.Bow ||
                item.m_shared.m_itemType == ItemData.ItemType.Shield ||
                item.m_shared.m_itemType == ItemData.ItemType.Tool ||
                item.m_shared.m_itemType == ItemData.ItemType.Torch ||
                item.m_shared.m_itemType == ItemData.ItemType.TwoHandedWeapon ||
                item.m_shared.m_itemType == ItemData.ItemType.TwoHandedWeaponLeft)
                return _tradersToRepairWeapons.Contains(trader.m_name) || _tradersToRepairWeapons.Contains(trader.name);

            if (item.m_shared.m_itemType == ItemData.ItemType.Helmet ||
                item.m_shared.m_itemType == ItemData.ItemType.Chest ||
                item.m_shared.m_itemType == ItemData.ItemType.Legs ||
                item.m_shared.m_itemType == ItemData.ItemType.Shoulder ||
                item.m_shared.m_itemType == ItemData.ItemType.Utility ||
                item.m_shared.m_itemType == ItemData.ItemType.Customization)
                return _tradersToRepairArmor.Contains(trader.m_name) || _tradersToRepairArmor.Contains(trader.name);

            return false;
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
        public static class ObjectDB_Awake_GetRepairEffect
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(List<Recipe> ___m_recipes)
            {
                if (!modEnabled.Value)
                    return;

                foreach (Recipe _recipe in ___m_recipes)
                    if (_recipe.m_craftingStation?.name == s_stationEffectName && _recipe.m_craftingStation.m_repairItemDoneEffects != null)
                    {
                        repairItemDoneEffects = _recipe.m_craftingStation.m_repairItemDoneEffects;
                        return;
                    }
            }
        }

    }
}
