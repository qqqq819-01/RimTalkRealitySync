using RimTalkRealitySync.Platforms.Discord;
using RimTalkRealitySync.Sync;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Verse;

namespace RimTalkRealitySync.Platforms.QQ
{
    /// <summary>
    /// Handles fetching messages from the Tencent QQ Guild API.
    /// Utilizes the automated OAuth 2.0 Token manager for authentication.
    /// </summary>
    public static class QGuildFetcher
    {
        public static bool IsFetching = false;

        public static void SyncQQMessagesAsync(bool isManual = false)
        {
            if (IsFetching) return;

            var settings = RimTalkRealitySyncMod.Settings;

            if (string.IsNullOrWhiteSpace(settings.QQAppID) || string.IsNullOrWhiteSpace(settings.QQAppSecret) || string.IsNullOrWhiteSpace(settings.QQChannelId))
            {
                if (isManual) RimPhoneEngine.EnqueueMainThreadAction(() => Verse.Messages.Message("QQ AppID, AppSecret, or Channel ID is missing.", RimWorld.MessageTypeDefOf.RejectInput, false));
                return;
            }

            IsFetching = true;

            Task.Run(() =>
            {
                try
                {
                    // 1. Get the dynamic access token!
                    string token = QQAuthManager.GetValidToken();
                    if (string.IsNullOrEmpty(token))
                    {
                        if (settings.DebugMode) RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error("[RimPhone QQ] Failed to obtain valid access token. Aborting fetch."));
                        return;
                    }

                    string cleanChannelId = settings.QQChannelId.Trim();
                    string url = $"https://api.sgroup.qq.com/channels/{cleanChannelId}/messages?limit=20";

                    string jsonResponse = null;

                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.Encoding = Encoding.UTF8;
                            // =====================================================================
                            // OFFICIAL QQ BOT AUTHENTICATION HEADER
                            // =====================================================================
                            client.Headers.Add("Authorization", $"QQBot {token}");
                            jsonResponse = client.DownloadString(url);
                        }
                    }
                    catch (WebException wex)
                    {
                        RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone QQ] Fetch Failed (Possible expired token or invalid ID): {wex.Message}"));

                        // If Unauthorized, force invalidate the token so it gets refreshed next time
                        if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            QQAuthManager.InvalidateToken();
                        }
                        return;
                    }

                    if (!string.IsNullOrEmpty(jsonResponse))
                    {
                        List<string> chunks = ExtractJsonObjects(jsonResponse);
                        if (chunks.Count > 0)
                        {
                            List<DiscordMessage> newMessages = new List<DiscordMessage>();
                            string newestMsgIdToSave = "";
                            //bool foundAnchor = false;

                            // Like KOOK, if it returns oldest->newest or newest->oldest, 
                            // iterating backwards and anchoring ensures stability.
                            for (int i = chunks.Count - 1; i >= 0; i--)
                            {
                                string chunk = chunks[i];
                                var idMatch = Regex.Match(chunk, @"\""id\""\s*:\s*\""([^\""]+)\""");
                                if (!idMatch.Success) continue;

                                string msgId = idMatch.Groups[1].Value;

                                if (string.IsNullOrEmpty(newestMsgIdToSave)) newestMsgIdToSave = msgId;

                                //if (!string.IsNullOrWhiteSpace(settings.LastQQMessageId) && msgId == settings.LastQQMessageId)
                                //{
                                    //foundAnchor = true;
                                    //break;
                                //}

                                var contentMatch = Regex.Match(chunk, @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*)\""");
                                string rawContent = contentMatch.Success ? contentMatch.Groups[1].Value : "";
                                string cleanContent = Regex.Unescape(rawContent.Replace("\\/", "/"));

                                // Ignore system/bot messages
                                if (chunk.Contains("\"bot\":true") || chunk.Contains("\"bot\": true")) continue;

                                var userMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""username\""\s*:\s*\""([^\""]+)\""", RegexOptions.Singleline);
                                string username = userMatch.Success ? Regex.Unescape(userMatch.Groups[1].Value.Replace("\\/", "/")) : "Unknown";

                                var userIdMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""id\""\s*:\s*\""([^\""]+)\""", RegexOptions.Singleline);
                                string senderId = userIdMatch.Success ? userIdMatch.Groups[1].Value : "";

                                var avatarMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""avatar\""\s*:\s*\""([^\""]+)\""", RegexOptions.Singleline);
                                string senderAvatarUrl = avatarMatch.Success ? avatarMatch.Groups[1].Value.Replace("\\/", "/") : "";

                                // Command interception and routing logic (Same as Discord/KOOK)
                                string trimmedCmd = cleanContent.Trim();
                                bool isSystemCommand = false;
                                string standardizedCmd = trimmedCmd;

                                var cmdMatch = Regex.Match(trimmedCmd, @"^[/\\.,。，~-]\s*(login|logout|人设|persona|移除人设|clearpersona)(?:\s+(.*))?$", RegexOptions.IgnoreCase);
                                if (cmdMatch.Success)
                                {
                                    isSystemCommand = true;
                                    string commandWord = cmdMatch.Groups[1].Value.ToLower();
                                    string args = cmdMatch.Groups[2].Success ? cmdMatch.Groups[2].Value : "";
                                    standardizedCmd = (commandWord == "人设" || commandWord == "persona" || commandWord == "login") ? $"/{commandWord} {args}".Trim() : $"/{commandWord}";
                                }

                                if (isSystemCommand)
                                {
                                    newMessages.Add(new DiscordMessage { Id = msgId, SenderName = username, SenderId = senderId, SenderAvatarUrl = senderAvatarUrl, TargetPawn = "System", Content = standardizedCmd, Timestamp = DateTime.Now, SourcePlatform = "QQ" });
                                    continue;
                                }

                                string targetPawn = "All";
                                bool isPawnSearchDone = false;
                                RimPhoneEngine.EnqueueMainThreadAction(() => {
                                    try
                                    {
                                        foreach (var pawn in RimWorld.PawnsFinder.AllMaps_FreeColonists)
                                        {
                                            string shortName = pawn.Name.ToStringShort;
                                            if (cleanContent.Contains("@" + shortName))
                                            {
                                                targetPawn = shortName;
                                                cleanContent = cleanContent.Replace("@" + shortName, "").Trim();
                                                break;
                                            }
                                            else
                                            {
                                                string[] prefixes = new[] { "/", "\\", ".", ",", "。", "，", "~", "-" };
                                                foreach (string p in prefixes)
                                                {
                                                    if (cleanContent.StartsWith(p + shortName) || cleanContent.StartsWith(p + " " + shortName))
                                                    {
                                                        targetPawn = shortName;
                                                        cleanContent = cleanContent.Substring(cleanContent.IndexOf(shortName) + shortName.Length).Trim();
                                                        break;
                                                    }
                                                }
                                                if (targetPawn != "All") break;
                                            }
                                        }
                                    }
                                    finally { isPawnSearchDone = true; }
                                });

                                while (!isPawnSearchDone) { System.Threading.Thread.Sleep(5); }

                                if (targetPawn == "All") continue;

                                newMessages.Add(new DiscordMessage { Id = msgId, SenderName = username, SenderId = senderId, SenderAvatarUrl = senderAvatarUrl, TargetPawn = targetPawn, Content = cleanContent, Timestamp = DateTime.Now, SourcePlatform = "QQ" });
                            }

                            RimPhoneEngine.EnqueueMainThreadAction(() =>
                            {
                                if (newMessages.Count > 0)
                                {
                                    newMessages.Reverse();
                                    RimPhoneCommandProcessor.ProcessSystemCommands(newMessages, settings);
                                }
                                if (!string.IsNullOrEmpty(newestMsgIdToSave) && newestMsgIdToSave != settings.LastQQMessageId)
                                {
                                    settings.LastQQMessageId = newestMsgIdToSave;
                                    settings.Write();
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (settings.DebugMode) RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone QQ] Background Task Error: {ex.Message}"));
                }
                finally
                {
                    RimPhoneEngine.EnqueueMainThreadAction(() => { IsFetching = false; });
                }
            });
        }

        private static List<string> ExtractJsonObjects(string arrayJson)
        {
            List<string> objs = new List<string>();
            int depth = 0; int start = -1; bool inString = false; bool escape = false;

            for (int i = 0; i < arrayJson.Length; i++)
            {
                char c = arrayJson[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }

                if (!inString)
                {
                    if (c == '{') { if (depth == 0) start = i; depth++; }
                    else if (c == '}') { depth--; if (depth == 0 && start != -1) { objs.Add(arrayJson.Substring(start, i - start + 1)); start = -1; } }
                }
            }
            return objs;
        }
    }
}