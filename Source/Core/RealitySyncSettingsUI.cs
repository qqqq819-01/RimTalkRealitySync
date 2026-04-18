using System;
using UnityEngine;
using Verse;
using RimWorld;
using RimTalkRealitySync.Platforms.Discord;
using RimTalkRealitySync.UI;

namespace RimTalkRealitySync
{
    public static class RealitySyncSettingsUI
    {
        private enum CategoryTab { RealitySystem, RimPhoneStation, DeveloperUI }
        private static CategoryTab _currentTab = CategoryTab.RealitySystem;
        private static Vector2 _sysScroll, _phoneScroll, _devScroll;
        private static bool _isBindingHotkey = false;

        public static void Draw(Rect inRect, RealitySyncSettings settings)
        {
            // =====================================================================
            // FIXED: Use IsOpen<T>() which is the standard Verse.WindowStack method.
            // This prevents the CS1061 error and correctly detects our floating window.
            // =====================================================================
            if (!Find.WindowStack.IsOpen<RealitySyncSettingsFloatingWindow>())
            {
                if (Widgets.ButtonText(new Rect(inRect.xMax - 200f, inRect.y, 200f, 30f), "<color=#00FFFF>" + "RTRS_Settings_OpenAdvancedUI".Translate() + "</color>"))
                {
                    Find.WindowStack.Add(new RealitySyncSettingsFloatingWindow());
                }
            }

            if (_isBindingHotkey && Event.current.isKey && Event.current.keyCode != KeyCode.None)
            {
                settings.RimPhoneHotkey = Event.current.keyCode;
                _isBindingHotkey = false;
                Event.current.Use();
            }

            float leftWidth = 180f;
            Rect leftNavRect = new Rect(inRect.x, inRect.y, leftWidth, inRect.height);
            Rect contentRect = new Rect(inRect.x + leftWidth + 15f, inRect.y, inRect.width - leftWidth - 15f, inRect.height);

            Widgets.DrawBoxSolid(leftNavRect, new Color(0.12f, 0.12f, 0.12f, 0.8f));

            Listing_Standard navListing = new Listing_Standard();
            navListing.Begin(leftNavRect.ContractedBy(10f));

            Text.Font = GameFont.Medium;
            navListing.Label("<b><color=#88AAFF>" + "RTRS_Category_Name".Translate() + "</color></b>");
            Text.Font = GameFont.Small;
            navListing.GapLine();
            navListing.Gap(10f);

            if (DrawNavButton(navListing, "RTRS_Tab_RealitySystem".Translate(), _currentTab == CategoryTab.RealitySystem)) _currentTab = CategoryTab.RealitySystem;
            if (DrawNavButton(navListing, "RTRS_Tab_RimPhoneStation".Translate(), _currentTab == CategoryTab.RimPhoneStation)) _currentTab = CategoryTab.RimPhoneStation;
            if (DrawNavButton(navListing, "RTRS_Tab_DeveloperUI".Translate(), _currentTab == CategoryTab.DeveloperUI)) _currentTab = CategoryTab.DeveloperUI;

            navListing.End();

            switch (_currentTab)
            {
                case CategoryTab.RealitySystem: DrawRealitySystem(contentRect, settings); break;
                case CategoryTab.RimPhoneStation: DrawRimPhoneStation(contentRect, settings); break;
                case CategoryTab.DeveloperUI: DrawDeveloperUI(contentRect, settings); break;
            }
        }

        private static void DrawRealitySystem(Rect rect, RealitySyncSettings settings)
        {
            Widgets.BeginScrollView(rect, ref _sysScroll, new Rect(0, 0, rect.width - 16f, 500f));
            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0, 0, rect.width - 16f, 500f));

            l.Label("<b><color=#ffcc00>" + "RTRS_WeatherApiTitle".Translate() + "</color></b>");
            l.GapLine();

            if (l.RadioButton("RTRS_ApiNone".Translate(), settings.WeatherApiProvider == "none")) settings.WeatherApiProvider = "none";
            if (l.RadioButton("RTRS_ApiHeWeather".Translate(), settings.WeatherApiProvider == "heweather")) settings.WeatherApiProvider = "heweather";
            if (l.RadioButton("RTRS_ApiOpenWeather".Translate(), settings.WeatherApiProvider == "openweathermap")) settings.WeatherApiProvider = "openweathermap";

            l.Gap(15f);

            if (settings.WeatherApiProvider != "none")
            {
                Rect cityRect = l.GetRect(24f);
                Widgets.Label(cityRect.LeftPart(0.3f), "RTRS_CityLabel".Translate());
                settings.CustomCity = Widgets.TextField(cityRect.RightPart(0.7f), settings.CustomCity);

                l.Gap(5f);

                Rect keyRect = l.GetRect(24f);
                Widgets.Label(keyRect.LeftPart(0.3f), "RTRS_ApiKeyLabel".Translate());
                if (settings.WeatherApiProvider == "openweathermap")
                {
                    settings.OpenWeatherApiKey = Widgets.TextField(keyRect.RightPart(0.7f), settings.OpenWeatherApiKey);
                }
                else if (settings.WeatherApiProvider == "heweather")
                {
                    settings.HeWeatherApiKey = Widgets.TextField(keyRect.RightPart(0.7f), settings.HeWeatherApiKey);
                    l.Gap(5f);
                    Rect hostRect = l.GetRect(24f);
                    Widgets.Label(hostRect.LeftPart(0.3f), "RTRS_ApiHostLabel".Translate());
                    settings.HeWeatherApiHost = Widgets.TextField(hostRect.RightPart(0.7f), settings.HeWeatherApiHost);
                }

                l.Gap(10f);
                l.CheckboxLabeled("RTRS_UseCelsius".Translate(), ref settings.UseCelsius);
                l.Gap(5f);
                settings.UpdateIntervalMinutes = (int)l.SliderLabeled("RTRS_UpdateInterval".Translate(settings.UpdateIntervalMinutes), settings.UpdateIntervalMinutes, 5f, 180f);

                l.Gap(15f);
                Rect buttonRect = l.GetRect(30f);
                buttonRect.width = 200f;
                if (Widgets.ButtonText(buttonRect, "RTRS_TestAPI_Button".Translate()))
                {
                    RealWorldProvider.TestApiConnectionSync();
                }
            }

            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawRimPhoneStation(Rect rect, RealitySyncSettings settings)
        {
            Widgets.BeginScrollView(rect, ref _phoneScroll, new Rect(0, 0, rect.width - 16f, 1000f));
            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0, 0, rect.width - 16f, 1000f));

            l.Label("<b><color=#FFCC00>" + "RTRS_AegisAuthTitle".Translate() + "</color></b>");
            l.GapLine();
            if (string.IsNullOrEmpty(settings.LinkedDiscordUserId))
            {
                l.Label("<color=#CCCCCC>" + "RTRS_AegisAuthDesc".Translate() + "</color>");
                Rect authRect = l.GetRect(24f);
                Widgets.Label(authRect.LeftPart(0.3f), "RTRS_LoginKeyLabel".Translate());
                if (string.IsNullOrEmpty(settings.PlayerLinkKey))
                {
                    settings.PlayerLinkKey = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                }

                Rect keyTextRect = new Rect(authRect.x + authRect.width * 0.3f, authRect.y, 100f, 24f);
                Widgets.Label(keyTextRect, $"<b><color=#00FFAA>{settings.PlayerLinkKey}</color></b>");

                if (Mouse.IsOver(keyTextRect))
                {
                    Widgets.DrawHighlight(keyTextRect);
                    TooltipHandler.TipRegion(keyTextRect, "RTRS_CopyCommandTooltip".Translate());
                }

                if (Widgets.ButtonInvisible(keyTextRect))
                {
                    GUIUtility.systemCopyBuffer = $"/login {settings.PlayerLinkKey}";
                    Messages.Message("RTRS_CommandCopied".Translate(), MessageTypeDefOf.PositiveEvent, false);
                }

                if (Widgets.ButtonText(new Rect(authRect.xMax - 110f, authRect.y, 110f, 24f), "RTRS_NewKeyBtn".Translate()))
                {
                    settings.PlayerLinkKey = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                }

                l.Label($"<color=#888888>{"RTRS_LoginInstruction".Translate(settings.PlayerLinkKey)}</color>");
            }
            else
            {
                // FIXED: RimWorld's .Translate(arg) automatically capitalizes the first letter of string arguments.
                // We use standard string.Format to preserve the exact case of the user's account name (e.g. qqqq819_01).
                string boundText = string.Format("RTRS_BoundSuccess".Translate().ToString(), $"<b>{settings.LinkedDiscordUsername}</b>");
                l.Label("<color=#00FFAA>" + boundText + "</color>");

                Rect unlinkRect = l.GetRect(24f);
                if (Widgets.ButtonText(new Rect(unlinkRect.x, unlinkRect.y, 150f, 24f), "RTRS_UnlinkBtn".Translate()))
                {
                    // TRANSLATION NOTE: I added RTRS_Msg_ForceUnlinked here, which will need to be added to XML in the next step.
                    RimPhoneCommandProcessor.OmniBroadcast("RimOS 系统", "RTRS_Msg_ForceUnlinked".Translate(settings.LinkedDiscordUsername), settings);

                    settings.LinkedDiscordUserId = "";
                    settings.LinkedDiscordUsername = "";
                    settings.LinkedDiscordAvatarUrl = "";
                    settings.PlayerLinkKey = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                }
                l.Label("<color=#888888>" + "RTRS_LogoutInstruction".Translate() + "</color>");
            }
            l.Gap(20f);

            l.Label("<b><color=#FFCC00>" + "RTRS_CommPlatformTitle".Translate() + "</color></b>");
            l.GapLine();

            // =====================================================================
            // PLATFORM SWITCHER WITH ANTI-GHOSTING
            // =====================================================================
            if (l.RadioButton("RTRS_PlatformDiscord".Translate(), settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord))
            {
                settings.ActivePlatform = RealitySyncSettings.PlatformType.Discord;
                GUI.FocusControl(null);
            }
            if (l.RadioButton("RTRS_PlatformKook".Translate(), settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook))
            {
                settings.ActivePlatform = RealitySyncSettings.PlatformType.Kook;
                GUI.FocusControl(null);
            }

            // =====================================================================
            // NEW: Omni-Broadcast Range Controls
            // =====================================================================
            l.Gap(10f);
            l.Label("<b><color=#FFCC00>" + "RTRS_BroadcastRangeTitle".Translate() + "</color></b>");
            l.CheckboxLabeled("RTRS_BroadcastDiscord".Translate(), ref settings.BroadcastToDiscord, "RTRS_BroadcastDiscordDesc".Translate());
            l.CheckboxLabeled("RTRS_BroadcastKook".Translate(), ref settings.BroadcastToKook, "RTRS_BroadcastKookDesc".Translate());

            l.Gap(15f);

            if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord)
            {
                l.Label("<color=#5865F2><b>" + "RTRS_DiscordConfigTitle".Translate() + "</b></color>");
                Rect tokenRect = l.GetRect(24f);
                Widgets.Label(tokenRect.LeftPart(0.3f), "RTRS_DiscordBotToken".Translate());
                settings.DiscordBotToken = Widgets.TextField(tokenRect.RightPart(0.7f), settings.DiscordBotToken);

                l.Gap(5f);
                Rect channelRect = l.GetRect(24f);
                Widgets.Label(channelRect.LeftPart(0.3f), "RTRS_DiscordChannelId".Translate());
                settings.DiscordChannelId = Widgets.TextField(channelRect.RightPart(0.7f), settings.DiscordChannelId);

                l.Gap(5f);
                Rect webhookRect = l.GetRect(24f);
                Widgets.Label(webhookRect.LeftPart(0.3f), "RTRS_DiscordWebhookUrl".Translate());
                settings.DiscordWebhookUrl = Widgets.TextField(webhookRect.RightPart(0.7f), settings.DiscordWebhookUrl);
            }
            else if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook)
            {
                l.Label("<color=#00ff00><b>" + "RTRS_KookConfigTitle".Translate() + "</b></color>");
                Rect tokenRect = l.GetRect(24f);
                Widgets.Label(tokenRect.LeftPart(0.3f), "RTRS_KookBotToken".Translate());
                settings.KookBotToken = Widgets.TextField(tokenRect.RightPart(0.7f), settings.KookBotToken);

                l.Gap(5f);
                Rect channelRect = l.GetRect(24f);
                Widgets.Label(channelRect.LeftPart(0.3f), "RTRS_KookChannelId".Translate());
                settings.KookChannelId = Widgets.TextField(channelRect.RightPart(0.7f), settings.KookChannelId);

                l.Label("<color=#888888><i>" + "RTRS_KookNotice".Translate() + "</i></color>");
            }

            // =====================================================================
            // FIXED: System Avatar URL Input (Platform Specific Rendering)
            // =====================================================================
            l.Gap(5f);
            if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord)
            {
                Rect sysAvatarRect = l.GetRect(24f);
                Widgets.Label(sysAvatarRect.LeftPart(0.3f), "RTRS_SystemAvatarUrl".Translate());
                settings.SystemAvatarUrl = Widgets.TextField(sysAvatarRect.RightPart(0.7f), settings.SystemAvatarUrl);
            }
            else if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook)
            {
                l.Label("<color=#888888><i>" + "RTRS_KookSystemAvatarNotice".Translate() + "</i></color>");
            }

            l.Gap(15f);
            l.Label("<b><color=#FFCC00>" + "RTRS_LocalStorageTitle".Translate() + "</color></b>");
            l.GapLine();

            Rect pathRect = l.GetRect(24f);
            Widgets.Label(pathRect.LeftPart(0.3f), "RTRS_CustomGalleryPath".Translate());
            settings.CustomGalleryPath = Widgets.TextField(pathRect.RightPart(0.7f), settings.CustomGalleryPath);

            l.Gap(5f);
            Rect bpRect = l.GetRect(24f);
            Widgets.Label(bpRect.LeftPart(0.3f), "RTRS_LastSyncedMsgId".Translate());

            string currentSyncId = settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord ? settings.LastDiscordMessageId : (settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook ? settings.LastKookMessageId : settings.LastQQMessageId);
            string newSyncId = Widgets.TextField(new Rect(bpRect.x + bpRect.width * 0.3f, bpRect.y, bpRect.width * 0.45f, bpRect.height), currentSyncId);

            if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord) settings.LastDiscordMessageId = newSyncId;
            else if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook) settings.LastKookMessageId = newSyncId;
            else settings.LastQQMessageId = newSyncId;

            if (Widgets.ButtonText(new Rect(bpRect.xMax - 100f, bpRect.y, 100f, 24f), "RTRS_ClearCacheBtn".Translate()))
            {
                if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord) settings.LastDiscordMessageId = "";
                else if (settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook) settings.LastKookMessageId = "";
                else settings.LastQQMessageId = "";
            }

            l.Gap(15f);
            Rect hotkeyRect = l.GetRect(24f);
            Widgets.Label(hotkeyRect.LeftPart(0.3f), "RTRS_RimPhoneHotkey".Translate());
            string buttonText = _isBindingHotkey ? "RTRS_PressAnyKey".Translate().ToString() : settings.RimPhoneHotkey.ToString();
            if (Widgets.ButtonText(hotkeyRect.RightPart(0.7f), buttonText))
            {
                _isBindingHotkey = true;
            }

            l.Gap(10f);
            settings.InboxRefreshIntervalSeconds = (int)l.SliderLabeled("RTRS_InboxRefreshInterval".Translate(settings.InboxRefreshIntervalSeconds), settings.InboxRefreshIntervalSeconds, 5f, 60f);

            l.Gap(20f);
            l.Label("<b><color=#FFCC00>" + "RTRS_AvatarFiltersTitle".Translate() + "</color></b>");
            l.GapLine();
            l.CheckboxLabeled("RTRS_LockAllAvatars".Translate(), ref settings.LockAllAvatarUpdates, "RTRS_LockAllAvatarsDesc".Translate());
            l.Gap(5f);
            l.CheckboxLabeled("RTRS_AllowColonist".Translate(), ref settings.AllowAvatar_Colonist);
            l.CheckboxLabeled("RTRS_AllowSlave".Translate(), ref settings.AllowAvatar_Slave);
            l.CheckboxLabeled("RTRS_AllowPrisoner".Translate(), ref settings.AllowAvatar_Prisoner);
            l.CheckboxLabeled("RTRS_AllowVisitor".Translate(), ref settings.AllowAvatar_Visitor);
            l.CheckboxLabeled("RTRS_AllowHostile".Translate(), ref settings.AllowAvatar_Hostile);
            l.CheckboxLabeled("RTRS_AllowNonHuman".Translate(), ref settings.AllowAvatar_NonHuman);

            l.End();
            Widgets.EndScrollView();
        }

        private static void DrawDeveloperUI(Rect rect, RealitySyncSettings settings)
        {
            Widgets.BeginScrollView(rect, ref _devScroll, new Rect(0, 0, rect.width - 16f, 500f));
            Listing_Standard l = new Listing_Standard();
            l.Begin(new Rect(0, 0, rect.width - 16f, 500f));

            l.Label("<b><color=#FFCC00>" + "RTRS_DevTitle".Translate() + "</color></b>");
            l.GapLine();
            l.CheckboxLabeled("RTRS_DebugMode".Translate(), ref settings.DebugMode);

            l.Gap(10f);
            l.CheckboxLabeled("RTRS_PauseOnImage".Translate(), ref settings.ImageViewerPausesGame, "RTRS_PauseOnImageDesc".Translate());

            // FIXED: Added checkbox for Sync Avatar Gizmo visibility
            l.Gap(10f);
            l.CheckboxLabeled("RTRS_ShowSyncAvatarGizmo".Translate(), ref settings.ShowSyncAvatarGizmo, "RTRS_ShowSyncAvatarGizmoDesc".Translate());

            l.Gap(20f);

            l.Label("<b><color=#FFCC00>" + "RTRS_RimPhoneUITester".Translate() + "</color></b>");
            l.GapLine();
            Rect testBtn = l.GetRect(30f);
            if (Widgets.ButtonText(testBtn, "RTRS_TestRimPhoneUI".Translate()))
            {
                RimPhoneStateManager.ToggleWindow();
            }

            l.End();
            Widgets.EndScrollView();
        }

        private static bool DrawNavButton(Listing_Standard l, string text, bool active)
        {
            Rect btnRect = l.GetRect(35f);
            if (active) Widgets.DrawBoxSolid(btnRect, new Color(0.2f, 0.4f, 0.6f, 0.5f));
            else if (Mouse.IsOver(btnRect)) Widgets.DrawBoxSolid(btnRect, new Color(0.3f, 0.3f, 0.3f, 0.3f));

            Widgets.Label(new Rect(btnRect.x + 10f, btnRect.y + 7f, btnRect.width - 20f, 24f), active ? $"<b>{text}</b>" : text);
            return Widgets.ButtonInvisible(btnRect);
        }
    }

    public class RealitySyncSettingsFloatingWindow : Window
    {
        // =====================================================================
        // Resize to match vanilla RimWorld Mod Settings window perfectly (900x700)
        // =====================================================================
        public override Vector2 InitialSize => new Vector2(900f, 700f);

        public RealitySyncSettingsFloatingWindow()
        {
            this.draggable = true;
            this.resizeable = false;
            this.doCloseX = true;
            this.closeOnAccept = false;
            this.closeOnCancel = true;
            this.forcePause = false;
            this.layer = WindowLayer.Dialog;

            this.preventCameraMotion = false;
            this.absorbInputAroundWindow = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            RealitySyncSettingsUI.Draw(inRect, RimTalkRealitySyncMod.Settings);
        }

        public override void PostClose()
        {
            base.PostClose();
            RimTalkRealitySyncMod.Settings.Write();
            RimPhoneCore.ResolveGalleryPath();
        }
    }
}