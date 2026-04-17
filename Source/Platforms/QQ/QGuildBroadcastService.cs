using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Verse;
using RimTalkRealitySync.Sync;

namespace RimTalkRealitySync.Platforms.QQ
{
    /// <summary>
    /// Handles sending messages to the Tencent QQ Guild API.
    /// Utilizes the automated OAuth 2.0 Token manager.
    /// </summary>
    public static class QGuildBroadcastService
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

        public static void BroadcastToQQ(string pawnName, string rawMessage)
        {
            var settings = RimTalkRealitySyncMod.Settings;
            if (string.IsNullOrWhiteSpace(settings.QQAppID) || string.IsNullOrWhiteSpace(settings.QQChannelId)) return;

            string cleanMessage = TranslateUnityRichTextToQQ(rawMessage);

            // Format for readability (QQ API doesn't support changing avatars easily like Discord Webhooks)
            string displayContent = $"【{pawnName}】\n{cleanMessage}";

            string safeContent = EscapeJson(displayContent);
            string jsonPayload = $"{{\"content\": \"{safeContent}\"}}";

            lock (_queueLock)
            {
                _webhookQueue.Enqueue(new QueuedMessage { Payload = jsonPayload, RetryCount = 0, DebugPawnName = pawnName });
                if (!_isProcessingQueue)
                {
                    _isProcessingQueue = true;
                    Task.Run(() => ProcessQQQueue());
                }
            }
        }

        private static void ProcessQQQueue()
        {
            while (true)
            {
                QueuedMessage currentMsg;
                lock (_queueLock)
                {
                    if (_webhookQueue.Count > 0) currentMsg = _webhookQueue.Peek();
                    else { _isProcessingQueue = false; return; }
                }

                bool sendSuccess = false;
                var settings = RimTalkRealitySyncMod.Settings;

                try
                {
                    string token = QQAuthManager.GetValidToken();
                    if (!string.IsNullOrEmpty(token))
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.Headers.Add("Content-Type", "application/json");
                            client.Headers.Add("Authorization", $"QQBot {token}");
                            client.Encoding = Encoding.UTF8;

                            string url = $"https://api.sgroup.qq.com/channels/{settings.QQChannelId.Trim()}/messages";
                            string response = client.UploadString(url, "POST", currentMsg.Payload);

                            sendSuccess = true;
                        }
                    }
                    else
                    {
                        sendSuccess = true; // Drop it if we permanently can't get a token
                    }
                }
                catch (WebException wex)
                {
                    currentMsg.RetryCount++;

                    if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        QQAuthManager.InvalidateToken(); // Force token refresh on next retry
                    }

                    if (currentMsg.RetryCount >= MAX_RETRIES)
                    {
                        if (settings.DebugMode) RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone QQ] Permanent failure: {wex.Message}"));
                        sendSuccess = true;
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(Math.Min(5000, (int)Math.Pow(2, currentMsg.RetryCount) * 1000));
                    }
                }
                catch (Exception)
                {
                    sendSuccess = true; // Drop on generic unknown errors
                }

                if (sendSuccess)
                {
                    lock (_queueLock) { _webhookQueue.Dequeue(); }
                    System.Threading.Thread.Sleep(1000); // Respect QQ API strict rate limits
                }
            }
        }

        private static string TranslateUnityRichTextToQQ(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            string result = System.Text.RegularExpressions.Regex.Replace(input, @"<color=[^>]+>", "");
            result = result.Replace("</color>", "");
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<i>(.*?)</i>", "$1", System.Text.RegularExpressions.RegexOptions.Singleline);
            result = System.Text.RegularExpressions.Regex.Replace(result, @"<b>(.*?)</b>", "$1", System.Text.RegularExpressions.RegexOptions.Singleline);
            return result.Trim();
        }

        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "").Replace("\t", "\\t");
        }
    }
}