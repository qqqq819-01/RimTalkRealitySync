using RimTalkRealitySync.Sync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimTalkRealitySync.Platforms.Discord
{
    /// <summary>
    /// Handles fetching and parsing messages exclusively from the Discord API.
    /// Normalizes commands and queues them for the core engine.
    /// </summary>
    public static class DiscordFetcher
    {
        public static bool IsFetching = false;

        public static void SyncDiscordMessagesAsync(bool isManual = false)
        {
            if (IsFetching) return;

            var settings = RimTalkRealitySyncMod.Settings;

            if (string.IsNullOrWhiteSpace(settings.DiscordBotToken) || string.IsNullOrWhiteSpace(settings.DiscordChannelId))
            {
                if (isManual) RimPhoneEngine.EnqueueMainThreadAction(() => Verse.Messages.Message("RTRS_Msg_MissingDiscordAuth".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false));
                return;
            }

            IsFetching = true;

            Task.Run(() =>
            {
                try
                {
                    string cleanChannelId = settings.DiscordChannelId.Trim();
                    string cleanToken = settings.DiscordBotToken.Trim();
                    string url = $"https://discord.com/api/v10/channels/{cleanChannelId}/messages?limit=20";

                    if (!string.IsNullOrWhiteSpace(settings.LastDiscordMessageId))
                    {
                        url += $"&after={settings.LastDiscordMessageId.Trim()}";
                    }

                    string jsonResponse = null;

                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.Encoding = Encoding.UTF8;
                            client.Headers.Add("User-Agent", "DiscordBot (RimTalkRealitySync, 1.0)");
                            client.Headers.Add("Authorization", $"Bot {cleanToken}");
                            jsonResponse = client.DownloadString(url);
                        }
                    }
                    catch (WebException wex)
                    {
                        if (settings.DebugMode)
                            RimPhoneEngine.EnqueueMainThreadAction(() => Log.Warning($"[RimPhone] API Sync rejected: {wex.Message}"));
                        return;
                    }

                    if (!string.IsNullOrEmpty(jsonResponse))
                    {
                        List<string> chunks = ExtractJsonObjects(jsonResponse);
                        if (chunks.Count > 0)
                        {
                            List<DiscordMessage> newMessages = new List<DiscordMessage>();
                            ulong currentSavedId = 0;
                            if (!string.IsNullOrWhiteSpace(settings.LastDiscordMessageId))
                                ulong.TryParse(settings.LastDiscordMessageId, out currentSavedId);

                            ulong maxFetchedId = 0;

                            foreach (string chunk in chunks)
                            {
                                ulong trueMsgId = 0;
                                var allIds = Regex.Matches(chunk, @"\""id\""\s*:\s*\""(\d+)\""");
                                foreach (Match m in allIds)
                                {
                                    if (ulong.TryParse(m.Groups[1].Value, out ulong parsedId))
                                    {
                                        if (parsedId > trueMsgId) trueMsgId = parsedId;
                                    }
                                }

                                if (trueMsgId == 0) continue;
                                string msgId = trueMsgId.ToString();

                                if (trueMsgId > maxFetchedId) maxFetchedId = trueMsgId;

                                var contentMatch = Regex.Match(chunk, @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*)\""");
                                string rawContent = contentMatch.Success ? contentMatch.Groups[1].Value : "";
                                string cleanContent = DecodeJsonString(rawContent);

                                bool dropEntireMessage = false;
                                string interceptedExt = "";

                                var urlMatches = Regex.Matches(chunk, @"\""url\""\s*:\s*\""(https?://cdn\.discordapp\.com/attachments/[^\""]+)\""");
                                foreach (Match uMatch in urlMatches)
                                {
                                    string imgUrl = uMatch.Groups[1].Value;
                                    string rawFileName = Path.GetFileName(new Uri(imgUrl).LocalPath);
                                    string safeFileName = Regex.Replace(rawFileName, @"[^a-zA-Z0-9\.\-_]", "");
                                    string ext = Path.GetExtension(safeFileName).ToLower();

                                    if (ext == ".gif" || ext == ".mp4" || ext == ".webm" || ext == ".webp")
                                    {
                                        dropEntireMessage = true;
                                        interceptedExt = ext.ToUpper();
                                        break;
                                    }

                                    RimPhoneCore.DownloadImageSafely(imgUrl, safeFileName, settings.DebugMode);
                                    string localFilePath = Path.Combine(RimPhoneCore.GalleryPath, safeFileName);
                                    cleanContent += $"\n<RIMPHONE_LOCAL_IMG:{localFilePath}>";
                                }

                                if (dropEntireMessage)
                                {
                                    RimPhoneEngine.EnqueueMainThreadAction(() => {
                                        Verse.Messages.Message("RTRS_Msg_InterceptedMedia".Translate("Discord", interceptedExt), RimWorld.MessageTypeDefOf.RejectInput, false);
                                    });
                                    continue;
                                }

                                var userMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""username\""\s*:\s*\""([^\""]+)\""", RegexOptions.Singleline);
                                string username = userMatch.Success ? DecodeJsonString(userMatch.Groups[1].Value).TrimEnd('.', ',', ' ', '!') : "Unknown";

                                var idMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""id\""\s*:\s*\""(\d+)\""", RegexOptions.Singleline);
                                string senderId = idMatch.Success ? idMatch.Groups[1].Value : "";

                                var avatarMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""avatar\""\s*:\s*\""([a-zA-Z0-9_]+)\""", RegexOptions.Singleline);
                                string senderAvatarUrl = "";
                                if (idMatch.Success && avatarMatch.Success && avatarMatch.Groups[1].Value != "null")
                                {
                                    senderAvatarUrl = $"https://cdn.discordapp.com/avatars/{senderId}/{avatarMatch.Groups[1].Value}.png";
                                }

                                string trimmedCmd = cleanContent.Trim();

                                // =====================================================================
                                // PHASE 1: Robust Regex Command Interceptor
                                // Matches any prefix (/, \, ., ,, 。, ，, ~, -) optionally followed by space,
                                // then the command keyword, and normalizes it to a strict slash command.
                                // =====================================================================
                                bool isSystemCommand = false;
                                string standardizedCmd = trimmedCmd;

                                var cmdMatch = Regex.Match(trimmedCmd, @"^[/\\.,。，~-]\s*(login|logout|人设|persona|移除人设|clearpersona)(?:\s+(.*))?$", RegexOptions.IgnoreCase);
                                if (cmdMatch.Success)
                                {
                                    isSystemCommand = true;
                                    string commandWord = cmdMatch.Groups[1].Value.ToLower();
                                    string args = cmdMatch.Groups[2].Success ? cmdMatch.Groups[2].Value : "";

                                    // Normalize the command format ensuring proper spacing
                                    if (commandWord == "人设" || commandWord == "persona" || commandWord == "login")
                                    {
                                        standardizedCmd = $"/{commandWord} {args}".Trim();
                                    }
                                    else
                                    {
                                        standardizedCmd = $"/{commandWord}";
                                    }
                                }

                                if (isSystemCommand)
                                {
                                    newMessages.Add(new DiscordMessage
                                    {
                                        Id = msgId,
                                        SenderName = username,
                                        SenderId = senderId,
                                        SenderAvatarUrl = senderAvatarUrl,
                                        TargetPawn = "System",
                                        Content = standardizedCmd,
                                        Timestamp = DateTime.Now,
                                        SourcePlatform = "Discord",
                                    });
                                    continue;
                                }

                                string targetPawn = "All";
                                RimPhoneEngine.EnqueueMainThreadAction(() => {
                                    foreach (var pawn in RimWorld.PawnsFinder.AllMaps_FreeColonists)
                                    {
                                        string shortName = pawn.Name.ToStringShort;

                                        // Standard @ mention logic
                                        if (cleanContent.Contains("@" + shortName))
                                        {
                                            targetPawn = shortName;
                                            cleanContent = cleanContent.Replace("@" + shortName, "").Trim();
                                            break;
                                        }
                                        else
                                        {
                                            // =====================================================================
                                            // NEW: Lazy Prefix Mention Interceptor
                                            // Converts ".Kena hello" or "~ Kena" into a standard targeted message.
                                            // =====================================================================
                                            string[] prefixes = new[] { "/", "\\", ".", ",", "。", "，", "~", "-" };
                                            foreach (string p in prefixes)
                                            {
                                                if (cleanContent.StartsWith(p + shortName) || cleanContent.StartsWith(p + " " + shortName))
                                                {
                                                    targetPawn = shortName;
                                                    // Remove the prefix and the colonist's name from the actual message payload
                                                    int nameIndex = cleanContent.IndexOf(shortName);
                                                    cleanContent = cleanContent.Substring(nameIndex + shortName.Length).Trim();
                                                    break;
                                                }
                                            }
                                            if (targetPawn != "All") break;
                                        }
                                    }
                                });
                                System.Threading.Thread.Sleep(10);

                                if (targetPawn == "All") continue;

                                var timeMatch = Regex.Match(chunk, @"\""timestamp\""\s*:\s*\""([^\""]+)\""");
                                DateTime timestamp = DateTime.Now;
                                if (timeMatch.Success && DateTime.TryParse(timeMatch.Groups[1].Value, out DateTime parsedTime))
                                    timestamp = parsedTime.ToLocalTime();

                                newMessages.Add(new DiscordMessage
                                {
                                    Id = msgId,
                                    SenderName = username,
                                    SenderId = senderId,
                                    SenderAvatarUrl = senderAvatarUrl,
                                    TargetPawn = targetPawn,
                                    Content = cleanContent,
                                    Timestamp = timestamp,
                                    SourcePlatform = "Discord",
                                });
                            }

                            RimPhoneEngine.EnqueueMainThreadAction(() =>
                            {
                                if (newMessages.Count > 0)
                                {
                                    newMessages.Reverse();
                                    RimPhoneCommandProcessor.ProcessSystemCommands(newMessages, settings);
                                }

                                if (maxFetchedId > currentSavedId)
                                {
                                    settings.LastDiscordMessageId = maxFetchedId.ToString();
                                    settings.Write();
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (settings.DebugMode)
                        RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone] Background Task Error: {ex.Message}"));
                }
                finally
                {
                    RimPhoneEngine.EnqueueMainThreadAction(() => { IsFetching = false; });
                }
            });
        }

        private static string DecodeJsonString(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            try
            {
                string safeRaw = raw.Replace("\\/", "/");
                return Regex.Unescape(safeRaw);
            }
            catch
            {
                string fallback = raw.Replace("\\\"", "\"").Replace("\\n", "\n").Replace("\\\\", "\\");
                return Regex.Replace(fallback, @"\\u([0-9A-Fa-f]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
            }
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
                    if (c == '{')
                    {
                        if (depth == 0) start = i;
                        depth++;
                    }
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0 && start != -1)
                        {
                            objs.Add(arrayJson.Substring(start, i - start + 1));
                            start = -1;
                        }
                    }
                }
            }
            return objs;
        }
    }
}