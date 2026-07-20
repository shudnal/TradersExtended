using HarmonyLib;
using System;
using System.Collections;
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
        internal static bool ShouldBlockAll => ConfigEditor.IsOpenGlobal && configEditorBlockGameInput?.Value == true;
        internal static bool ShouldBlockMouse => ShouldBlockAll;
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

        internal static IEnumerable<MethodBase> FindStringButtonMethods()
        {
            return FindBooleanMethods(
                    nameof(ZInput.GetButton),
                    nameof(ZInput.GetButtonDown),
                    nameof(ZInput.GetButtonUp))
                .Where(method =>
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length > 0 && parameters[0].ParameterType == typeof(string);
                });
        }
    }

    internal static class ZInputMouseBindingResolver
    {
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Dictionary<string, CachedMouseBindings> BindingCache = new Dictionary<string, CachedMouseBindings>(StringComparer.Ordinal);

        private sealed class CachedMouseBindings
        {
            public bool DefinitionFound;
            public bool BindingSourceResolved;
            public object ButtonAction;
            public object[] MouseControls = Array.Empty<object>();
            public int MouseButtonMask;
        }

        internal static void ClearCache() => BindingCache.Clear();

        internal static void Invalidate(string action)
        {
            if (!string.IsNullOrWhiteSpace(action))
                BindingCache.Remove(action);
        }

        internal static bool IsMouseBindingActive(
            string action,
            string queryMethodName,
            bool actionResultIsKnownTrue = false)
        {
            if (string.IsNullOrWhiteSpace(action))
                return false;

            CachedMouseBindings bindings = GetConfiguredMouseBindings(action);
            if (bindings.DefinitionFound)
            {
                object activeControl = GetMemberValue(bindings.ButtonAction, "activeControl");
                if (activeControl != null)
                {
                    bool activeControlIsMouse = IsMouseControl(activeControl);
                    bool activeControlMatchesQuery = IsControlActive(activeControl, queryMethodName);

                    if (activeControlIsMouse && (actionResultIsKnownTrue || activeControlMatchesQuery))
                        return true;

                    // When the action itself says a keyboard or controller control produced the
                    // successful query, keep that source available even if the same action also
                    // has a mouse binding.
                    if (!activeControlIsMouse && (actionResultIsKnownTrue || activeControlMatchesQuery))
                        return false;
                }

                for (int i = 0; i < bindings.MouseControls.Length; i++)
                {
                    if (IsControlActive(bindings.MouseControls[i], queryMethodName))
                        return true;
                }

                int mask = bindings.MouseButtonMask;
                for (int mouseButton = 0; mask != 0 && mouseButton < 31; mouseButton++, mask >>= 1)
                {
                    if ((mask & 1) != 0 && IsMouseButtonActive(mouseButton, queryMethodName))
                        return true;
                }

                // A real binding source was resolved. If it contains no active mouse binding,
                // preserve a keyboard or controller activation of the same action.
                if (bindings.BindingSourceResolved)
                    return false;
            }

            int fallbackButton = action switch
            {
                "Attack" => 0,
                "Block" or "BuildMenu" => 1,
                "SecondaryAttack" or "Remove" => 2,
                _ => -1
            };

            return fallbackButton >= 0 && IsMouseButtonActive(fallbackButton, queryMethodName);
        }

        private static CachedMouseBindings GetConfiguredMouseBindings(string action)
        {
            if (BindingCache.TryGetValue(action, out CachedMouseBindings cached))
                return cached;

            CachedMouseBindings resolved = ResolveConfiguredMouseBindings(action);
            BindingCache[action] = resolved;
            return resolved;
        }

        private static CachedMouseBindings ResolveConfiguredMouseBindings(string action)
        {
            var resolved = new CachedMouseBindings();

            try
            {
                ZInput input = ZInput.instance;
                if (input?.m_buttons == null || !input.m_buttons.TryGetValue(action, out ZInput.ButtonDef definition))
                    return resolved;

                resolved.DefinitionFound = true;
                ResolveDirectButtonDefinition(definition, resolved);

                object buttonAction = GetMemberValue(definition, "ButtonAction");
                if (buttonAction == null)
                    buttonAction = GetMemberValue(definition, "m_buttonAction");

                if (buttonAction == null)
                    return resolved;

                resolved.BindingSourceResolved = true;
                resolved.ButtonAction = buttonAction;

                object bindings = GetMemberValue(buttonAction, "bindings");
                if (bindings is IEnumerable bindingEnumerable)
                {
                    foreach (object binding in bindingEnumerable)
                    {
                        string path = GetMemberValue(binding, "effectivePath") as string;
                        if (string.IsNullOrWhiteSpace(path))
                            path = GetMemberValue(binding, "path") as string;

                        if (TryParseMouseButton(path, out int mouseButton) && mouseButton < 31)
                            resolved.MouseButtonMask |= 1 << mouseButton;
                    }
                }

                object controls = GetMemberValue(buttonAction, "controls");
                if (controls is IEnumerable controlEnumerable)
                {
                    var mouseControls = new List<object>();
                    foreach (object control in controlEnumerable)
                    {
                        if (control != null && IsMouseControl(control))
                            mouseControls.Add(control);
                    }

                    resolved.MouseControls = mouseControls.ToArray();
                }
            }
            catch
            {
                return new CachedMouseBindings();
            }

            return resolved;
        }


        private static void ResolveDirectButtonDefinition(object definition, CachedMouseBindings resolved)
        {
            if (definition == null || resolved == null)
                return;

            bool mouseBindingFlagFound = TryGetBooleanMember(definition, "m_bMouseButtonSet", out bool mouseBindingSet) ||
                                         TryGetBooleanMember(definition, "m_mouseButtonSet", out mouseBindingSet);

            object mouseButton = GetMemberValue(definition, "m_mouseButton");
            if (mouseBindingFlagFound)
            {
                resolved.BindingSourceResolved = true;
                if (mouseBindingSet && TryConvertMouseButton(mouseButton, out int mouseButtonIndex))
                    resolved.MouseButtonMask |= 1 << mouseButtonIndex;
            }
            else if (mouseButton != null && TryConvertMouseButton(mouseButton, out int mouseButtonIndex))
            {
                // Some ZInput revisions omit the explicit flag and use a sentinel enum value.
                string enumName = mouseButton.ToString() ?? string.Empty;
                if (!enumName.Equals("None", StringComparison.OrdinalIgnoreCase) &&
                    !enumName.Equals("Back", StringComparison.OrdinalIgnoreCase))
                {
                    resolved.BindingSourceResolved = true;
                    resolved.MouseButtonMask |= 1 << mouseButtonIndex;
                }
            }

            object key = GetMemberValue(definition, "m_key");
            if (key != null)
            {
                string keyName = key.ToString() ?? string.Empty;
                if (TryParseLegacyMouseKeyName(keyName, out int keyMouseButton))
                {
                    resolved.BindingSourceResolved = true;
                    resolved.MouseButtonMask |= 1 << keyMouseButton;
                }
                else if (!keyName.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    resolved.BindingSourceResolved = true;
                }
            }
        }

        private static bool TryConvertMouseButton(object value, out int mouseButton)
        {
            mouseButton = -1;
            if (value == null)
                return false;

            string name = value.ToString() ?? string.Empty;
            if (name.Equals("Left", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("LeftButton", StringComparison.OrdinalIgnoreCase))
            {
                mouseButton = 0;
                return true;
            }

            if (name.Equals("Right", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("RightButton", StringComparison.OrdinalIgnoreCase))
            {
                mouseButton = 1;
                return true;
            }

            if (name.Equals("Middle", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("MiddleButton", StringComparison.OrdinalIgnoreCase))
            {
                mouseButton = 2;
                return true;
            }

            if (name.Equals("Forward", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("ForwardButton", StringComparison.OrdinalIgnoreCase))
            {
                mouseButton = 3;
                return true;
            }

            if (name.Equals("Back", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("BackButton", StringComparison.OrdinalIgnoreCase))
            {
                mouseButton = 4;
                return true;
            }

            try
            {
                int numeric = Convert.ToInt32(value);
                if (numeric >= 0 && numeric < 31)
                {
                    mouseButton = numeric;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryParseLegacyMouseKeyName(string keyName, out int mouseButton)
        {
            mouseButton = -1;
            if (string.IsNullOrWhiteSpace(keyName) ||
                !keyName.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return int.TryParse(keyName.Substring(5), out mouseButton) && mouseButton >= 0 && mouseButton < 31;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            try
            {
                Type type = instance.GetType();

                PropertyInfo property = type.GetProperty(memberName, InstanceMembers);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(instance, null);

                FieldInfo field = type.GetField(memberName, InstanceMembers);
                return field?.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetBooleanMember(object instance, string memberName, out bool value)
        {
            value = false;
            object raw = GetMemberValue(instance, memberName);
            if (!(raw is bool boolean))
                return false;

            value = boolean;
            return true;
        }

        private static bool IsMouseControl(object control)
        {
            if (control == null)
                return false;

            string path = GetMemberValue(control, "path") as string;
            if (!string.IsNullOrWhiteSpace(path) &&
                path.IndexOf("/Mouse/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            object device = GetMemberValue(control, "device");
            string deviceType = device?.GetType().FullName ?? string.Empty;
            if (deviceType.EndsWith(".Mouse", StringComparison.Ordinal) ||
                string.Equals(device?.GetType().Name, "Mouse", StringComparison.Ordinal))
            {
                return true;
            }

            string displayName = GetMemberValue(device, "displayName") as string;
            return string.Equals(displayName, "Mouse", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsControlActive(object control, string queryMethodName)
        {
            if (control == null)
                return false;

            string memberName = queryMethodName switch
            {
                nameof(ZInput.GetButtonDown) => "wasPressedThisFrame",
                nameof(ZInput.GetButtonUp) => "wasReleasedThisFrame",
                _ => "isPressed"
            };

            if (TryGetBooleanMember(control, memberName, out bool active))
                return active;

            MethodInfo readValueAsButton = control.GetType().GetMethod(
                "ReadValueAsButton",
                InstanceMembers,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);

            if (readValueAsButton != null && readValueAsButton.ReturnType == typeof(bool))
            {
                try
                {
                    return (bool)readValueAsButton.Invoke(control, null);
                }
                catch
                {
                }
            }

            return false;
        }

        private static bool TryParseMouseButton(string path, out int mouseButton)
        {
            mouseButton = -1;
            if (string.IsNullOrWhiteSpace(path) || path.IndexOf("<Mouse>", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            string normalized = path.Replace(" ", string.Empty).ToLowerInvariant();

            if (normalized.EndsWith("/leftbutton", StringComparison.Ordinal))
                mouseButton = 0;
            else if (normalized.EndsWith("/rightbutton", StringComparison.Ordinal))
                mouseButton = 1;
            else if (normalized.EndsWith("/middlebutton", StringComparison.Ordinal))
                mouseButton = 2;
            else if (normalized.EndsWith("/forwardbutton", StringComparison.Ordinal))
                mouseButton = 3;
            else if (normalized.EndsWith("/backbutton", StringComparison.Ordinal))
                mouseButton = 4;
            else
            {
                int marker = normalized.LastIndexOf("/button", StringComparison.Ordinal);
                if (marker < 0 || !int.TryParse(normalized.Substring(marker + 7), out mouseButton))
                    return false;
            }

            return mouseButton >= 0;
        }

        private static bool IsMouseButtonActive(int mouseButton, string queryMethodName)
        {
            if (TryGetInputSystemMouseButton(mouseButton, out object control) &&
                IsControlActive(control, queryMethodName))
            {
                return true;
            }

            try
            {
                return queryMethodName switch
                {
                    nameof(ZInput.GetButtonDown) => Input.GetMouseButtonDown(mouseButton),
                    nameof(ZInput.GetButtonUp) => Input.GetMouseButtonUp(mouseButton),
                    _ => Input.GetMouseButton(mouseButton)
                };
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetInputSystemMouseButton(int mouseButton, out object control)
        {
            control = null;

            try
            {
                Type mouseType = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem", throwOnError: false);
                object mouse = mouseType?.GetProperty("current", BindingFlags.Static | BindingFlags.Public)?.GetValue(null, null);
                if (mouse == null)
                    return false;

                string memberName = mouseButton switch
                {
                    0 => "leftButton",
                    1 => "rightButton",
                    2 => "middleButton",
                    3 => "forwardButton",
                    4 => "backButton",
                    _ => null
                };

                if (memberName == null)
                    return false;

                control = GetMemberValue(mouse, memberName);
                return control != null;
            }
            catch
            {
                return false;
            }
        }
    }

    [HarmonyPatch]
    internal static class ZInputMouseBindingCachePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return typeof(ZInput)
                .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method =>
                {
                    if (method.Name == nameof(ZInput.Load))
                        return true;

                    if (method.Name != nameof(ZInput.AddButton))
                        return false;

                    ParameterInfo[] parameters = method.GetParameters();
                    return parameters.Length > 0 && parameters[0].ParameterType == typeof(string);
                })
                .Cast<MethodBase>()
                .Distinct();
        }

        private static void Postfix(MethodBase __originalMethod, object[] __args)
        {
            if (__originalMethod?.Name == nameof(ZInput.Load))
            {
                ZInputMouseBindingResolver.ClearCache();
                return;
            }

            string action = __args != null && __args.Length > 0 ? __args[0] as string : null;
            ZInputMouseBindingResolver.Invalidate(action);
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
                AccessTools.Method(typeof(InventoryGrid), nameof(InventoryGrid.OnRightClick)),
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
    internal static class ZInputMouseMappedButtonBlockPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => ZInputPatchMethods.FindStringButtonMethods();

        [HarmonyPriority(Priority.Last)]
        private static Exception Finalizer(
            Exception __exception,
            MethodBase __originalMethod,
            object[] __args,
            ref bool __result)
        {
            if (__exception != null ||
                !__result ||
                ConfigEditorInputState.ShouldBlockAll ||
                !ConfigEditorInputState.ShouldBlockMouse)
            {
                return __exception;
            }

            string action = __args != null && __args.Length > 0 ? __args[0] as string : null;
            if (ZInputMouseBindingResolver.IsMouseBindingActive(
                    action,
                    __originalMethod?.Name,
                    actionResultIsKnownTrue: true))
            {
                __result = false;
            }

            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class PlayerSetControlsMouseBlockPatch
    {
        private const BindingFlags AllMethods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static IEnumerable<MethodBase> TargetMethods()
        {
            string[] requiredNames =
            {
                "attack",
                "attackHold",
                "secondaryAttack",
                "secondaryAttackHold",
                "block",
                "blockHold"
            };

            return typeof(Player)
                .GetMethods(AllMethods)
                .Where(method => method.Name == "SetControls")
                .Where(method =>
                {
                    var names = new HashSet<string>(
                        method.GetParameters().Select(parameter => parameter.Name),
                        StringComparer.Ordinal);
                    return requiredNames.All(names.Contains);
                })
                .Cast<MethodBase>()
                .Distinct();
        }

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            ref bool attack,
            ref bool attackHold,
            ref bool secondaryAttack,
            ref bool secondaryAttackHold,
            ref bool block,
            ref bool blockHold)
        {
            if (!ConfigEditorInputState.ShouldBlockMouse || ConfigEditorInputState.ShouldBlockAll)
                return;

            if ((attack || attackHold) &&
                ZInputMouseBindingResolver.IsMouseBindingActive(
                    "Attack",
                    nameof(ZInput.GetButton),
                    actionResultIsKnownTrue: true))
            {
                attack = false;
                attackHold = false;
            }

            if ((secondaryAttack || secondaryAttackHold) &&
                ZInputMouseBindingResolver.IsMouseBindingActive(
                    "SecondaryAttack",
                    nameof(ZInput.GetButton),
                    actionResultIsKnownTrue: true))
            {
                secondaryAttack = false;
                secondaryAttackHold = false;
            }

            if ((block || blockHold) &&
                ZInputMouseBindingResolver.IsMouseBindingActive(
                    "Block",
                    nameof(ZInput.GetButton),
                    actionResultIsKnownTrue: true))
            {
                block = false;
                blockHold = false;
            }
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
