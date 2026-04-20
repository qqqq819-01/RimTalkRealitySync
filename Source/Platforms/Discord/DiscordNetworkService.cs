using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalkRealitySync.Sync;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace RimTalkRealitySync.Platforms.Discord
{
    public class DiscordMessage
    {
        public string Id;
        public string SenderName;
        public string SenderId;
        public string SenderAvatarUrl;

        public string TargetPawn;
        public string Content;
        public DateTime Timestamp;

        // =====================================================================
        // NEW: Omni-Platform Tracker
        // =====================================================================
        public string SourcePlatform = "Unknown";

        public bool IsSelected = false;
        public bool IsExpanded = false;
        public float AnimProgress = 0f;
        public float LastHoverTime = 0f;
    }

    public static class DiscordNetworkService
    {
        public static List<DiscordMessage> Messages = new List<DiscordMessage>();
        public static bool AutoSendEnabled = false;

        public static bool IsFileReady(string path)
        {
            try
            {
                using (FileStream inputStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return inputStream.Length > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void ProcessAndSendImage(string imagePath, Pawn targetPawn, string cleanMessage, string discordSenderName = null)
        {
            try
            {
                byte[] rawBytes = File.ReadAllBytes(imagePath);
                Texture2D originalTex = new Texture2D(2, 2);
                originalTex.LoadImage(rawBytes);

                int maxDim = 800;
                int newWidth = originalTex.width;
                int newHeight = originalTex.height;

                if (newWidth > maxDim || newHeight > maxDim)
                {
                    float ratio = Mathf.Min((float)maxDim / newWidth, (float)maxDim / newHeight);
                    newWidth = Mathf.RoundToInt(newWidth * ratio);
                    newHeight = Mathf.RoundToInt(newHeight * ratio);
                }

                RenderTexture rt = RenderTexture.GetTemporary(newWidth, newHeight);
                RenderTexture.active = rt;
                Graphics.Blit(originalTex, rt);

                Texture2D resizedTex = new Texture2D(newWidth, newHeight, TextureFormat.RGB24, false);
                resizedTex.ReadPixels(new Rect(0, 0, newWidth, newHeight), 0, 0);
                resizedTex.Apply();

                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);

                byte[] compressedBytes = resizedTex.EncodeToJPG(50);
                string base64String = Convert.ToBase64String(compressedBytes);

                UnityEngine.Object.Destroy(originalTex);
                UnityEngine.Object.Destroy(resizedTex);

                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        using (var client = new System.Net.WebClient())
                        {
                            var reqParm = new System.Collections.Specialized.NameValueCollection();
                            reqParm.Add("key", "6d207e02198a847aa98d0a2a901485a5");
                            reqParm.Add("action", "upload");
                            reqParm.Add("source", base64String);
                            reqParm.Add("format", "json");

                            byte[] responseBytes = client.UploadValues("https://freeimage.host/api/1/upload", "POST", reqParm);
                            string responseString = System.Text.Encoding.UTF8.GetString(responseBytes);

                            var match = System.Text.RegularExpressions.Regex.Match(responseString, @"\""url\""\s*:\s*\""(http[^\s\""]+)\""");
                            if (match.Success)
                            {
                                string imageUrl = match.Groups[1].Value.Replace("\\/", "/");
                                string imageTag = $"<RIMPHONE_URL:{imageUrl}>";

                                string finalPrompt = string.IsNullOrWhiteSpace(cleanMessage) ? imageTag : $"{cleanMessage}\n{imageTag}";

                                // =========================================================================
                                // PURE ENGINE (Image Transmission)
                                // Perfectly mimics the text pipeline to guarantee consistency.
                                // =========================================================================
                                RimPhoneEngine.EnqueueMainThreadAction(() =>
                                {
                                    var pawnState = RimTalk.Data.Cache.Get(targetPawn);
                                    if (pawnState == null) return;

                                    string attachedImgTag = "RTRS_Tag_AttachedImage".Translate();
                                    string sentImgText = "RTRS_Tag_SentAnImage".Translate();

                                    string uiText = string.IsNullOrWhiteSpace(cleanMessage)
                                        ? $"{sentImgText} {attachedImgTag}"
                                        : $"{cleanMessage} {attachedImgTag}";

                                    string senderNameToUse = string.IsNullOrEmpty(discordSenderName) ? "Player" : discordSenderName;

                                    // Secure Main-Thread logging
                                    RimTalk.Data.ApiLog apiLog = RimTalk.Data.ApiHistory.AddUserHistory(targetPawn, targetPawn, uiText);
                                    apiLog.Name = senderNameToUse;
                                    apiLog.SpokenTick = GenTicks.TicksGame;
                                    RimTalk.UI.Overlay.NotifyLogUpdated();

                                    List<Pawn> nearbyPawns = RimTalk.Service.PawnSelector.GetAllNearByPawns(targetPawn);
                                    var pawns = new List<Pawn> { targetPawn }
                                        .Concat(nearbyPawns.Where(p => {
                                            var ps = RimTalk.Data.Cache.Get(p);
                                            return ps != null && ps.CanDisplayTalk() && ps.TalkResponses.Empty();
                                        }))
                                        .Distinct()
                                        .Take(RimTalk.Settings.Get().Context.MaxPawnContextCount)
                                        .ToList();

                                    // =====================================================================
                                    // FIXED: Synchronize Player Entity for Multi-modal (Image) requests.
                                    // =====================================================================
                                    var playerEntity = RimTalk.Data.Cache.GetPlayer();
                                    bool isPlayer = !string.IsNullOrEmpty(RimTalkRealitySyncMod.Settings.LinkedDiscordUsername) &&
                                                    discordSenderName != null &&
                                                    RimTalkRealitySyncMod.Settings.LinkedDiscordUsername == discordSenderName;

                                    // Use Player Entity if linked, otherwise null (Observer)
                                    Pawn initiator = isPlayer ? playerEntity : null;
                                    TalkType talkType = isPlayer ? TalkType.User : TalkType.Other;

                                    TalkRequest req = new TalkRequest(finalPrompt, targetPawn, initiator, talkType);
                                    req.IsMonologue = pawns.Count == 1;

                                    req.PromptMessages = RimTalk.Prompt.PromptManager.Instance.BuildMessages(req, pawns, pawnState.LastStatus);

                                    // =====================================================================
                                    // FIXED: Precise Image Directive Injection
                                    // Instead of overwriting the entire User block (which destroys JSON format rules),
                                    // we locate the exact rendered {{ rs_ImageTag }} (e.g., <RIMPHONE_URL:...>) 
                                    // and replace it with the full directive containing the image.
                                    // =====================================================================
                                    if (req.PromptMessages != null && req.PromptMessages.Count > 0)
                                    {
                                        int lastUserIdx = req.PromptMessages.FindLastIndex(m => m.role == Role.User);
                                        if (lastUserIdx >= 0)
                                        {
                                            string content = req.PromptMessages[lastUserIdx].content;

                                            // =====================================================================
                                            // FIXED: Bypass RimWorld Translate() Tag Stripping
                                            // Translate() violently removes unknown tags like <RIMPHONE_URL...>. 
                                            // We pass a safe placeholder first, then inject the raw payload afterwards!
                                            // =====================================================================
                                            string powerfulPrompt = "RTRS_Prompt_ImageDirective".Translate(senderNameToUse, "[IMAGE_PAYLOAD]").ToString();
                                            powerfulPrompt = powerfulPrompt.Replace("[IMAGE_PAYLOAD]", finalPrompt);

                                            if (content.Contains(imageTag))
                                            {
                                                // Safely replace the tag with the full directive payload
                                                content = content.Replace(imageTag, powerfulPrompt);
                                            }
                                            else
                                            {
                                                // Fallback: If the user didn't use {{ rs_ImageTag }} in their template,
                                                // prepend it to avoid destroying the JSON output rules at the bottom.
                                                content = powerfulPrompt + "\n\n" + content;
                                            }

                                            req.PromptMessages[lastUserIdx] = (Role.User, content);
                                        }
                                    }

                                    var extracted = RimTalk.Prompt.PromptManager.ExtractUserPrompt(req.PromptMessages);
                                    if (!string.IsNullOrEmpty(extracted)) req.Prompt = extracted;

                                    pawnState.IsGeneratingTalk = true;

                                    Task.Run(async () => {
                                        // Wait for TPS to flow (Mimic Temporal Sync for images too!)
                                        bool isPaused = true;
                                        while (isPaused)
                                        {
                                            RimPhoneEngine.EnqueueMainThreadAction(() => {
                                                isPaused = Find.TickManager == null || Find.TickManager.Paused;
                                            });
                                            if (isPaused) await Task.Delay(100);
                                        }

                                        // =================================================================
                                        // ASYNC RESTORED: Image AI network calls run safely in the background!
                                        // =================================================================
                                        try
                                        {
                                            var receivedResponses = new List<RimTalk.Data.TalkResponse>();

                                            await RimTalk.Service.AIService.ChatStreaming(req, response => {
                                                RimPhoneEngine.EnqueueMainThreadAction(() => {
                                                    var ps = RimTalk.Data.Cache.GetByName(response.Name);
                                                    if (ps != null)
                                                    {
                                                        response.Name = ps.Pawn.LabelShort;
                                                        if (receivedResponses.Any()) response.ParentTalkId = receivedResponses.Last().Id;
                                                        receivedResponses.Add(response);
                                                        ps.TalkResponses.Add(response);
                                                    }
                                                });
                                            });

                                            RimPhoneEngine.EnqueueMainThreadAction(() => {
                                                if (receivedResponses.Any())
                                                {
                                                    string serialized = RimTalk.Util.JsonUtil.SerializeToJson(receivedResponses);
                                                    var uniquePawns = receivedResponses
                                                        .Select(r => RimTalk.Data.Cache.GetByName(r.Name)?.Pawn)
                                                        .Where(p => p != null)
                                                        .Distinct();
                                                    foreach (var p in uniquePawns)
                                                    {
                                                        RimTalk.Data.TalkHistory.AddMessageHistory(p, finalPrompt, serialized);
                                                    }
                                                }
                                                pawnState.IsGeneratingTalk = false;
                                            });
                                        }
                                        catch (Exception ex)
                                        {
                                            RimTalk.Util.Logger.Error($"[RimPhone] Image Chat Channel Error: {ex.Message}\n{ex.StackTrace}");
                                            RimPhoneEngine.EnqueueMainThreadAction(() => pawnState.IsGeneratingTalk = false);
                                        }
                                    });

                                    Verse.Messages.Message("RTRS_Msg_CloudSyncComplete".Translate(targetPawn.Name.ToStringShort), MessageTypeDefOf.PositiveEvent, false);
                                });
                            }
                            else
                            {
                                RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error("[RimPhone] Cloud upload returned unexpected JSON: " + responseString));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        RimPhoneEngine.EnqueueMainThreadAction(() => Log.Error($"[RimPhone] Cloud upload failed: {ex.Message}"));
                    }
                });
            }
            catch (Exception ex)
            {
                Log.Error($"[RimPhone] Critical failure during image processing: {ex.Message}");
                RimPhoneEngine.EnqueueMainThreadAction(() => Verse.Messages.Message("RTRS_Msg_ProcessImageFailed".Translate(), MessageTypeDefOf.RejectInput, false));
            }
        }
    }
}