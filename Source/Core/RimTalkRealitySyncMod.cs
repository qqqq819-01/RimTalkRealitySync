using System;
using System.Reflection;
using HarmonyLib;
using RimTalkRealitySync;
using UnityEngine;
using Verse;

namespace RimTalkRealitySync
{
    /// <summary>
    /// The main entry point and settings UI manager for the RimTalk Reality Sync mod.
    /// (Updated: The stupid button is gone! We now inject our advanced UI directly into the vanilla rect.)
    /// </summary>
    public class RimTalkRealitySyncMod : Mod
    {
        // Static reference to the mod's settings so it can be accessed globally
        public static RealitySyncSettings Settings;

        /// <summary>
        /// Constructor called by RimWorld when the mod is loaded.
        /// </summary>
        public RimTalkRealitySyncMod(ModContentPack content) : base(content)
        {
            // 1. Initialize mod settings
            Settings = GetSettings<RealitySyncSettings>();

            // 2. Initialize and apply Harmony patches
            try
            {
                // NEW: Initialize Harmony with a unique identifier to prevent collisions with other mods.
                var harmony = new Harmony("com.rimtalkrealitysync.patch");

                // NEW: Automatically find and apply all classes with [HarmonyPatch] attributes.
                // This will effortlessly activate our new OpenAIClient_BuildRequestJson_Patch (The Trojan Horse).
                harmony.PatchAll(Assembly.GetExecutingAssembly());

                Log.Message("[RimTalk Reality Sync] Main Harmony patches (including Multimodal API Hijack) loaded successfully.");
            }
            catch (Exception e)
            {
                Log.Error($"[RimTalk Reality Sync] Failed to initialize Harmony patches: {e}");
            }

            Log.Message("[RimTalk Reality Sync] Mod initialized safely.");
        }

        /// <summary>
        /// Defines the name of the mod in the vanilla Mod Settings menu.
        /// </summary>
        public override string SettingsCategory()
        {
            return "RimTalk Reality Sync";
        }

        /// <summary>
        /// Draws the actual contents of the Mod Settings window.
        /// We completely bypass the vanilla Listing_Standard and directly draw our RimMusic-style UI!
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (!Find.WindowStack.IsOpen<RealitySyncSettingsFloatingWindow>())
            {
                Find.WindowStack.Add(new RealitySyncSettingsFloatingWindow());

                // =====================================================================
                // FIXED: Zombie Window Annihilation
                // Forcefully close the vanilla Mod Settings dialog to prevent it from 
                // lingering awkwardly behind our sleek floating UI.
                // =====================================================================
                var vanillaWindow = Find.WindowStack.WindowOfType<RimWorld.Dialog_ModSettings>();
                if (vanillaWindow != null)
                {
                    Find.WindowStack.TryRemove(vanillaWindow, false);
                }
            }
        }

        /// <summary>
        /// Called when the user closes the settings window.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Settings.DebugMode)
            {
                Log.Message("[RimTalk Reality Sync] Settings have been saved and written to disk.");
            }

            // Re-resolve the local gallery path in case the user changed it in the settings
            RimPhoneCore.ResolveGalleryPath();
        }
    }
}