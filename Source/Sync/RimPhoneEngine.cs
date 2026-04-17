using RimTalk.Prompt;
using RimTalkRealitySync.Platforms.Discord;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Windows;
using Verse;

namespace RimTalkRealitySync.Sync
{
    /// <summary>
    /// The Unity Main Thread Bridge.
    /// Handles scheduled updates, auto-send cooldowns, and the critical EnqueueMainThreadAction dispatcher.
    /// </summary>
    public class RimPhoneEngine : MonoBehaviour
    {
        private static Queue<Action> _mainThreadQueue = new Queue<Action>();
        private static readonly object _queueLock = new object();
        private float _timer = 0f;

        private bool _moduleInjected = false;

        public static void EnqueueMainThreadAction(Action action)
        {
            lock (_queueLock) { _mainThreadQueue.Enqueue(action); }
        }

        void Update()
        {
            // Execute all background-queued actions on the main thread safely
            lock (_queueLock)
            {
                while (_mainThreadQueue.Count > 0)
                {
                    _mainThreadQueue.Dequeue()?.Invoke();
                }
            }

            if (Current.ProgramState != ProgramState.Playing) return;

            // =====================================================================
            // HOTKEY REDUX: Native Unity Input Polling (Unchained)
            // RimTalk's speech bubbles steal Unity's GUI focus while fading out, 
            // which was falsely tripping our anti-typing lock. We removed the lock.
            // =====================================================================
            if (RimTalkRealitySyncMod.Settings != null && RimTalkRealitySyncMod.Settings.RimPhoneHotkey != KeyCode.None)
            {
                if (UnityEngine.Input.GetKeyDown(RimTalkRealitySyncMod.Settings.RimPhoneHotkey))
                {
                    RimTalkRealitySync.UI.RimPhoneStateManager.ToggleWindow();
                }
            }

            // Prompt Injection (Can be migrated to XML Defs in Phase 3)
            if (!_moduleInjected)
            {
                _moduleInjected = true;
                var manager = PromptManager.Instance;
                if (manager != null)
                {
                    var preset = manager.GetActivePreset();
                    if (preset != null)
                    {
                        string modId = "RimTalkRealitySync";

                        // Observer Lore Module
                        string loreEntryName = "RimPhone: Observer Lore";
                        string loreDetId = PromptEntry.GenerateDeterministicId(modId, loreEntryName);
                        if (!preset.DeletedModEntryIds.Contains(loreDetId) && !preset.Entries.Any(e => e.Id == loreDetId))
                        {
                            // =====================================================================
                            // FIXED: Replaced hardcoded prompt with XML Translation hook
                            // Using .ToString() to safely cast TaggedString to normal string
                            // =====================================================================
                            string lorePrompt = "RTRS_Prompt_ObserverLore".Translate().ToString();
                            var newEntry = new PromptEntry(loreEntryName, lorePrompt, PromptRole.System);
                            newEntry.SourceModId = modId;
                            newEntry.Position = PromptPosition.Relative;
                            preset.Entries.Insert(0, newEntry);
                        }

                        // Image Protocol Module
                        string imgEntryName = "RimPhone: Image Protocol";
                        string imgDetId = PromptEntry.GenerateDeterministicId(modId, imgEntryName);
                        if (!preset.DeletedModEntryIds.Contains(imgDetId) && !preset.Entries.Any(e => e.Id == imgDetId))
                        {
                            // TRANSLATION HOOK
                            string imgPrompt = "RTRS_Prompt_ImageProtocol".Translate().ToString();
                            var newImgEntry = new PromptEntry(imgEntryName, imgPrompt, PromptRole.System);
                            newImgEntry.SourceModId = modId;
                            newImgEntry.Position = PromptPosition.Relative;
                            preset.Entries.Insert(1, newImgEntry);
                        }

                        // Reality Sync Module
                        string syncEntryName = "RimPhone: Reality Sync";
                        string syncDetId = PromptEntry.GenerateDeterministicId(modId, syncEntryName);
                        if (!preset.DeletedModEntryIds.Contains(syncDetId) && !preset.Entries.Any(e => e.Id == syncDetId))
                        {
                            // TRANSLATION HOOK
                            string syncPrompt = "RTRS_Prompt_RealitySync".Translate().ToString();
                            var newSyncEntry = new PromptEntry(syncEntryName, syncPrompt, PromptRole.System);
                            newSyncEntry.SourceModId = modId;
                            newSyncEntry.Position = PromptPosition.Relative;
                            preset.Entries.Insert(2, newSyncEntry);
                        }
                        Log.Message("[RimPhone] Successfully injected Commander's Trilogy Native Modules.");
                    }
                }
            }
            // =====================================================================
            // SMART QUEUE: Intelligent Auto-Send (Phase 1.8 - Global Silence & Anti-Jam)
            // =====================================================================
            if (DiscordNetworkService.AutoSendEnabled && DiscordNetworkService.Messages.Count > 0)
            {
                // RULE 1: Check if the AI engine is completely idle globally
                if (!RimTalk.Service.AIService.IsBusy())
                {
                    int lastIndex = DiscordNetworkService.Messages.Count - 1;
                    var msg = DiscordNetworkService.Messages[lastIndex];
                    Pawn targetPawn = PawnsFinder.AllMaps_FreeColonists.FirstOrDefault(p => p.Name.ToStringShort == msg.TargetPawn);

                    if (targetPawn != null)
                    {
                        // RULE 2: GLOBAL SILENCE CHECK
                        // Scan all colonists. If anyone is thinking or talking, the base is NOT silent.
                        bool isGlobalSilence = true;
                        foreach (var p in PawnsFinder.AllMaps_FreeColonists)
                        {
                            var ps = RimTalk.Data.Cache.Get(p);
                            if (ps != null && (ps.IsGeneratingTalk || !ps.TalkResponses.Empty()))
                            {
                                isGlobalSilence = false;
                                break;
                            }
                        }

                        // =====================================================================
                        // PATIENT INJECTION: Only send when the base is completely silent.
                        // Removed the impatient anti-jam timer to allow long natural conversations.
                        // =====================================================================
                        if (isGlobalSilence)
                        {
                            RimPhoneChatProcessor.InjectMessageIntoRimTalk(targetPawn, msg);
                            DiscordNetworkService.Messages.RemoveAt(lastIndex);
                        }
                    }
                    else
                    {
                        // Target pawn vanished or invalid, discard to prevent clogging the pipeline
                        DiscordNetworkService.Messages.RemoveAt(lastIndex);
                    }
                }
            }

            // =====================================================================
            // DYNAMIC REFRESH: Multi-Platform Polling Router
            // =====================================================================
            _timer += Time.deltaTime;
            if (_timer >= RimTalkRealitySyncMod.Settings.InboxRefreshIntervalSeconds)
            {
                _timer = 0f;
                if (RimTalkRealitySyncMod.Settings.ActivePlatform == RealitySyncSettings.PlatformType.Discord)
                {
                    Platforms.Discord.DiscordFetcher.SyncDiscordMessagesAsync(false);
                }
                else if (RimTalkRealitySyncMod.Settings.ActivePlatform == RealitySyncSettings.PlatformType.Kook)
                {
                    Platforms.Kook.KookFetcher.SyncKookMessagesAsync(false);
                }
                //else if (RimTalkRealitySyncMod.Settings.ActivePlatform == RealitySyncSettings.PlatformType.QQ)
               //{
                    //Platforms.QQ.QGuildFetcher.SyncQQMessagesAsync(false);
               //}
            }
        }
    }
}