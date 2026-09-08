using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal static partial class StorePanel
    {
        private static RectTransform positionedStoreRoot;
        private static Vector2? previewStoreOffset;

        internal static Vector2 FinitePanelOffset(Vector2 offset) =>
            float.IsNaN(offset.x) || float.IsInfinity(offset.x) || float.IsNaN(offset.y) || float.IsInfinity(offset.y)
                ? Vector2.zero : offset;

        private static Vector2 GetStorePanelOffset() =>
            FinitePanelOffset(previewStoreOffset ?? storePanelOffset?.Value ?? Vector2.zero);

        private static void PreviewStorePanelOffset(Vector2 offset)
        {
            previewStoreOffset = offset;
            SetStoreGuiPosition();
        }

        private static void CommitStorePanelOffset(Vector2 offset)
        {
            previewStoreOffset = null;
            storePanelOffset.Value = FinitePanelOffset(offset);
            SetStoreGuiPosition();
        }

        private static void ConfigureStorePanelDragging(StoreGui store)
        {
            RectTransform root = store.m_rootPanel.GetComponent<RectTransform>();
            GameObject background = DragHandle.CreateBackground(root, root.Find("border (1)") as RectTransform);
            foreach (Transform handle in new[] { background.transform, root.Find("bkg"), storeName.transform, playerName.transform })
                if (handle != null)
                    DragHandle.Configure(handle.gameObject, root, () => IsOpen() && !AmountDialog.IsOpen(),
                        GetStorePanelOffset, PreviewStorePanelOffset, CommitStorePanelOffset);
        }

        private static void PreparePanelPositionsForOpen(StoreGui store)
        {
            // Finish the previous session before clearing offsets, including a drag in the amount dialog.
            AmountDialog.Close();
            foreach (DragHandle handle in store.m_rootPanel.GetComponentsInChildren<DragHandle>(true))
                handle.FinishDrag();

            if (resetPanelPositionsOnOpen.Value)
            {
                previewStoreOffset = null;
                storePanelOffset.Value = Vector2.zero;
                amountDialogOffset.Value = Vector2.zero;
            }
        }

        // Pointer drag handling follows ExtraSlots' equipment panel behavior. This helper is shared
        // by store and amount panels; child buttons, list scrolling and sliders retain their own input.
        [DisallowMultipleComponent]
        internal sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            private RectTransform target;
            private RectTransform movementSpace;
            private Func<bool> canDrag;
            private Func<Vector2> getOffset;
            private Action<Vector2> previewOffset;
            private Action<Vector2> commitOffset;
            private bool dragging;
            private float canvasScale;
            private Vector2 rawOffset;

            internal static GameObject CreateBackground(RectTransform parent, RectTransform template = null)
            {
                // A leaf raycast surface behind controls avoids making their container a drag handler.
                GameObject background = new GameObject("PanelDragBackground", typeof(RectTransform), typeof(Image));
                background.layer = parent.gameObject.layer;
                RectTransform rect = background.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.SetAsFirstSibling();
                if (template != null)
                {
                    rect.anchorMin = template.anchorMin;
                    rect.anchorMax = template.anchorMax;
                    rect.pivot = template.pivot;
                    rect.sizeDelta = template.sizeDelta;
                    rect.anchoredPosition = template.anchoredPosition;
                }
                else
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                }
                background.GetComponent<Image>().color = Color.clear;
                return background;
            }

            internal static void Configure(GameObject handleObject, RectTransform target, Func<bool> canDrag,
                Func<Vector2> getOffset, Action<Vector2> previewOffset, Action<Vector2> commitOffset)
            {
                if (handleObject == null || target == null || !handleObject.TryGetComponent(out Graphic graphic))
                    return;

                graphic.raycastTarget = true;
                DragHandle handle = handleObject.GetComponent<DragHandle>() ?? handleObject.AddComponent<DragHandle>();
                handle.target = target;
                handle.canDrag = canDrag;
                handle.getOffset = getOffset;
                handle.previewOffset = previewOffset;
                handle.commitOffset = commitOffset;
            }

            private bool CanDrag()
            {
                if (instance == null || target == null || storePanelDragKey == null || canDrag?.Invoke() != true)
                    return false;

                var shortcut = storePanelDragKey.Value;
                if (shortcut.MainKey == KeyCode.None || !ZInput.GetKey(shortcut.MainKey))
                    return false;
                foreach (KeyCode modifier in shortcut.Modifiers)
                    if (!ZInput.GetKey(modifier))
                        return false;
                return true;
            }

            public void OnBeginDrag(PointerEventData eventData)
            {
                if (dragging || eventData.button != PointerEventData.InputButton.Left ||
                    eventData.pointerPressRaycast.gameObject != gameObject || !CanDrag() || getOffset == null)
                    return;

                dragging = true;
                rawOffset = FinitePanelOffset(getOffset());
                // Titles can have a different parent than the panel they move.
                movementSpace = target.parent as RectTransform;
                canvasScale = target.GetComponentInParent<Canvas>()?.scaleFactor ?? 1f;
                if (canvasScale <= 0f || float.IsNaN(canvasScale) || float.IsInfinity(canvasScale))
                    canvasScale = 1f;
            }

            public void OnDrag(PointerEventData eventData)
            {
                if (!dragging)
                    return;
                if (!CanDrag())
                {
                    FinishDrag();
                    return;
                }

                Vector2 delta = eventData.delta / canvasScale;
                if (movementSpace != null &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(movementSpace,
                        eventData.position, eventData.pressEventCamera, out Vector2 current) &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(movementSpace,
                        eventData.position - eventData.delta, eventData.pressEventCamera, out Vector2 previous))
                    delta = current - previous;

                rawOffset = FinitePanelOffset(rawOffset + delta);
                previewOffset?.Invoke(rawOffset);
                eventData.eligibleForClick = false;
            }

            public void OnEndDrag(PointerEventData eventData) => FinishDrag();

            private void OnDisable() => FinishDrag();

            private void OnApplicationFocus(bool hasFocus)
            {
                if (!hasFocus)
                    FinishDrag();
            }

            internal void FinishDrag()
            {
                if (!dragging)
                    return;
                dragging = false;
                commitOffset?.Invoke(rawOffset);
            }
        }
    }
}
