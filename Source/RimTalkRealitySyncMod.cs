using System;
using System.Reflection;
using HarmonyLib;
using RimWorld; // Added RimWorld namespace for MessageTypeDefOf used in testing
using UnityEngine;
using Verse;

namespace RimTalkRealitySync
{
    /// <summary>
    /// The main entry point and settings UI manager for the RimTalk Reality Sync mod.
    /// </summary>
    public class RimTalkRealitySyncMod : Mod
    {
        // Static reference to the mod's settings so it can be accessed globally
        public static RealitySyncSettings Settings;

        // Scroll position for the settings window UI
        private Vector2 _scrollPosition;

        /// <summary>
        /// Constructor called by RimWorld when the mod is loaded.
        /// </summary>
        public RimTalkRealitySyncMod(ModContentPack content) : base(content)
        {
            // 1. Initialize mod settings
            Settings = GetSettings<RealitySyncSettings>();

            // 2. Initialize and apply Harmony patches
            // This is CRITICAL for our SaveGame and LoadGame patches in RealWorldProvider to work!
            try
            {
                var harmony = new Harmony("YourName.RimTalk.RealitySync");
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                Log.Message("[RimTalk Reality Sync] Main Harmony patches loaded successfully.");
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
        /// </summary>
        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Contract the rect slightly and draw a dark semi-transparent background (Borrowed from RimMusic UI style)
            Rect contentRect = inRect.ContractedBy(10f);
            Widgets.DrawBoxSolid(contentRect, new Color(0.12f, 0.12f, 0.12f, 0.8f));

            // Create an inner rect for the actual content
            Rect innerRect = contentRect.ContractedBy(15f);
            Listing_Standard listing = new Listing_Standard();

            // Setup a scroll view in case the window is too small for all settings
            Rect viewRect = new Rect(0, 0, innerRect.width - 20f, 500f);
            Widgets.BeginScrollView(innerRect, ref _scrollPosition, viewRect);
            listing.Begin(viewRect);

            // --- Header Section ---
            Text.Font = GameFont.Medium;
            listing.Label("<b><color=#88AAFF>" + "RTRS_SettingsTitle".Translate() + "</color></b>");
            Text.Font = GameFont.Small;
            listing.GapLine();
            listing.Gap(10f);

            // --- 1. Weather API Settings Section ---
            listing.Label("<b><color=#FFDD44>" + "RTRS_WeatherApiTitle".Translate() + "</color></b>");
            listing.Gap(5f);

            // API Provider Radio Buttons
            if (listing.RadioButton("RTRS_ApiNone".Translate(), Settings.WeatherApiProvider == "none"))
                Settings.WeatherApiProvider = "none";
            if (listing.RadioButton("RTRS_ApiHeWeather".Translate(), Settings.WeatherApiProvider == "heweather"))
                Settings.WeatherApiProvider = "heweather";
            if (listing.RadioButton("RTRS_ApiOpenWeather".Translate(), Settings.WeatherApiProvider == "openweathermap"))
                Settings.WeatherApiProvider = "openweathermap";

            listing.Gap(10f);

            // Only show detailed weather settings if an API is selected
            if (Settings.WeatherApiProvider != "none")
            {
                // Custom City Input Field
                Rect cityRect = listing.GetRect(24f);
                Widgets.Label(cityRect.LeftPart(0.3f), "RTRS_CityLabel".Translate());
                Settings.CustomCity = Widgets.TextField(cityRect.RightPart(0.7f), Settings.CustomCity);
                TooltipHandler.TipRegion(cityRect, "RTRS_CityTooltip".Translate());

                listing.Gap(5f);

                // API Key Input Field
                Rect keyRect = listing.GetRect(24f);
                Widgets.Label(keyRect.LeftPart(0.3f), "RTRS_ApiKeyLabel".Translate());
                Settings.WeatherApiKey = Widgets.TextField(keyRect.RightPart(0.7f), Settings.WeatherApiKey);

                listing.Gap(5f);

                // Temperature Unit Toggle
                listing.CheckboxLabeled("RTRS_UseCelsius".Translate(), ref Settings.UseCelsius, "RTRS_UseCelsiusTooltip".Translate());
                listing.Gap(5f);

                // Update Interval Slider
                Settings.UpdateIntervalMinutes = (int)listing.SliderLabeled(
                    "RTRS_UpdateInterval".Translate(Settings.UpdateIntervalMinutes),
                    Settings.UpdateIntervalMinutes, 5f, 180f);

                listing.Gap(10f);

                // --- API Test Button (Added Feature) ---
                // Set fixed width for aesthetics
                Rect buttonRect = listing.GetRect(30f);
                buttonRect.width = 200f;

                if (Widgets.ButtonText(buttonRect, "RTRS_TestAPI_Button".Translate()))
                {
                    // Validate settings before allowing test
                    if (Settings.WeatherApiProvider == "none")
                    {
                        Messages.Message("RTRS_TestAPI_None".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                    else if (string.IsNullOrWhiteSpace(Settings.WeatherApiKey))
                    {
                        Messages.Message("RTRS_TestAPI_EmptyKey".Translate(), MessageTypeDefOf.RejectInput, false);
                    }
                    else
                    {
                        // Notify user testing has started, then trigger the synchronous fetch
                        Messages.Message("RTRS_TestAPI_Testing".Translate(), MessageTypeDefOf.NeutralEvent, false);
                        RealWorldProvider.TestApiConnectionSync();
                    }
                }
            }

            listing.Gap(20f);

            // --- 2. Developer Settings Section ---
            listing.Label("<b><color=#FF4444>" + "RTRS_DevTitle".Translate() + "</color></b>");
            listing.GapLine();

            // Debug Mode Toggle
            listing.CheckboxLabeled("RTRS_DebugMode".Translate(), ref Settings.DebugMode, "RTRS_DebugModeTooltip".Translate());

            // End drawing
            listing.End();
            Widgets.EndScrollView();

            base.DoSettingsWindowContents(inRect);
        }

        /// <summary>
        /// Called when the user closes the settings window.
        /// Useful for clearing caches or logging that settings were saved.
        /// </summary>
        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Settings.DebugMode)
            {
                Log.Message("[RimTalk Reality Sync] Settings have been saved and written to disk.");
            }
        }
    }
}