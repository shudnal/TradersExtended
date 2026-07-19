using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using System;
using System.Reflection;
using static TradersExtended.TradersExtended;

namespace TradersExtended.Compatibility
{
    internal static class EpicLootCompat
    {
        internal const string GUID = "randyknapp.mods.epicloot";

        internal static PluginInfo plugin;
        internal static Assembly assembly;
        internal static bool isEnabled;

        private static MethodInfo isAdventureModeEnabledMethod;
        private static bool invocationErrorLogged;

        internal static void CheckForCompatibility()
        {
            if (!(isEnabled = Chainloader.PluginInfos.TryGetValue(GUID, out plugin)))
                return;

            assembly ??= Assembly.GetAssembly(plugin.Instance.GetType());
            isAdventureModeEnabledMethod ??= AccessTools.Method(plugin.Instance.GetType(), "IsAdventureModeEnabled");
        }

        internal static bool IsAdventureModeEnabled()
        {
            if (!isEnabled || isAdventureModeEnabledMethod == null)
                return false;

            try
            {
                object target = isAdventureModeEnabledMethod.IsStatic ? null : plugin.Instance;
                return isAdventureModeEnabledMethod.Invoke(target, null) is bool enabled && enabled;
            }
            catch (Exception exception)
            {
                if (!invocationErrorLogged)
                {
                    invocationErrorLogged = true;
                    LogWarning($"Could not read EpicLoot adventure mode state: {exception.Message}");
                }
                return false;
            }
        }
    }
}
