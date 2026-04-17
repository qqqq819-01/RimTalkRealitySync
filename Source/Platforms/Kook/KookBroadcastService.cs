using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimTalkRealitySync.Sync;

namespace RimTalkRealitySync.Platforms.Kook
{
    /// <summary>
    /// Handles sending messages to KOOK. 
    /// Unlike Discord, KOOK does not easily support Webhooks with dynamic avatars per message.
    /// We use KMarkdown (Type 9) to format the output elegantly.
    /// </summary>
    public static class KookBroadcastService
    {
        private class QueuedMessage
        {
            public string Payload;
            public int RetryCount;
            public string DebugPawnName;
        }

        private static Queue<QueuedMessage> _webhookQueue = new Queue<QueuedMessage>();
        private static bool _isProcessingQueue = false;
        private static readonly object _queueLock = new object();
        private const int MAX_RETRIES = 5;

        public static void BroadcastToKook(string pawnName, string rawMessage)
        {
            var settings = RimTalkRealitySyncMod.Settings;
            if (string.IsNullOrWhiteSpace(settings.KookBotToken) || string.IsNullOrWhiteSpace(settings.KookChannelId)) return;

            // =====================================================================
            // FIXED: Retain Dynamic Platform Tags
            // Removed the hardcoded [玩家] string override so tags like [Discord 玩家] survive.
            // =====================================================================
            string displayUsername = pawnName;

            // Clean the message of Unity rich text
            string cleanMessage = TranslateUnityRichTextToKook(rawMessage);

            // Format beautifully using KOOK Markdown (KMarkdown)
            string kMarkdownContent = $"**【{displayUsername}】**\n{cleanMessage}";

            string safeChannelId = EscapeJson(settings.KookChannelId);
            string safeContent = EscapeJson(kMarkdownContent);

            // Payload: type=9 means KMarkdown
            string jsonPayload = $"{{\"type\": 9, \"target_id\": \"{safeChannelId}\", \"content\": \"{safeContent}\"}}";

            lock (_queueLock)
            {
                _webhookQueue.Enqueue(new QueuedMessage { Payload = jsonPayload, RetryCount = 0, DebugPawnName = displayUsername });
                if (!_isProcessingQueue)
                {
                    _isProcessingQueue = true;
                    Task.Run(() => ProcessKookQueue());
                }
            }
        }

        private static void ProcessKookQueue()
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
                var settings = RimTalkRealitySyncMod.Settings;

                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("Content-Type", "application/json");
                        client.Headers.Add("Authorization", $"Bot {settings.KookBotToken.Trim()}");
                        client.Encoding = Encoding.UTF8;

                        string response = client.UploadString("https://www.kookapp.cn/api/v3/message/create", "POST", currentMsg.Payload);

                        // DEBUG: Capture and log the actual response from KOOK
                        if (settings.DebugMode)
                        {
                            RimPhoneEngine.EnqueueMainThreadAction(() => Log.Message($"[RimPhone KOOK] API Response: {response}"));
                        }

                        if (response.Contains("\"code\":0") || response.Contains("\"code\": 0"))
                        {
                            sendSuccess = true;
                        }
                        else
                        {
                            RimPhoneEngine.EnqueueMainThreadAction(() => Log.Warning($"[RimPhone KOOK] Message rejected by server. Data: {response}"));
                            sendSuccess = true; // Drop failed message to prevent infinite loops
                        }
                    }
                }
                catch (Exception ex)
                {
                    currentMsg.RetryCount++;
                    if (currentMsg.RetryCount >= MAX_RETRIES)
                    {
                        if (settings.DebugMode)
                            RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone KOOK] Permanent failure after {MAX_RETRIES} retries: {ex.Message}"));

                        sendSuccess = true; // Drop it
                    }
                    else
                    {
                        int backoffMs = Math.Min(5000, (int)Math.Pow(2, currentMsg.RetryCount) * 1000);
                        System.Threading.Thread.Sleep(backoffMs);
                    }
                }

                if (sendSuccess)
                {
                    lock (_queueLock)
                    {
                        _webhookQueue.Dequeue();
                    }
                    System.Threading.Thread.Sleep(500); // Respect KOOK API rate limits
                }
            }
        }

        private static string TranslateUnityRichTextToKook(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string result = System.Text.RegularExpressions.Regex.Replace(input, @"<color=[^>]+>", "");
            result = result.Replace("</color>", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<i>(.*?)</i>", "*$1*", System.Text.RegularExpressions.RegexOptions.Singleline);
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<b>(.*?)</b>", "**$1**", System.Text.RegularExpressions.RegexOptions.Singleline);
            return result.Trim();
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