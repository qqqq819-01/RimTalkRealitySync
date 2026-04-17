using RimTalkRealitySync.Core;
using RimTalkRealitySync.Platforms.Discord;
using System;
using System.Collections.Generic;
using Verse;

namespace RimTalkRealitySync
{
    public static class RimPhoneCommandProcessor
    {
        // =====================================================================
        // NEW: Omni-Directional Broadcast Router
        // Sends system announcements to ALL configured platforms automatically.
        // =====================================================================
        public static void OmniBroadcast(string sender, string msg, RealitySyncSettings settings)
        {
            // Respect independent platform toggles set in UI
            if (settings.BroadcastToDiscord && !string.IsNullOrWhiteSpace(settings.DiscordWebhookUrl))
                DiscordBroadcastService.BroadcastToDiscord(sender, msg);

            if (settings.BroadcastToKook && !string.IsNullOrWhiteSpace(settings.KookBotToken) && !string.IsNullOrWhiteSpace(settings.KookChannelId))
                RimTalkRealitySync.Platforms.Kook.KookBroadcastService.BroadcastToKook(sender, msg);

            // =====================================================================
            // NEW: Route to QQ Channels
            // =====================================================================
            //if (settings.BroadcastToQQ && !string.IsNullOrWhiteSpace(settings.QQAppID))
                //RimTalkRealitySync.Platforms.QQ.QGuildBroadcastService.BroadcastToQQ(sender, msg);
        }

        public static void ProcessSystemCommands(List<DiscordMessage> newMessages, RealitySyncSettings settings)
        {
            List<DiscordMessage> inboxMessages = new List<DiscordMessage>();
            foreach (var m in newMessages)
            {
                if (m.Content.StartsWith("/login "))
                {
                    string attemptKey = m.Content.Substring(7).Trim();
                    if (!string.IsNullOrEmpty(settings.PlayerLinkKey) && attemptKey == settings.PlayerLinkKey)
                    {
                        settings.LinkedDiscordUserId = m.SenderId;
                        settings.LinkedDiscordUsername = m.SenderName;
                        settings.LinkedDiscordAvatarUrl = m.SenderAvatarUrl;
                        settings.Write();

                        OmniBroadcast("RimOS 系统", $"**[系统通知]** 身份验证通过。最高指挥官 `{m.SenderName}` (来自 {m.SourcePlatform} 节点)，欢迎重获边缘世界控制权！", settings);
                        Verse.Messages.Message($"RimPhone: Successfully linked to [{m.SenderName}] via {m.SourcePlatform}", RimWorld.MessageTypeDefOf.PositiveEvent, false);
                    }
                }
                else if (m.Content == "/logout")
                {
                    if (settings.LinkedDiscordUserId == m.SenderId)
                    {
                        settings.LinkedDiscordUserId = "";
                        settings.LinkedDiscordUsername = "";
                        settings.LinkedDiscordAvatarUrl = "";
                        settings.PlayerLinkKey = Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
                        settings.Write();

                        OmniBroadcast("RimOS 系统", $"**[系统通知]** 指挥官 `{m.SenderName}` ({m.SourcePlatform}) 已主动断开灵魂连接。神明移开了视线。", settings);
                        Verse.Messages.Message($"RimPhone: User [{m.SenderName}] has unlinked.", RimWorld.MessageTypeDefOf.NeutralEvent, false);
                    }
                }
                else if (m.Content.StartsWith("/人设 ") || m.Content.StartsWith("/persona "))
                {
                    try
                    {
                        string cmdPrefix = m.Content.StartsWith("/人设 ") ? "/人设 " : "/persona ";
                        string input = m.Content.Substring(cmdPrefix.Length).Trim();
                        // ... 解析逻辑保持不变 ...
                        string rawTags = ""; float importance = 1.0f; string matchMode = "Any";
                        bool canExtract = false; bool canMatch = false; string content = input;

                        if (input.StartsWith("["))
                        {
                            int closeBracket = input.IndexOf(']');
                            if (closeBracket > 0)
                            {
                                string header = input.Substring(1, closeBracket - 1);
                                content = input.Substring(closeBracket + 1).Trim();
                                string[] parts = header.Split('|');
                                if (parts.Length > 0) rawTags = parts[0].Trim();
                                if (parts.Length > 1) float.TryParse(parts[1], out importance);
                                if (parts.Length > 2) matchMode = parts[2].Trim();
                                if (parts.Length > 3) bool.TryParse(parts[3], out canExtract);
                                if (parts.Length > 4) bool.TryParse(parts[4], out canMatch);
                            }
                        }

                        if (RimTalkMemoryAdapter.TryInjectPersona(m.SenderId, m.SenderName, rawTags, content, importance, matchMode, canExtract, canMatch, out string outFinalTags))
                        {
                            // TRANSLATION HOOK
                            OmniBroadcast("RimOS 系统", "RTRS_Msg_PersonaInjected".Translate(m.SenderName, m.SourcePlatform, outFinalTags, matchMode), settings);
                        }
                    }
                    catch (Exception ex) { Log.Error($"[RimPhone] Persona error: {ex.Message}"); }
                }
                else if (m.Content.StartsWith("/移除人设") || m.Content.StartsWith("/clearpersona"))
                {
                    if (RimTalkMemoryAdapter.TryRemovePersona(m.SenderId))
                    {
                        // TRANSLATION HOOK
                        OmniBroadcast("RimOS 系统", "RTRS_Msg_PersonaRemoved".Translate(m.SenderName, m.SourcePlatform), settings);
                    }
                }
                else
                {
                    inboxMessages.Add(m);
                }
            }

            if (inboxMessages.Count > 0) DiscordNetworkService.Messages.InsertRange(0, inboxMessages);
        }
    }
}