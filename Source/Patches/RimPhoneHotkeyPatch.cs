// RimPhoneHotkeyPatch.cs
using HarmonyLib;
using RimTalkRealitySync.UI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimTalkRealitySync.Patches
{
    // =====================================================================
    // OLD UIRootOnGUI PATCH DELETED
    // Hotkey logic moved to RimPhoneEngine.Update() for 100% reliability.
    // =====================================================================

    // =====================================================================
    // NEW: Native UI Integration
    // Injects a clean, clickable icon directly into RimWorld's bottom-right
    // toggle bar (where the roof/resource visibility toggles live).
    // Zero overlapping, 100% native feel.
    // =====================================================================
    [HarmonyPatch(typeof(PlaySettings), "DoPlaySettingsGlobalControls")]
    public static class RimPhonePlaySettingsPatch
    {
        [HarmonyPostfix]
        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView) return;

            // Using the vanilla 'Search' magnifying glass icon temporarily 
            // until you have a custom mobile phone icon asset.
            if (row.ButtonIcon(TexButton.Search, "RTRS_Tooltip_OpenStation".Translate()))
            {
                RimPhoneStateManager.ToggleWindow();
            }
        }
    }
}