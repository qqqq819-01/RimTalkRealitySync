using System;
using System.Text.RegularExpressions;
using HarmonyLib;
using RimTalk.Client.OpenAI;
using RimTalk.Client.Player2;
//using RimTalk.Client.Gemini;
using RimTalk.Service;
using RimTalk.Source.Data;
using Verse;

namespace RimTalkRealitySync.Patches
{
    // DELETED: PromptService_DecoratePrompt_Patch has been completely removed.
    // Lore injection is now handled cleanly via Native UI Modules (PromptEntry)!

    [HarmonyPatch(typeof(OpenAIClient), "BuildRequestJson")]
    public static class OpenAIClient_BuildRequestJson_Patch
    {
        private static readonly Regex ImgTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)<RIMPHONE_IMG:data:([^;]+);base64[,，]([^>]+)>((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled);

        private static readonly Regex UrlTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)<RIMPHONE_URL:([^>]+)>((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled);

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;

            int totalTags = Regex.Matches(__result, @"<RIMPHONE_(URL|IMG)[^>]+>").Count;
            if (totalTags > 1)
            {
                int currentIndex = 0;
                __result = Regex.Replace(__result, @"<RIMPHONE_(URL|IMG)[^>]+>", match =>
                {
                    currentIndex++;
                    if (currentIndex == totalTags) return match.Value;
                    return "RTRS_Tag_AttachedImage".Translate(); // TRANSLATION HOOK
                });
            }

            if (__result.Contains("<RIMPHONE_URL:"))
            {
                __result = UrlTagRegex.Replace(__result, match =>
                {
                    string beforeText = match.Groups[1].Value;
                    string imageUrl = match.Groups[2].Value;
                    string afterText = match.Groups[3].Value;

                    imageUrl = imageUrl.Replace("\\/", "/");
                    string combinedText = beforeText + afterText;

                    return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{imageUrl}\"}}}}]";
                });
            }
            else if (__result.Contains("<RIMPHONE_IMG:"))
            {
                __result = ImgTagRegex.Replace(__result, match =>
                {
                    string beforeText = match.Groups[1].Value;
                    string mimeType = match.Groups[2].Value;
                    string base64Data = match.Groups[3].Value;
                    string afterText = match.Groups[4].Value;

                    base64Data = base64Data.Replace("\\/", "/");
                    string combinedText = beforeText + afterText;

                    return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"data:{mimeType};base64,{base64Data}\"}}}}]";
                });
            }
        }
    }

    [HarmonyPatch(typeof(Player2Client), "BuildRequestJson")]
    public static class Player2Client_BuildRequestJson_Patch
    {
        private static readonly Regex ImgTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)<RIMPHONE_IMG:data:([^;]+);base64[,，]([^>]+)>((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled);

        private static readonly Regex UrlTagRegex = new Regex(
            @"\""content\""\s*:\s*\""((?:\\.|[^\""\\])*?)<RIMPHONE_URL:([^>]+)>((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled);

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;

            int totalTags = Regex.Matches(__result, @"<RIMPHONE_(URL|IMG)[^>]+>").Count;
            if (totalTags > 1)
            {
                int currentIndex = 0;
                __result = Regex.Replace(__result, @"<RIMPHONE_(URL|IMG)[^>]+>", match =>
                {
                    currentIndex++;
                    if (currentIndex == totalTags) return match.Value;
                    return "RTRS_Tag_AttachedImage".Translate(); // TRANSLATION HOOK
                });
            }

            if (__result.Contains("<RIMPHONE_URL:"))
            {
                __result = UrlTagRegex.Replace(__result, match =>
                {
                    string beforeText = match.Groups[1].Value;
                    string imageUrl = match.Groups[2].Value;
                    string afterText = match.Groups[3].Value;
                    imageUrl = imageUrl.Replace("\\/", "/");
                    string combinedText = beforeText + afterText;
                    return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"{imageUrl}\"}}}}]";
                });
            }
            else if (__result.Contains("<RIMPHONE_IMG:"))
            {
                __result = ImgTagRegex.Replace(__result, match =>
                {
                    string beforeText = match.Groups[1].Value;
                    string mimeType = match.Groups[2].Value;
                    string base64Data = match.Groups[3].Value;
                    string afterText = match.Groups[4].Value;
                    base64Data = base64Data.Replace("\\/", "/");
                    string combinedText = beforeText + afterText;
                    return $"\"content\":[{{\"type\":\"text\",\"text\":\"{combinedText}\"}},{{\"type\":\"image_url\",\"image_url\":{{\"url\":\"data:{mimeType};base64,{base64Data}\"}}}}]";
                });
            }
        }
    }

    //[HarmonyPatch(typeof(GeminiClient), "BuildRequestJson")]
    public static class GeminiClient_BuildRequestJson_Patch
    {
        private static readonly Regex ImgTagRegex = new Regex(
            @"\""text\""\s*:\s*\""((?:\\.|[^\""\\])*?)<RIMPHONE_IMG:data:([^;]+);base64[,，]([^>]+)>((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled);

        private static readonly Regex UrlTagRegex = new Regex(
            @"\""text\""\s*:\s*\""((?:\\.|[^\""\\])*?)<RIMPHONE_URL:([^>]+)>((?:\\.|[^\""\\])*?)\""",
            RegexOptions.Compiled);

        [HarmonyPostfix]
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;

            int totalTags = Regex.Matches(__result, @"<RIMPHONE_(URL|IMG)[^>]+>").Count;
            if (totalTags > 1)
            {
                int currentIndex = 0;
                __result = Regex.Replace(__result, @"<RIMPHONE_(URL|IMG)[^>]+>", match =>
                {
                    currentIndex++;
                    if (currentIndex == totalTags) return match.Value;
                    return "RTRS_Tag_AttachedImage".Translate(); // TRANSLATION HOOK
                });
            }

            if (__result.Contains("<RIMPHONE_URL:"))
            {
                __result = UrlTagRegex.Replace(__result, match =>
                {
                    string beforeText = match.Groups[1].Value;
                    string imageUrl = match.Groups[2].Value;
                    string afterText = match.Groups[3].Value;
                    imageUrl = imageUrl.Replace("\\/", "/");
                    string combinedText = beforeText + afterText;
                    return $"\"text\":\"{combinedText}\"}},{{\"fileData\":{{\"mimeType\":\"image/jpeg\",\"fileUri\":\"{imageUrl}\"}}";
                });
            }
            else if (__result.Contains("<RIMPHONE_IMG:"))
            {
                __result = ImgTagRegex.Replace(__result, match =>
                {
                    string beforeText = match.Groups[1].Value;
                    string mimeType = match.Groups[2].Value;
                    string base64Data = match.Groups[3].Value;
                    string afterText = match.Groups[4].Value;
                    mimeType = mimeType.Replace("\\/", "/");
                    base64Data = base64Data.Replace("\\/", "/");
                    string combinedText = beforeText + afterText;
                    return $"\"text\":\"{combinedText}\"}},{{\"inlineData\":{{\"mimeType\":\"{mimeType}\",\"data\":\"{base64Data}\"}}";
                });
            }
        }
    }

    [HarmonyPatch(typeof(TalkService), "AddResponsesToHistory")]
    public static class TalkService_AddResponsesToHistory_Patch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string prompt)
        {
            if (string.IsNullOrEmpty(prompt)) return;

            string localizedTag = "RTRS_Tag_AttachedImage".Translate(); // TRANSLATION HOOK
            prompt = Regex.Replace(prompt, @"<RIMPHONE_URL:[^>]+>", localizedTag);
            prompt = Regex.Replace(prompt, @"<RIMPHONE_IMG:[^>]+>", localizedTag);
            prompt = Regex.Replace(prompt, @"<RIMPHONE_LOCAL_IMG:[^>]+>", localizedTag);
        }
    }
}