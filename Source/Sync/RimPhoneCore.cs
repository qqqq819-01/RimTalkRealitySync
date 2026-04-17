using RimTalkRealitySync.Sync;
using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimTalkRealitySync
{
    /// <summary>
    /// The core bootstrapper and central utility hub for RimPhone.
    /// Replaces the old SyncManager's static constructor duties safely.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class RimPhoneCore
    {
        public static string GalleryPath;

        static RimPhoneCore()
        {
            // Security protocol for Discord and Image hosting APIs
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };

            ResolveGalleryPath();

            // Inject the Main Thread Engine into RimWorld's lifecycle
            var go = new GameObject("RimPhoneEngine");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<RimPhoneEngine>();

            // Kick off the initial platform fetch cycle safely based on active setting
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                Task.Run(() => {
                    if (RimTalkRealitySyncMod.Settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord)
                        Platforms.Discord.DiscordFetcher.SyncDiscordMessagesAsync(false);
                    else if (RimTalkRealitySyncMod.Settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook)
                        Platforms.Kook.KookFetcher.SyncKookMessagesAsync(false);
                });
            });
        }

        public static void ResolveGalleryPath()
        {
            var settings = RimTalkRealitySyncMod.Settings;
            if (!string.IsNullOrWhiteSpace(settings.CustomGalleryPath))
                GalleryPath = settings.CustomGalleryPath;
            else
                GalleryPath = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimTalk_Gallery");

            if (!Directory.Exists(GalleryPath))
            {
                try { Directory.CreateDirectory(GalleryPath); }
                catch (Exception ex) { Log.Error($"[RimPhone] Failed to create gallery directory: {ex.Message}"); }
            }
        }

        public static void DownloadImageSafely(string url, string filename, bool debugMode)
        {
            Task.Run(() =>
            {
                try
                {
                    string safeName = filename ?? Path.GetFileName(new Uri(url).LocalPath);
                    string localFilePath = Path.Combine(GalleryPath, safeName);

                    if (!File.Exists(localFilePath))
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.DownloadFile(url, localFilePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimPhoneEngine.EnqueueMainThreadAction(() => Log.Warning($"[RimPhone] Failed to download image from {url}: {ex.Message}"));
                }
            });
        }
    }
}