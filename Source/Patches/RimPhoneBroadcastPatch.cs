using HarmonyLib;
using RimTalk.Data;
using RimTalk.Service;
using System.Collections.Generic;
using Verse;
using RimWorld;
using RimTalkRealitySync.Platforms.Discord;

namespace RimTalkRealitySync.Patches
{
    [HarmonyPatch]
    public static class RimPhoneBroadcastPatch
    {
        // =====================================================================
        // NEW: Global Silencer Flag
        // =====================================================================
        public static bool SuppressInputBroadcast = false;

        [HarmonyPatch(typeof(TalkService), "ConsumeTalk")]
        [HarmonyPostfix]
        public static void Postfix_ConsumeTalk(ref TalkResponse __result)
        {
            if (__result != null && !string.IsNullOrWhiteSpace(__result.Text))
            {
                // =====================================================================
                // FIXED: Parallel Broadcasting (Multi-Platform Support)
                // Respects independent toggle switches for each platform.
                // =====================================================================
                var settings = RimTalkRealitySyncMod.Settings;
                if (settings.BroadcastToDiscord)
                    DiscordBroadcastService.BroadcastToDiscord(__result.Name, __result.Text);

                if (settings.BroadcastToKook)
                    RimTalkRealitySync.Platforms.Kook.KookBroadcastService.BroadcastToKook(__result.Name, __result.Text);
            }
        }

        [HarmonyPatch(typeof(ApiHistory), "AddUserHistory")]
        [HarmonyPostfix]
        public static void Postfix_AddUserHistory(Pawn initiator, Pawn recipient, string text)
        {
            // =====================================================================
            // NEW: The Silencer
            // Prevents echoing when injecting messages from remote platforms.
            // =====================================================================
            if (SuppressInputBroadcast) return;

            if (initiator == null || string.IsNullOrWhiteSpace(text)) return;

            // =====================================================================
            // CLEANED: Perfect Native Anti-Echo Shield
            // When we inject a Discord message, we intentionally set the recipient 
            // the same as the initiator to bypass UI crashes.
            // This perfectly serves as a robust flag to NOT broadcast the text back to Discord!
            // =====================================================================
            if (initiator == recipient)
            {
                return;
            }

            Pawn playerPawn = RimTalk.Data.Cache.GetPlayer();
            if (initiator == playerPawn || playerPawn == null)
            {
                string playerName = initiator.LabelShort ?? "Player";

                // =====================================================================
                // FIXED: Local Native Tagging Optimized (CS0128 Fix)
                // Header: [本地 玩家] Name
                // Body: -> Target Content
                // =====================================================================
                string displayTag = $"[本地 玩家] {playerName}";
                string routedText = recipient != null ? $"-> {recipient.LabelShort} {text}" : text;

                var settings = RimTalkRealitySyncMod.Settings;
                if (settings.BroadcastToDiscord)
                    DiscordBroadcastService.BroadcastToDiscord(displayTag, routedText);

                if (settings.BroadcastToKook)
                    RimTalkRealitySync.Platforms.Kook.KookBroadcastService.BroadcastToKook(displayTag, routedText);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    public static class RimPhonePawnGizmoPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            __result = AddGizmo(__result, __instance);
        }

        private static IEnumerable<Gizmo> AddGizmo(IEnumerable<Gizmo> original, Pawn pawn)
        {
            foreach (var gizmo in original)
            {
                yield return gizmo;
            }

            if (RimTalkRealitySyncMod.Settings.LockAllAvatarUpdates || pawn == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                // TRANSLATION HOOKS
                defaultLabel = "RTRS_Gizmo_SyncAvatar_Label".Translate(),
                defaultDesc = "RTRS_Gizmo_SyncAvatar_Desc".Translate(),
                icon = BaseContent.BadTex,
                action = () =>
                {
                    DiscordBroadcastService.ForceUpdateAvatar(pawn);
                    Messages.Message("RTRS_Msg_RequestingAvatar".Translate(pawn.Name.ToStringShort), MessageTypeDefOf.TaskCompletion, false);
                }
            };
        }
    }
    // =====================================================================
    // NEW: Ultimate Anti-Crash Thread Shield
    // RimTalk's native async networking often catches errors on background 
    // threads and calls Verse.Messages.Message, causing fatal OnGUI crashes.
    // This intercepts ALL game messages and forces them safely onto the Main Thread!
    // =====================================================================
    [HarmonyPatch(typeof(Verse.Messages), "Message", new System.Type[] { typeof(Verse.Message), typeof(bool) })]
    public static class AntiCrashMessagePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Verse.Message msg, bool historical)
        {
            if (!Verse.UnityData.IsInMainThread)
            {
                RimTalkRealitySync.Sync.RimPhoneEngine.EnqueueMainThreadAction(() =>
                {
                    Verse.Messages.Message(msg, historical);
                });
                return false; // Skip original execution on the dangerous background thread
            }
            return true;
        }
    }
}