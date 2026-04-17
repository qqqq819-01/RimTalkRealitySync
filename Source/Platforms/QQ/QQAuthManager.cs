using System;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Verse;
using RimTalkRealitySync.Sync;

namespace RimTalkRealitySync.Platforms.QQ
{
    /// <summary>
    /// Manages OAuth 2.0 Client Credentials flow for Tencent QQ API.
    /// Automatically caches and refreshes the Access Token in the background.
    /// </summary>
    public static class QQAuthManager
    {
        private static string _cachedToken = "";
        private static DateTime _expirationTime = DateTime.MinValue;
        private static readonly object _tokenLock = new object();

        /// <summary>
        /// Retrieves a valid access token. If the current token is expired (or close to expiring),
        /// it will automatically request a new one from the Tencent API.
        /// THIS METHOD BLOCKS until a token is retrieved, so it should only be called from background threads!
        /// </summary>
        public static string GetValidToken()
        {
            lock (_tokenLock)
            {
                // Add a 5-minute safety buffer to ensure the token doesn't expire mid-flight
                if (!string.IsNullOrEmpty(_cachedToken) && DateTime.Now < _expirationTime.AddMinutes(-5))
                {
                    return _cachedToken;
                }

                var settings = RimTalkRealitySyncMod.Settings;
                if (string.IsNullOrWhiteSpace(settings.QQAppID) || string.IsNullOrWhiteSpace(settings.QQAppSecret))
                {
                    return null; // Missing credentials, cannot authenticate
                }

                try
                {
                    using (WebClient client = new WebClient())
                    {
                        client.Headers.Add("Content-Type", "application/json");
                        client.Encoding = Encoding.UTF8;

                        // Build the OAuth2 payload
                        string payload = $"{{\"appId\":\"{settings.QQAppID.Trim()}\",\"clientSecret\":\"{settings.QQAppSecret.Trim()}\"}}";

                        // Request a new token from Tencent's official bot endpoint
                        string response = client.UploadString("https://bots.qq.com/app/get_token", "POST", payload);

                        // Extract access_token using Regex (avoids needing heavyweight JSON libraries like Newtonsoft)
                        var tokenMatch = Regex.Match(response, @"\""access_token\""\s*:\s*\""([^\""]+)\""");
                        var expireMatch = Regex.Match(response, @"\""expires_in\""\s*:\s*(\d+)");

                        if (tokenMatch.Success && expireMatch.Success)
                        {
                            _cachedToken = tokenMatch.Groups[1].Value;

                            if (int.TryParse(expireMatch.Groups[1].Value, out int expiresInSeconds))
                            {
                                _expirationTime = DateTime.Now.AddSeconds(expiresInSeconds);
                            }
                            else
                            {
                                // Fallback if parsing fails (usually 7200 seconds / 2 hours)
                                _expirationTime = DateTime.Now.AddHours(1);
                            }

                            if (settings.DebugMode)
                            {
                                RimPhoneEngine.EnqueueMainThreadAction(() =>
                                    Log.Message("[RimPhone QQ] Successfully refreshed OAuth Token."));
                            }

                            return _cachedToken;
                        }
                        else
                        {
                            RimPhoneEngine.EnqueueMainThreadAction(() =>
                                Log.Error($"[RimPhone QQ] Token parser failed. API Response: {response}"));
                            return null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    RimPhoneEngine.EnqueueMainThreadAction(() =>
                        Log.Error($"[RimPhone QQ] Failed to fetch token: {ex.Message}"));
                    return null;
                }
            }
        }

        /// <summary>
        /// Forcefully invalidates the current token. 
        /// Useful if the API returns a 401 Unauthorized despite the token technically not being expired.
        /// </summary>
        public static void InvalidateToken()
        {
            lock (_tokenLock)
            {
                _cachedToken = "";
                _expirationTime = DateTime.MinValue;

                if (RimTalkRealitySyncMod.Settings.DebugMode)
                {
                    RimPhoneEngine.EnqueueMainThreadAction(() =>
                        Log.Warning("[RimPhone QQ] OAuth Token manually invalidated."));
                }
            }
        }
    }
}