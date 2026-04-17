using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using RimWorld;
using RimTalkRealitySync.Sync;

namespace RimTalkRealitySync.Platforms.Discord
{
    public static class DiscordBroadcastService
    {
        private static Dictionary<string, string> _avatarUrlCache = new Dictionary<string, string>();
        private static HashSet<string> _evaluatedPawns = new HashSet<string>();
        private static HashSet<string> _uploadingPawns = new HashSet<string>();

        private class QueuedMessage
        {
            public string Payload;
            public int RetryCount;
            public string DebugPawnName;
        }

        private static Queue<QueuedMessage> _webhookQueue = new Queue<QueuedMessage>();
        private static bool _isProcessingQueue = false;
        private static readonly object _queueLock = new object();
        private const int MAX_RETRIES = 10;

        public static void ForceUpdateAvatar(Pawn pawn)
        {
            if (pawn == null || pawn.Name == null) return;
            string name = pawn.Name.ToStringShort;

            _avatarUrlCache.Remove(name);
            _evaluatedPawns.Remove(name);

            if (!_uploadingPawns.Contains(name))
            {
                UploadPawnAvatarAsync(pawn, name);
            }
        }

        private static bool IsAvatarGenerationAllowed(Pawn pawn)
        {
            var s = RimTalkRealitySyncMod.Settings;
            if (s.LockAllAvatarUpdates) return false;

            if (!pawn.RaceProps.Humanlike) return s.AllowAvatar_NonHuman;

            if (pawn.IsFreeColonist) return s.AllowAvatar_Colonist;
            if (pawn.IsSlaveOfColony) return s.AllowAvatar_Slave;
            if (pawn.IsPrisonerOfColony) return s.AllowAvatar_Prisoner;
            if (pawn.HostileTo(Faction.OfPlayer)) return s.AllowAvatar_Hostile;

            return s.AllowAvatar_Visitor;
        }

        public static void BroadcastToDiscord(string pawnName, string rawMessage)
        {
            var settings = RimTalkRealitySyncMod.Settings;
            string webhookUrl = settings.DiscordWebhookUrl;
            if (string.IsNullOrWhiteSpace(webhookUrl)) return;

            string discordMessage = TranslateUnityRichTextToDiscord(rawMessage);
            string avatarUrl = "";
            string displayUsername = pawnName;

            // =====================================================================
            // FIXED: Flexible Player Avatar Hook
            // Uses .Contains to match dynamic tags like [Discord 玩家] or [本地 玩家]
            // without overriding the meticulously crafted prefix!
            // =====================================================================
            if (pawnName.Contains("玩家"))
            {
                if (!string.IsNullOrEmpty(settings.LinkedDiscordAvatarUrl))
                {
                    avatarUrl = settings.LinkedDiscordAvatarUrl;
                }
            }
            else if (pawnName == "RimOS 系统")
            {
                displayUsername = pawnName;
                avatarUrl = string.IsNullOrWhiteSpace(settings.SystemAvatarUrl) ?
                            "https://i.imgur.com/3q174fL.png" : settings.SystemAvatarUrl;
            }
            else
            {
                Pawn pawn = GetPawnByName(pawnName);
                if (_avatarUrlCache.TryGetValue(pawnName, out string cachedUrl))
                {
                    avatarUrl = cachedUrl;
                }
                else if (pawn != null && !_evaluatedPawns.Contains(pawnName))
                {
                    _evaluatedPawns.Add(pawnName);

                    if (IsAvatarGenerationAllowed(pawn))
                    {
                        UploadPawnAvatarAsync(pawn, pawnName);
                    }
                }
            }

            string safeName = EscapeJson(displayUsername);
            string safeMessage = EscapeJson(discordMessage);
            string jsonPayload;

            if (!string.IsNullOrEmpty(avatarUrl))
            {
                jsonPayload = $"{{\"username\": \"{safeName}\", \"avatar_url\": \"{avatarUrl}\", \"content\": \"{safeMessage}\"}}";
            }
            else
            {
                jsonPayload = $"{{\"username\": \"{safeName}\", \"content\": \"{safeMessage}\"}}";
            }

            lock (_queueLock)
            {
                _webhookQueue.Enqueue(new QueuedMessage { Payload = jsonPayload, RetryCount = 0, DebugPawnName = displayUsername });
                if (!_isProcessingQueue)
                {
                    _isProcessingQueue = true;
                    Task.Run(() => ProcessWebhookQueue());
                }
            }
        }

        private static void ProcessWebhookQueue()
        {
            while (true)
            {
                QueuedMessage currentMsg;
                lock (_queueLock)
                {
                    if (_webhookQueue.Count > 0)
                    {
                        currentMsg = _webhookQueue.Peek();
                    }
                    else
                    {
                        _isProcessingQueue = false;
                        return;
                    }
                }

                bool sendSuccess = false;
                string webhookUrl = RimTalkRealitySyncMod.Settings.DiscordWebhookUrl;

                if (!string.IsNullOrWhiteSpace(webhookUrl))
                {
                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.Headers.Add("Content-Type", "application/json");
                            client.Encoding = Encoding.UTF8;
                            client.UploadString(webhookUrl, "POST", currentMsg.Payload);
                            sendSuccess = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        currentMsg.RetryCount++;
                        if (currentMsg.RetryCount >= MAX_RETRIES)
                        {
                            if (RimTalkRealitySyncMod.Settings.DebugMode)
                                RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone Webhook] Permanent failure after {MAX_RETRIES} retries: {ex.Message}"));

                            RimPhoneEngine.EnqueueMainThreadAction(() => Verse.Messages.Message("RTRS_Msg_NetworkFailureDrop".Translate(currentMsg.DebugPawnName), MessageTypeDefOf.RejectInput, false));
                            sendSuccess = true;
                        }
                        else
                        {
                            int backoffMs = Math.Min(10000, (int)Math.Pow(2, currentMsg.RetryCount) * 1000);

                            if (RimTalkRealitySyncMod.Settings.DebugMode)
                                RimPhoneEngine.EnqueueMainThreadAction(() => Log.Warning($"[RimPhone Webhook] Network error. Retrying ({currentMsg.RetryCount}/{MAX_RETRIES}) in {backoffMs}ms..."));

                            System.Threading.Thread.Sleep(backoffMs);
                        }
                    }
                }
                else
                {
                    sendSuccess = true;
                }

                if (sendSuccess)
                {
                    lock (_queueLock)
                    {
                        _webhookQueue.Dequeue();
                    }
                    System.Threading.Thread.Sleep(500);
                }
            }
        }

        private static string TranslateUnityRichTextToDiscord(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string result = Regex.Replace(input, @"<color=[^>]+>", "");
            result = result.Replace("</color>", "");
            result = Regex.Replace(result, @"<i>(.*?)</i>", "*$1*", RegexOptions.Singleline);
            result = Regex.Replace(result, @"<b>(.*?)</b>", "**$1**", RegexOptions.Singleline);
            return result.Trim();
        }

        private static Pawn GetPawnByName(string name)
        {
            string cleanName = name.Replace("[玩家] ", "");

            foreach (var pawn in PawnsFinder.AllMaps_FreeColonistsAndPrisonersSpawned)
            {
                if (pawn.Name != null && pawn.Name.ToStringShort == cleanName) return pawn;
            }

            foreach (var pawn in Find.CurrentMap?.mapPawns?.AllPawnsSpawned ?? new List<Pawn>())
            {
                if (pawn.Name != null && pawn.Name.ToStringShort == cleanName) return pawn;
            }
            return null;
        }

        public static void InvalidateAvatar(string pawnName)
        {
            if (_avatarUrlCache.ContainsKey(pawnName))
            {
                _avatarUrlCache.Remove(pawnName);
            }
        }

        private static void UploadPawnAvatarAsync(Pawn pawn, string cacheKeyName)
        {
            _uploadingPawns.Add(cacheKeyName);

            RimPhoneEngine.EnqueueMainThreadAction(() =>
            {
                try
                {
                    RenderTexture rt = PortraitsCache.Get(
                        pawn,
                        new Vector2(128f, 128f),
                        Rot4.South,
                        new Vector3(0f, 0f, 0.15f),
                        1.25f
                    );

                    RenderTexture.active = rt;
                    Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                    tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    byte[] pngBytes = tex.EncodeToPNG();
                    string base64String = Convert.ToBase64String(pngBytes);
                    UnityEngine.Object.Destroy(tex);

                    Task.Run(() =>
                    {
                        try
                        {
                            using (var client = new WebClient())
                            {
                                var reqParm = new System.Collections.Specialized.NameValueCollection();
                                reqParm.Add("key", "6d207e02198a847aa98d0a2a901485a5");
                                reqParm.Add("action", "upload");
                                reqParm.Add("source", base64String);
                                reqParm.Add("format", "json");

                                byte[] responseBytes = client.UploadValues("https://freeimage.host/api/1/upload", "POST", reqParm);
                                string responseString = Encoding.UTF8.GetString(responseBytes);

                                var match = Regex.Match(responseString, @"\""url\""\s*:\s*\""(http[^\s\""]+)\""");
                                if (match.Success)
                                {
                                    string imageUrl = match.Groups[1].Value.Replace("\\/", "/");
                                    _avatarUrlCache[cacheKeyName] = imageUrl;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // FIXED: ex is now properly logged when DebugMode is active, resolving CS0168.
                            RimPhoneEngine.EnqueueMainThreadAction(() =>
                            {
                                _evaluatedPawns.Remove(cacheKeyName);
                                if (RimTalkRealitySyncMod.Settings.DebugMode)
                                    Log.Warning($"[RimPhone] Avatar upload failed for {cacheKeyName}. Retry lock removed. Error: {ex.Message}");
                            });
                        }
                        finally
                        {
                            RimPhoneEngine.EnqueueMainThreadAction(() => _uploadingPawns.Remove(cacheKeyName));
                        }
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning($"[RimPhone] Failed to capture portrait for {cacheKeyName}: {ex.Message}");
                    _uploadingPawns.Remove(cacheKeyName);
                }
            });
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "")
                      .Replace("\t", "\\t");
        }
    }
}