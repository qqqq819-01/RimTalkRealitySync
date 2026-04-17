using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalkRealitySync.Platforms.Discord;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verse;

namespace RimTalkRealitySync.Sync
{
    /// <summary>
    /// The Central Brain for injecting external messages into the RimTalk AI Engine.
    /// Used universally by both Auto-Send Background tasks and Manual UI interactions.
    /// Guarantees exact same behavior and prevents code duplication.
    /// </summary>
    public static class RimPhoneChatProcessor
    {
        public static void InjectMessageIntoRimTalk(Pawn targetPawn, DiscordMessage msg)
        {
            if (targetPawn == null || RimTalk.Data.Cache.GetPlayer() == null)
            {
                // TRANSLATION HOOK
                Verse.Messages.Message("RTRS_Msg_CriticalErrorNoColonist".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }

            var pawnState = RimTalk.Data.Cache.Get(targetPawn);
            if (pawnState == null) return;

            string cleanText = msg.Content.Trim();

            // 1. Image Hand-off
            var match = System.Text.RegularExpressions.Regex.Match(cleanText, @"<RIMPHONE_LOCAL_IMG:([^>]+)>");
            if (match.Success)
            {
                string imgPath = match.Groups[1].Value;
                cleanText = cleanText.Replace(match.Value, "").Trim();

                if (!System.IO.File.Exists(imgPath) || !DiscordNetworkService.IsFileReady(imgPath))
                {
                    Verse.Messages.Message($"Attachment for {targetPawn.Name.ToStringShort} is still downloading. Try again in seconds.", MessageTypeDefOf.RejectInput, false);
                    return;
                }

                DiscordNetworkService.ProcessAndSendImage(imgPath, targetPawn, cleanText, msg.SenderName);
                return;
            }

            // =====================================================================
            // NEW: Phase 1 - Direct Cross-Platform Routing
            // Instantly relay messages from one platform to the others BEFORE game injection.
            // This perfectly solves the "Observer on KOOK invisible to Discord" issue.
            // =====================================================================
            bool isPlayer = !string.IsNullOrEmpty(RimTalkRealitySyncMod.Settings.LinkedDiscordUserId) &&
                            msg.SenderId == RimTalkRealitySyncMod.Settings.LinkedDiscordUserId;

            // =====================================================================
            // FIXED: Optimized Body Tagging (Anti-Redundancy)
            // Header: [Platform Role] Name
            // Body: -> Target Content
            // =====================================================================
            string platformName = string.IsNullOrEmpty(msg.SourcePlatform) ? "未知" : msg.SourcePlatform;
            string displayTag = isPlayer ? $"[{platformName} 玩家] {msg.SenderName}" : $"[{platformName} 观测者] {msg.SenderName}";

            // Inject direction into the message body itself
            string routedText = $"-> {targetPawn.LabelShort} {cleanText}";

            var settings = RimTalkRealitySyncMod.Settings;

            if (msg.SourcePlatform == "Discord" && settings.BroadcastToKook)
                Platforms.Kook.KookBroadcastService.BroadcastToKook(displayTag, routedText);
            else if (msg.SourcePlatform == "KOOK" && settings.BroadcastToDiscord)
                Platforms.Discord.DiscordBroadcastService.BroadcastToDiscord(displayTag, routedText);

            // =====================================================================
            // FIXED: Ultimate Player Takeover with SILENCER
            // =====================================================================
            if (isPlayer)
            {
                var playerEntity = RimTalk.Data.Cache.GetPlayer();
                if (playerEntity != null)
                {
                    // Activate Silencer to prevent RimPhoneBroadcastPatch from echoing this back
                    Patches.RimPhoneBroadcastPatch.SuppressInputBroadcast = true;
                    try
                    {
                        RimTalk.Service.CustomDialogueService.ExecuteDialogue(playerEntity, targetPawn, cleanText);
                    }
                    finally
                    {
                        // Always deactivate silencer even if errors occur
                        Patches.RimPhoneBroadcastPatch.SuppressInputBroadcast = false;
                    }
                    return; // Kill the method here to avoid duplicates
                }
            }

            // 2. Safe Main-Thread logging (For Observers)
            RimPhoneEngine.EnqueueMainThreadAction(() => {
                ApiLog apiLog = ApiHistory.AddUserHistory(targetPawn, targetPawn, cleanText);
                apiLog.Name = msg.SenderName;
                apiLog.SpokenTick = GenTicks.TicksGame;
                RimTalk.UI.Overlay.NotifyLogUpdated();
            });

            // 3. Build local pawn context legally
            List<Pawn> nearbyPawns = PawnSelector.GetAllNearByPawns(targetPawn);
            var pawns = new List<Pawn> { targetPawn }
                .Concat(nearbyPawns.Where(p => {
                    var ps = RimTalk.Data.Cache.Get(p);
                    return ps != null && ps.CanDisplayTalk() && ps.TalkResponses.Empty();
                }))
                .Distinct()
                .Take(RimTalk.Settings.Get().Context.MaxPawnContextCount)
                .ToList();

            // =====================================================================
            // FIXED: Pre-Injection of Hidden Beacon (Phase 2.5 CS0103 Fix)
            // Kept the variable name "formattedPrompt" because it is required by 
            // TalkHistory.AddMessageHistory at the bottom of the file.
            // All legacy Surgical Override string manipulation has been purged.
            // =====================================================================
            string formattedPrompt = $"[高维通讯] '{msg.SenderName}' 传来了话语：\n\"{cleanText}\"";

            // 4. Create the request with the fully formatted text immediately
            TalkRequest req = new TalkRequest(formattedPrompt, targetPawn, null, TalkType.Other);
            req.IsMonologue = pawns.Count == 1;

            // Engine builds messages. Scriban variables will now natively catch the beacon!
            req.PromptMessages = RimTalk.Prompt.PromptManager.Instance.BuildMessages(req, pawns, pawnState.LastStatus);

            var extracted = RimTalk.Prompt.PromptManager.ExtractUserPrompt(req.PromptMessages);
            if (!string.IsNullOrEmpty(extracted)) req.Prompt = extracted;

            pawnState.IsGeneratingTalk = true;

            // 6. Offload Temporal Sync to background, but execute AI on Main Thread
            Task.Run(async () => {
                // Wait for TPS to flow on background thread so we don't block the game
                bool isPaused = true;
                while (isPaused)
                {
                    RimPhoneEngine.EnqueueMainThreadAction(() => {
                        isPaused = Find.TickManager == null || Find.TickManager.Paused;
                    });
                    if (isPaused) await Task.Delay(100);
                }

                // =================================================================
                // ASYNC RESTORED: AI network calls run safely in the background!
                // Thanks to our AntiCrashMessagePatch, we no longer fear OnGUI crashes.
                // Only pawn state UI updates are sent to the Main Thread.
                // =================================================================
                try
                {
                    var receivedResponses = new List<TalkResponse>();

                    await AIService.ChatStreaming(req, response => {
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
                                TalkHistory.AddMessageHistory(p, formattedPrompt, serialized);
                            }
                        }
                        pawnState.IsGeneratingTalk = false;
                    });
                }
                catch (Exception ex)
                {
                    RimTalk.Util.Logger.Error($"[RimPhone] Native Chat Channel Error: {ex.Message}\n{ex.StackTrace}");
                    RimPhoneEngine.EnqueueMainThreadAction(() => pawnState.IsGeneratingTalk = false);
                }
            });
        }
    }
}