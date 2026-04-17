using RimTalkRealitySync.Platforms.Discord;
using RimTalkRealitySync.Sync;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Verse;

namespace RimTalkRealitySync.Platforms.Kook
{
    /// <summary>
    /// Handles fetching and parsing messages exclusively from the KOOK (Kaiheila) API.
    /// Perfectly mirrors the DiscordFetcher logic, ensuring seamless multi-platform support.
    /// </summary>
    public static class KookFetcher
    {
        public static bool IsFetching = false;

        public static void SyncKookMessagesAsync(bool isManual = false)
        {
            if (IsFetching) return;

            var settings = RimTalkRealitySyncMod.Settings;

            if (string.IsNullOrWhiteSpace(settings.KookBotToken) || string.IsNullOrWhiteSpace(settings.KookChannelId))
            {
                if (isManual) RimPhoneEngine.EnqueueMainThreadAction(() => Verse.Messages.Message("RTRS_Msg_MissingKookAuth".Translate(), RimWorld.MessageTypeDefOf.RejectInput, false));
                return;
            }

            IsFetching = true;

            Task.Run(() =>
            {
                try
                {
                    string cleanChannelId = settings.KookChannelId.Trim();
                    string cleanToken = settings.KookBotToken.Trim();

                    // =====================================================================
                    // FIXED: KOOK CDN Cache Busting
                    // Added a dynamic Unix timestamp parameter to force the KOOK API 
                    // to return fresh data immediately instead of serving stale cache.
                    // =====================================================================
                    string url = $"https://www.kookapp.cn/api/v3/message/list?target_id={cleanChannelId}&page_size=20&_t={DateTimeOffset.Now.ToUnixTimeMilliseconds()}";

                    string jsonResponse = null;

                    try
                    {
                        using (WebClient client = new WebClient())
                        {
                            client.Encoding = Encoding.UTF8;
                            client.Headers.Add("Authorization", $"Bot {cleanToken}");
                            // =====================================================================
                            // FIXED: Ultimate Cache Buster (HTTP Headers)
                            // =====================================================================
                            client.Headers.Add("Cache-Control", "no-cache");
                            client.Headers.Add("Pragma", "no-cache");
                            jsonResponse = client.DownloadString(url);
                        }
                    }
                    catch (WebException wex)
                    {
                        // FORCE ERROR LOGGING: Catch API issues (e.g., 401 Unauthorized, 403 Forbidden)
                        RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone KOOK] API Fetch Failed: {wex.Message}"));
                        return;
                    }

                    if (!string.IsNullOrEmpty(jsonResponse))
                    {
                        // Basic validation that the API call was successful
                        if (!jsonResponse.Contains("\"code\":0") && !jsonResponse.Contains("\"code\": 0"))
                        {
                            RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone KOOK] API returned non-zero code. JSON: {jsonResponse}"));
                            return;
                        }

                        // =====================================================================
                        // FIXED: Regex Truncation Bypass
                        // KOOK JSON contains nested "item_part":[]}, which prematurely closed 
                        // the previous Regex. We now manually locate the array start and let 
                        // the ExtractJsonObjects parser handle the nested brackets safely.
                        // =====================================================================
                        int itemsIdx = jsonResponse.IndexOf("\"items\":");
                        if (itemsIdx != -1)
                        {
                            int arrayStart = jsonResponse.IndexOf('[', itemsIdx);
                            if (arrayStart != -1)
                            {
                                string arrayJson = jsonResponse.Substring(arrayStart);
                                List<string> chunks = ExtractJsonObjects(arrayJson);

                                if (chunks.Count > 0)
                                {
                                    List<DiscordMessage> newMessages = new List<DiscordMessage>();

                                    string newestMsgIdToSave = "";
                                    List<string> newChunks = new List<string>();
                                    bool foundAnchor = false;

                                    // =====================================================================
                                    // FIXED: The KOOK Array Reversal Bug
                                    // Unlike Discord (which returns Newest->Oldest), KOOK returns Oldest->Newest.
                                    // We MUST iterate backwards to find the newest message first, then 
                                    // stop when we hit the anchor.
                                    // =====================================================================
                                    for (int i = chunks.Count - 1; i >= 0; i--)
                                    {
                                        string chunk = chunks[i];
                                        var idMatch = Regex.Match(chunk, @"\""id\""\s*:\s*\""([^\""]+)\""");
                                        if (!idMatch.Success) continue;

                                        string msgId = idMatch.Groups[1].Value;

                                        if (string.IsNullOrEmpty(newestMsgIdToSave)) newestMsgIdToSave = msgId;

                                        if (!string.IsNullOrWhiteSpace(settings.LastKookMessageId) && msgId == settings.LastKookMessageId)
                                        {
                                            foundAnchor = true;
                                            break;
                                        }
                                        newChunks.Add(chunk);
                                    }

                                    // =====================================================================
                                    // FIXED: KOOK API Cache Time-Travel Protection
                                    // If the saved anchor was not found, it means the API either returned 
                                    // heavily outdated cached data or we missed 20+ messages.
                                    // We clear the chunks to prevent infinite looping of old messages,
                                    // and only update the anchor to the newest ID.
                                    // =====================================================================
                                    if (!string.IsNullOrWhiteSpace(settings.LastKookMessageId) && !foundAnchor)
                                    {
                                        newChunks.Clear();
                                        if (settings.DebugMode)
                                            RimPhoneEngine.EnqueueMainThreadAction(() => Log.Warning("[RimPhone KOOK] Anchor missing due to API cache. Resetting sync marker to prevent loop."));
                                    }

                                    // 2. Reverse array to process from oldest to newest
                                    newChunks.Reverse();

                                foreach (string chunk in newChunks)
                                {
                                    var idMatch = Regex.Match(chunk, @"\""id\""\s*:\s*\""([^\""]+)\""");
                                    string msgId = idMatch.Groups[1].Value;

                                    var contentMatch = Regex.Match(chunk, @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*)\""");
                                    string rawContent = contentMatch.Success ? contentMatch.Groups[1].Value : "";
                                    string cleanContent = DecodeJsonString(rawContent);

                                    // Ignore system messages or bots
                                    if (chunk.Contains("\"bot\":true") || chunk.Contains("\"bot\": true")) continue;

                                    var userMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""username\""\s*:\s*\""([^\""]+)\""", RegexOptions.Singleline);
                                    string username = userMatch.Success ? DecodeJsonString(userMatch.Groups[1].Value).TrimEnd('.', ',', ' ', '!') : "Unknown";

                                    var userIdMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""id\""\s*:\s*\""([^\""]+)\""", RegexOptions.Singleline);
                                    string senderId = userIdMatch.Success ? userIdMatch.Groups[1].Value : "";

                                    var avatarMatch = Regex.Match(chunk, @"\""author\""\s*:\s*\{.*?\""avatar\""\s*:\s*\""([^\""]+)\""", RegexOptions.Singleline);
                                    string senderAvatarUrl = avatarMatch.Success ? avatarMatch.Groups[1].Value.Replace("\\/", "/") : "";

                                    // Extract Timestamp (KOOK uses unix milliseconds 'create_at')
                                    DateTime timestamp = DateTime.Now;
                                    var timeMatch = Regex.Match(chunk, @"\""create_at\""\s*:\s*(\d+)");
                                    if (timeMatch.Success && long.TryParse(timeMatch.Groups[1].Value, out long unixMs))
                                    {
                                        timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;
                                    }

                                    // =====================================================================
                                    // KOOK MEDIA & QUOTE PARSER (Phase 3: Deep Extraction)
                                    // Operates on unescaped cleanContent to easily bypass Type 10 Card wrappers.
                                    // =====================================================================
                                    int msgType = 9; // Default to Text/KMarkdown
                                    var typeMatch = Regex.Match(chunk, @"\""type\""\s*:\s*(\d+)");
                                    if (typeMatch.Success) int.TryParse(typeMatch.Groups[1].Value, out msgType);

                                    string imageUrl = "";

                                    if (msgType == 2) // Direct Image
                                    {
                                        imageUrl = cleanContent;
                                        cleanContent = ""; // Clear URL from text payload
                                    }
                                    else if (msgType == 10) // Card Image
                                    {
                                        // FIXED: Execute Regex on cleanContent to avoid nested \/ escaping chaos
                                        var srcMatch = Regex.Match(cleanContent, @"\""src\""\s*:\s*\""(https?://[^\""]+)\""");
                                        if (srcMatch.Success) imageUrl = srcMatch.Groups[1].Value;
                                        cleanContent = "";
                                    }
                                    else if (msgType == 9 && (chunk.Contains("\"has_quote\":true") || chunk.Contains("\"has_quote\": true")))
                                    {
                                        // Deep extract the quoted payload and decode it first
                                        var quoteContentMatch = Regex.Match(chunk, @"\""quote\""\s*:\s*\{.*?\""content\""\s*:\s*\""((?:\\.|[^\""\\])*)\""", RegexOptions.Singleline);
                                        if (quoteContentMatch.Success)
                                        {
                                            string quoteRaw = quoteContentMatch.Groups[1].Value;
                                            string quoteClean = DecodeJsonString(quoteRaw);
                                            
                                            var quoteTypeMatch = Regex.Match(chunk, @"\""quote\""\s*:\s*\{.*?\""type\""\s*:\s*(\d+)");
                                            int qType = 0;
                                            if (quoteTypeMatch.Success) int.TryParse(quoteTypeMatch.Groups[1].Value, out qType);

                                            if (qType == 2) 
                                            {
                                                imageUrl = quoteClean; // Plain URL
                                            }
                                            else if (qType == 10)
                                            {
                                                // Extract from decoded Card
                                                var srcMatch = Regex.Match(quoteClean, @"\""src\""\s*:\s*\""(https?://[^\""]+)\""");
                                                if (srcMatch.Success) imageUrl = srcMatch.Groups[1].Value;
                                            }
                                        }
                                    }

                                    // =====================================================================
                                    // FIXED: Dynamic Media Interception
                                    // Scans the file extension and safely drops unsupported formats 
                                    // (like GIF or MP4) to prevent gallery loading crashes.
                                    // =====================================================================
                                    bool dropEntireMessage = false;
                                    string interceptedExt = "";

                                    // Process Extracted Image
                                    if (!string.IsNullOrEmpty(imageUrl))
                                    {
                                        string safeFileName = Regex.Replace(Path.GetFileName(new Uri(imageUrl).LocalPath), @"[^a-zA-Z0-9\.\-_]", "");
                                        string ext = Path.GetExtension(safeFileName).ToLower();

                                        if (ext == ".gif" || ext == ".mp4" || ext == ".webm" || ext == ".webp")
                                        {
                                            dropEntireMessage = true;
                                            interceptedExt = ext.ToUpper();
                                        }
                                        else
                                        {
                                            RimPhoneCore.DownloadImageSafely(imageUrl, safeFileName, settings.DebugMode);
                                            string localFilePath = Path.Combine(RimPhoneCore.GalleryPath, safeFileName);
                                            string imgTag = $"<RIMPHONE_LOCAL_IMG:{localFilePath}>";
                                            cleanContent = string.IsNullOrWhiteSpace(cleanContent) ? imgTag : $"{cleanContent}\n{imgTag}";
                                        }
                                    }

                                    if (dropEntireMessage)
                                    {
                                            RimPhoneEngine.EnqueueMainThreadAction(() => {
                                                Verse.Messages.Message("RTRS_Msg_InterceptedMedia".Translate("KOOK", interceptedExt), RimWorld.MessageTypeDefOf.RejectInput, false);
                                            });
                                            continue;
                                    }

                                    string trimmedCmd = cleanContent.Trim();

                                    // =====================================================================
                                    // SYSTEM COMMAND INTERCEPTOR
                                    // =====================================================================
                                    bool isSystemCommand = false;
                                    string standardizedCmd = trimmedCmd;

                                    var cmdMatch = Regex.Match(trimmedCmd, @"^[/\\.,。，~-]\s*(login|logout|人设|persona|移除人设|clearpersona)(?:\s+(.*))?$", RegexOptions.IgnoreCase);
                                    if (cmdMatch.Success)
                                    {
                                        isSystemCommand = true;
                                        string commandWord = cmdMatch.Groups[1].Value.ToLower();
                                        string args = cmdMatch.Groups[2].Success ? cmdMatch.Groups[2].Value : "";

                                        if (commandWord == "人设" || commandWord == "persona" || commandWord == "login")
                                            standardizedCmd = $"/{commandWord} {args}".Trim();
                                        else
                                            standardizedCmd = $"/{commandWord}";
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
                                            Timestamp = timestamp,
                                            SourcePlatform = "KOOK",
                                        });
                                        continue;
                                    }

                                        // =====================================================================
                                        // PAWN TARGET ROUTING
                                        // =====================================================================
                                        string targetPawn = "All";
                                        bool isPawnSearchDone = false; // NEW: Thread sync flag
                                        RimPhoneEngine.EnqueueMainThreadAction(() => {
                                            try
                                            {
                                                foreach (var pawn in RimWorld.PawnsFinder.AllMaps_FreeColonists)
                                                {
                                                    string shortName = pawn.Name.ToStringShort;

                                                    // KOOK mention format typically looks like (met)ID(met), but we also support raw @Name
                                                    if (cleanContent.Contains("@" + shortName) || cleanContent.Contains($"(met){shortName}(met)"))
                                                    {
                                                        targetPawn = shortName;
                                                        cleanContent = cleanContent.Replace("@" + shortName, "").Replace($"(met){shortName}(met)", "").Trim();
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
                                                                int nameIndex = cleanContent.IndexOf(shortName);
                                                                cleanContent = cleanContent.Substring(nameIndex + shortName.Length).Trim();
                                                                break;
                                                            }
                                                        }
                                                        if (targetPawn != "All") break;
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                // Guarantee the flag is set even if an error occurs
                                                isPawnSearchDone = true;
                                            }
                                        });

                                        // =====================================================================
                                        // FIXED: Absolute Thread Sync
                                        // Replaced Thread.Sleep(10) with a precise wait loop. The background 
                                        // thread now safely waits EXACTLY until the Main Thread finishes.
                                        // =====================================================================
                                        while (!isPawnSearchDone)
                                        {
                                            System.Threading.Thread.Sleep(5);
                                        }

                                        // DISCARD LOGIC: If it's a standalone image with no @ target, it drops here safely.
                                        if (targetPawn == "All") continue;

                                    newMessages.Add(new DiscordMessage
                                    {
                                        Id = msgId,
                                        SenderName = username,
                                        SenderId = senderId,
                                        SenderAvatarUrl = senderAvatarUrl,
                                        TargetPawn = targetPawn,
                                        Content = cleanContent,
                                        Timestamp = timestamp,
                                        SourcePlatform = "KOOK",
                                    });
                                }

                                RimPhoneEngine.EnqueueMainThreadAction(() =>
                                {
                                    if (newMessages.Count > 0)
                                    {
                                        RimPhoneCommandProcessor.ProcessSystemCommands(newMessages, settings);
                                    }

                                    // Update the sync marker if we found new messages
                                    if (!string.IsNullOrEmpty(newestMsgIdToSave) && newestMsgIdToSave != settings.LastKookMessageId)
                                    {
                                        settings.LastKookMessageId = newestMsgIdToSave;
                                        settings.Write();
                                    }
                                });
                            }
                        }
                    }
                }
                }
                catch (Exception ex)
                {
                    if (settings.DebugMode)
                        RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone KOOK] Background Task Error: {ex.Message}"));
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