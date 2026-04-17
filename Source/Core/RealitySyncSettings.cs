using Verse;
using UnityEngine;

namespace RimTalkRealitySync
{
    public class RealitySyncSettings : ModSettings
    {
        public string WeatherApiProvider = "none";
        public string OpenWeatherApiKey = "";
        public string HeWeatherApiKey = "";
        public string HeWeatherApiHost = "";
        public string CustomCity = "Beijing";
        public bool UseCelsius = true;
        public int UpdateIntervalMinutes = 30;
        public bool DebugMode = false;

        // =====================================================================
        // MULTI-PLATFORM PROTOCOL (Phase 2)
        // =====================================================================
        public enum PlatformType { Discord, Kook, QQ }
        public PlatformType ActivePlatform = PlatformType.Discord;

        public string KookBotToken = "";
        public string KookChannelId = "";
        public string LastKookMessageId = ""; // NEW: For Kook sync tracking

        // =====================================================================
        // NEW: QQ Guild (Channel) Settings
        // Uses AppID and AppSecret for OAuth 2.0 Client Credentials flow.
        // =====================================================================
        public string QQAppID = "";
        public string QQAppSecret = "";
        public string QQChannelId = "";
        public string LastQQMessageId = "";

        public string CustomGalleryPath = "";

        public string DiscordBotToken = "";
        public string DiscordChannelId = "";
        public string LastDiscordMessageId = "";
        public string DiscordWebhookUrl = "";

        // =====================================================================
        // NEW: Omni-Broadcast Toggles
        // Allows independent control over which platforms receive outgoing messages.
        // =====================================================================
        public bool BroadcastToDiscord = true;
        public bool BroadcastToKook = true;
        public bool BroadcastToQQ = true;

        // =====================================================================
        // NEW: System Notification Avatar
        // Allows the user to customize the profile picture of the "RimOS System"
        // =====================================================================
        public string SystemAvatarUrl = "";

        public string PlayerLinkKey = "";
        public string LinkedDiscordUserId = "";
        public string LinkedDiscordUsername = "";
        public string LinkedDiscordAvatarUrl = "";

        public bool ImageViewerPausesGame = false;
        public KeyCode RimPhoneHotkey = KeyCode.P;
        public int InboxRefreshIntervalSeconds = 10;

        public bool LockAllAvatarUpdates = false;
        public bool AllowAvatar_Colonist = true;
        public bool AllowAvatar_Slave = false;
        public bool AllowAvatar_Prisoner = false;
        public bool AllowAvatar_Visitor = false;
        public bool AllowAvatar_Hostile = false;
        public bool AllowAvatar_NonHuman = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref WeatherApiProvider, "weatherApiProvider", "none");
            Scribe_Values.Look(ref OpenWeatherApiKey, "openWeatherApiKey", "");
            Scribe_Values.Look(ref HeWeatherApiKey, "heWeatherApiKey", "");
            Scribe_Values.Look(ref HeWeatherApiHost, "heWeatherApiHost", "");
            Scribe_Values.Look(ref CustomCity, "customCity", "Beijing");
            Scribe_Values.Look(ref UseCelsius, "useCelsius", true);
            Scribe_Values.Look(ref UpdateIntervalMinutes, "updateIntervalMinutes", 30);
            Scribe_Values.Look(ref DebugMode, "debugMode", false);

            Scribe_Values.Look(ref ActivePlatform, "activePlatform", PlatformType.Discord);
            Scribe_Values.Look(ref KookBotToken, "kookBotToken", "");
            Scribe_Values.Look(ref KookChannelId, "kookChannelId", "");
            Scribe_Values.Look(ref LastKookMessageId, "lastKookMessageId", "");
            Scribe_Values.Look(ref CustomGalleryPath, "customGalleryPath", "");

            Scribe_Values.Look(ref DiscordBotToken, "discordBotToken", "");
            Scribe_Values.Look(ref DiscordChannelId, "discordChannelId", "");
            Scribe_Values.Look(ref LastDiscordMessageId, "lastDiscordMessageId", "");
            Scribe_Values.Look(ref DiscordWebhookUrl, "discordWebhookUrl", "");
            Scribe_Values.Look(ref SystemAvatarUrl, "systemAvatarUrl", ""); // Load/Save new setting

            // NEW: Save QQ Settings
            Scribe_Values.Look(ref QQAppID, "qqAppID", "");
            Scribe_Values.Look(ref QQAppSecret, "qqAppSecret", "");
            Scribe_Values.Look(ref LastQQMessageId, "lastQQMessageId", "");

            Scribe_Values.Look(ref PlayerLinkKey, "playerLinkKey", "");
            Scribe_Values.Look(ref LinkedDiscordUserId, "linkedDiscordUserId", "");
            Scribe_Values.Look(ref LinkedDiscordUsername, "linkedDiscordUsername", "");
            Scribe_Values.Look(ref LinkedDiscordAvatarUrl, "linkedDiscordAvatarUrl", "");

            Scribe_Values.Look(ref ImageViewerPausesGame, "imageViewerPausesGame", false);
            Scribe_Values.Look(ref RimPhoneHotkey, "rimPhoneHotkey", KeyCode.P);
            Scribe_Values.Look(ref InboxRefreshIntervalSeconds, "inboxRefreshIntervalSeconds", 10);

            Scribe_Values.Look(ref LockAllAvatarUpdates, "lockAllAvatarUpdates", false);
            Scribe_Values.Look(ref AllowAvatar_Colonist, "allowAvatar_Colonist", true);
            Scribe_Values.Look(ref AllowAvatar_Slave, "allowAvatar_Slave", false);
            Scribe_Values.Look(ref AllowAvatar_Prisoner, "allowAvatar_Prisoner", false);
            Scribe_Values.Look(ref AllowAvatar_Visitor, "allowAvatar_Visitor", false);
            Scribe_Values.Look(ref AllowAvatar_Hostile, "allowAvatar_Hostile", false);
            Scribe_Values.Look(ref AllowAvatar_NonHuman, "allowAvatar_NonHuman", true);

            Scribe_Values.Look(ref BroadcastToDiscord, "broadcastToDiscord", true);
            Scribe_Values.Look(ref BroadcastToKook, "broadcastToKook", true);
            Scribe_Values.Look(ref BroadcastToQQ, "broadcastToQQ", true); // NEW
        }
    }
}