using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace RimTalkRealitySync.Core
{
    /// <summary>
    /// Soft-Dependency Adapter for RimTalk - Expand Memory.
    /// Manages persona injection with deduplication, hidden ID tracking, and dual-mode support.
    /// </summary>
    public static class RimTalkMemoryAdapter
    {
        private static bool _initialized = false;
        private static bool _isActive = false;

        private static Type _memoryManagerType;
        private static Type _commonKnowledgeLibraryType;
        private static Type _commonKnowledgeEntryType;
        private static Type _keywordMatchModeType;
        private static Type _knowledgeEntryCategoryType;
        private static Type _extendedKnowledgeEntryType;
        private static MethodInfo _getCommonKnowledgeMethod;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                // Find classes regardless of the DLL filename
                _memoryManagerType = GenTypes.GetTypeInAnyAssembly("RimTalk.Memory.MemoryManager");
                if (_memoryManagerType == null) return;

                _commonKnowledgeLibraryType = GenTypes.GetTypeInAnyAssembly("RimTalk.Memory.CommonKnowledgeLibrary");
                _commonKnowledgeEntryType = GenTypes.GetTypeInAnyAssembly("RimTalk.Memory.CommonKnowledgeEntry");
                _keywordMatchModeType = GenTypes.GetTypeInAnyAssembly("RimTalk.Memory.KeywordMatchMode");
                _knowledgeEntryCategoryType = GenTypes.GetTypeInAnyAssembly("RimTalk.Memory.KnowledgeEntryCategory");
                _extendedKnowledgeEntryType = GenTypes.GetTypeInAnyAssembly("RimTalk.Memory.ExtendedKnowledgeEntry");

                _getCommonKnowledgeMethod = _memoryManagerType.GetMethod("GetCommonKnowledge", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                _isActive = _memoryManagerType != null && _getCommonKnowledgeMethod != null;

                if (_isActive) Log.Message("[RimPhone] RimTalk - Expand Memory adapter initialized.");
            }
            catch (Exception ex)
            {
                Log.Warning($"[RimPhone] Memory Adapter failed: {ex.Message}");
                _isActive = false;
            }
        }

        /// <summary>
        /// Injects a persona with hidden ID tracking and tag deduplication.
        /// Outputs the final cleaned tags as an out parameter.
        /// </summary>
        public static bool TryInjectPersona(string discordUserId, string senderName, string rawTags, string content, float importance, string matchModeStr, bool canExtract, bool canMatch, out string outFinalTags)
        {
            outFinalTags = "";
            if (!_initialized) Initialize();
            if (!_isActive) return false;

            try
            {
                object library = _getCommonKnowledgeMethod.Invoke(null, null);
                if (library == null) return false;

                PropertyInfo entriesProp = _commonKnowledgeLibraryType.GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance);
                FieldInfo entriesField = _commonKnowledgeLibraryType.GetField("Entries", BindingFlags.Public | BindingFlags.Instance);
                IList entriesList = (entriesProp != null) ? (entriesProp.GetValue(library, null) as IList) : (entriesField.GetValue(library) as IList);
                if (entriesList == null) return false;

                // Unique Internal Key (Hidden from UI Tag to prevent ALL mode mismatch)
                string internalKey = $"RimPhone_Discord_{discordUserId}";

                // 1. Precise Tracking: Find and remove existing entry by the hidden ID field
                FieldInfo idField = _commonKnowledgeEntryType.GetField("id", BindingFlags.Public | BindingFlags.Instance);
                if (idField != null)
                {
                    ArrayList toRemove = new ArrayList();
                    foreach (object entry in entriesList)
                    {
                        if ((idField.GetValue(entry) as string) == internalKey) toRemove.Add(entry);
                    }

                    MethodInfo removeEntryMethod = _commonKnowledgeLibraryType.GetMethod("RemoveEntry", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { _commonKnowledgeEntryType }, null);
                    foreach (object entry in toRemove)
                    {
                        if (removeEntryMethod != null) removeEntryMethod.Invoke(library, new object[] { entry });
                        else entriesList.Remove(entry);
                    }
                }

                // 2. Tag Deduplication Pool
                HashSet<string> tagPool = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(rawTags))
                {
                    foreach (var t in rawTags.Split(new[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        tagPool.Add(t.Trim());
                }

                // NEW: Removed "高维观测者" to fix native chat matching. Only senderName is required.
                tagPool.Add(senderName);

                string finalTags = string.Join(", ", tagPool);
                outFinalTags = finalTags; // Return processed tags to UI

                // 3. Construct the Soul Entry
                object newEntry = Activator.CreateInstance(_commonKnowledgeEntryType, new object[] { finalTags, content });

                // Set hidden ID for future tracking and deletion
                idField?.SetValue(newEntry, internalKey);

                _commonKnowledgeEntryType.GetField("importance").SetValue(newEntry, importance);
                _commonKnowledgeEntryType.GetField("isUserEdited").SetValue(newEntry, true);

                try
                {
                    object matchModeEnum = Enum.Parse(_keywordMatchModeType, matchModeStr, true);
                    _commonKnowledgeEntryType.GetField("matchMode").SetValue(newEntry, matchModeEnum);
                }
                catch { }

                try
                {
                    object categoryEnum = Enum.Parse(_knowledgeEntryCategoryType, "Lore", true);
                    _commonKnowledgeEntryType.GetField("category").SetValue(newEntry, categoryEnum);
                }
                catch { }

                // 4. Inject
                MethodInfo addEntryMethod = _commonKnowledgeLibraryType.GetMethod("AddEntry", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { _commonKnowledgeEntryType }, null);
                if (addEntryMethod != null) addEntryMethod.Invoke(library, new object[] { newEntry });
                else entriesList.Add(newEntry);

                // 5. Extended Attributes
                if (_extendedKnowledgeEntryType != null)
                {
                    MethodInfo setExt = _extendedKnowledgeEntryType.GetMethod("SetCanBeExtracted", BindingFlags.Public | BindingFlags.Static, null, new Type[] { _commonKnowledgeEntryType, typeof(bool) }, null);
                    setExt?.Invoke(null, new object[] { newEntry, canExtract });

                    MethodInfo setMatch = _extendedKnowledgeEntryType.GetMethod("SetCanBeMatched", BindingFlags.Public | BindingFlags.Static, null, new Type[] { _commonKnowledgeEntryType, typeof(bool) }, null);
                    setMatch?.Invoke(null, new object[] { newEntry, canMatch });
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimPhone] Injection Error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Explicitly removes the persona associated with a Discord User ID using hidden internal key.
        /// </summary>
        public static bool TryRemovePersona(string discordUserId)
        {
            if (!_initialized) Initialize();
            if (!_isActive) return false;

            try
            {
                object library = _getCommonKnowledgeMethod.Invoke(null, null);
                if (library == null) return false;

                FieldInfo entriesField = _commonKnowledgeLibraryType.GetField("Entries", BindingFlags.Public | BindingFlags.Instance);
                PropertyInfo entriesProp = _commonKnowledgeLibraryType.GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance);
                IList entriesList = (entriesProp != null) ? (entriesProp.GetValue(library, null) as IList) : (entriesField.GetValue(library) as IList);
                if (entriesList == null) return false;

                // Match against the hidden key
                string internalKey = $"RimPhone_Discord_{discordUserId}";
                FieldInfo idField = _commonKnowledgeEntryType.GetField("id", BindingFlags.Public | BindingFlags.Instance);

                if (idField != null)
                {
                    ArrayList toRemove = new ArrayList();
                    foreach (object entry in entriesList)
                    {
                        if ((idField.GetValue(entry) as string) == internalKey) toRemove.Add(entry);
                    }

                    if (toRemove.Count == 0) return true; // Already clean

                    MethodInfo removeMethod = _commonKnowledgeLibraryType.GetMethod("RemoveEntry", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { _commonKnowledgeEntryType }, null);
                    foreach (object entry in toRemove)
                    {
                        if (removeMethod != null) removeMethod.Invoke(library, new object[] { entry });
                        else entriesList.Remove(entry);
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error($"[RimPhone] Remove Error: {ex.Message}");
                return false;
            }
        }
    }
}