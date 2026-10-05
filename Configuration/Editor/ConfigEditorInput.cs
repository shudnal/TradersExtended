using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using static TradersExtended.TradersExtended;

namespace TradersExtended
{
    internal sealed class ConfigEditorCursor
    {
        private bool captured;
        private CursorLockMode previousLockState;
        private bool previousVisible;

        internal void Update(bool active)
        {
            if (active)
                Unlock();
            else
                Release();
        }

        internal void Release()
        {
            if (!captured)
                return;
            Cursor.lockState = previousLockState;
            Cursor.visible = previousVisible;
            captured = false;
        }

        private void Unlock()
        {
            if (!captured || Cursor.lockState != CursorLockMode.None || !Cursor.visible)
            {
                previousLockState = Cursor.lockState;
                previousVisible = Cursor.visible;
                captured = true;
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    internal static class ConfigEditorInputState
    {
        internal static bool ShouldBlockAll;
        internal static bool ShouldBlockMouse;

        private static bool editorOpen;
        private static bool blockGameInput;

        internal static void SetEditorOpen(bool value)
        {
            if (editorOpen == value)
                return;

            editorOpen = value;
            Refresh();
        }

        internal static void SetBlockGameInput(bool value)
        {
            if (blockGameInput == value)
                return;

            blockGameInput = value;
            Refresh();
        }

        internal static void OnBlockGameInputSettingChanged(object sender, EventArgs e)
        {
            SetBlockGameInput(configEditorBlockGameInput?.Value == true);
        }

        private static void Refresh()
        {
            bool block = editorOpen && blockGameInput;
            ShouldBlockAll = block;
            ShouldBlockMouse = block;
        }
    }

    internal static class ZInputPatchMethods
    {
        private const BindingFlags AllMethods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static IEnumerable<MethodBase> FindBooleanMethods(params string[] names)
        {
            var acceptedNames = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.Ordinal);
            return typeof(ZInput)
                .GetMethods(AllMethods)
                .Where(method => method.ReturnType == typeof(bool) && acceptedNames.Contains(method.Name))
                .Cast<MethodBase>()
                .Distinct();
        }

    }

    [HarmonyPatch(typeof(PlayerController), nameof(PlayerController.TakeInput))]
    [HarmonyPriority(Priority.Last)]
    internal static class PlayerControllerTakeInputPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ConfigEditorInputState.ShouldBlockAll)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
    [HarmonyPriority(Priority.Last)]
    internal static class TextInputIsVisiblePatch
    {
        private static void Postfix(ref bool __result)
        {
            if (ConfigEditorInputState.ShouldBlockAll)
                __result = true;
        }
    }

    [HarmonyPatch]
    internal static class ValheimMouseInteractionBlockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return new[]
            {
                AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftClick)),
                AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.OnRightDown)),
                AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.OnSelectedItem)),
                AccessTools.Method(typeof(InventoryGui), nameof(InventoryGui.OnRightClickItem)),
                AccessTools.Method(typeof(Toggle), nameof(Toggle.OnPointerClick)),
                AccessTools.Method(typeof(Button), nameof(Button.OnPointerClick)),
                AccessTools.Method(typeof(ScrollRect), nameof(ScrollRect.OnScroll))
            }.Where(method => method != null);
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !ConfigEditorInputState.ShouldBlockMouse;
    }

    [HarmonyPatch]
    internal static class ValheimAllInputInteractionBlockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return new[]
            {
                AccessTools.Method(typeof(Toggle), nameof(Toggle.OnSubmit)),
                AccessTools.Method(typeof(Button), nameof(Button.OnSubmit)),
                AccessTools.Method(typeof(Button), "Press"),
                AccessTools.Method(typeof(Player), nameof(Player.UseHotbarItem))
            }.Where(method => method != null);
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => !ConfigEditorInputState.ShouldBlockAll;
    }

    [HarmonyPatch]
    internal static class ZInputAllBooleanBlockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return ZInputPatchMethods.FindBooleanMethods(
                nameof(ZInput.ShouldAcceptInputFromSource),
                nameof(ZInput.GetKey),
                nameof(ZInput.GetKeyUp),
                nameof(ZInput.GetKeyDown),
                nameof(ZInput.GetButton),
                nameof(ZInput.GetButtonDown),
                nameof(ZInput.GetButtonUp),
                nameof(ZInput.GetRadialTap),
                nameof(ZInput.GetRadialMultiTap));
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            if (!ConfigEditorInputState.ShouldBlockAll)
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ZInputMouseBooleanBlockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return ZInputPatchMethods.FindBooleanMethods(
                nameof(ZInput.GetMouseButton),
                nameof(ZInput.GetMouseButtonDown),
                nameof(ZInput.GetMouseButtonUp));
        }

        [HarmonyPriority(Priority.First)]
        private static bool Prefix(ref bool __result)
        {
            if (!ConfigEditorInputState.ShouldBlockMouse)
                return true;

            __result = false;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class ZInputAllFloatBlockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return new[]
            {
                AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickX)),
                AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLeftStickY)),
                AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRTrigger)),
                AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyLTrigger)),
                AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRightStickX)),
                AccessTools.Method(typeof(ZInput), nameof(ZInput.GetJoyRightStickY))
            }.Where(method => method != null);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref float __result)
        {
            if (ConfigEditorInputState.ShouldBlockAll)
                __result = 0f;
        }
    }

    [HarmonyPatch]
    internal static class ZInputAllVectorBlockPatch
    {
        // Valheim 1.0.14 reads movement directly from vector stick getters, bypassing
        // the scalar axis patches. Resolve them by name to keep older references usable.
        private static readonly MethodBase[] StickMethods = typeof(ZInput)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.ReturnType == typeof(Vector2)
                && !method.ContainsGenericParameters
                && method.GetParameters().Length == 0
                && (method.Name == "GetJoyLeftStick" || method.Name == "GetJoyRightStick"))
            .Cast<MethodBase>()
            .ToArray();

        // Older game versions have only scalar getters. Skip this optional patch when absent.
        private static bool Prepare() => StickMethods.Length > 0;

        private static IEnumerable<MethodBase> TargetMethods() => StickMethods;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref Vector2 __result)
        {
            if (ConfigEditorInputState.ShouldBlockAll)
                __result = Vector2.zero;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
    internal static class ZInputMouseScrollBlockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref float __result)
        {
            if (ConfigEditorInputState.ShouldBlockMouse)
                __result = 0f;
        }
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseDelta))]
    internal static class ZInputMouseDeltaBlockPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(ref Vector2 __result)
        {
            if (ConfigEditorInputState.ShouldBlockMouse)
                __result = Vector2.zero;
        }
    }
}
