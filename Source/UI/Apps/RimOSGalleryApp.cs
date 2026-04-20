using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalkRealitySync.Platforms.Discord;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalkRealitySync.UI.Apps
{
    /// <summary>
    /// The virtual RimPhone Gallery UI.
    /// Handles image browsing, filtering, and delegating image uploads to the Network Service.
    /// </summary>
    public class RimOSGalleryApp : Window
    {
        public override Vector2 InitialSize => new Vector2(380f, 650f);
        protected override float Margin => 0f;

        private bool _isClosing = false;
        public bool IsClosing => _isClosing;

        private float _animProgress = 0f;
        private readonly float _animSpeed = 4.0f;

        // Splash & Return fade progress for seamless transitions
        private float _splashFadeProgress = 0f;
        private float _returnFadeProgress = 0f;
        private bool _isReturningToHome = false;

        // Anti-Flicker sync frames
        private int _closeDelayFrames = -1;
        private int _transitionDelayFrames = 0;

        private static Texture2D _galleryIconTex;

        // Global State Persistence
        private enum TimeFilter { PastHour, PastDay, PastWeek, All }
        private static TimeFilter _currentFilter = TimeFilter.All;
        private static Vector2 _scrollPosition;

        private static Dictionary<DateTime, List<string>> _groupedGallery = new Dictionary<DateTime, List<string>>();
        private static Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
        private static FileInfo[] _allFiles;
        private static string _selectedImagePath = null;
        private static DateTime _lastDirWriteTime = DateTime.MinValue;

        private int _texturesLoadedThisFrame = 0;

        // Animated Filter Drawer State
        private bool _isFilterRevealed = false;
        private float _filterAnimProgress = 0f;
        private readonly float _filterAnimSpeed = 8.0f;
        private float _lastMouseHoverTime = 0f;
        private readonly float _autoHideDelaySeconds = 2.0f;

        // Physics-Based Fading Scrollbar State
        private float _scrollbarAlpha = 0f;
        private float _lastScrollInteractionTime = 0f;
        private float _lastScrollPosY = 0f;
        private bool _isDraggingScrollbar = false;

        private float _colonistScrollbarAlpha = 0f;
        private float _lastColonistScrollInteractionTime = 0f;
        private float _lastColonistScrollPosY = 0f;
        private bool _isDraggingColonistScrollbar = false;

        // Message & Recipient Slide-up Menu State
        private static string _messageText = "";
        private bool _isRecipientMenuOpen = false;
        private float _recipientAnimProgress = 0f;
        private readonly float _recipientAnimSpeed = 6.0f;
        private static Vector2 _colonistScrollPos;

        // Strictly track the single selected recipient
        private static Pawn _selectedRecipient = null;

        private static Texture2D _roundedBackgroundTex;

        public RimOSGalleryApp(bool skipSlideAnimation = false)
        {
            this.doCloseX = false;
            this.doCloseButton = false;
            this.draggable = false;
            this.resizeable = false;
            this.preventCameraMotion = false;
            this.absorbInputAroundWindow = false;
            this.focusWhenOpened = true;
            this.layer = WindowLayer.Dialog;
            this.doWindowBackground = false;
            this.drawShadow = false;

            _animProgress = skipSlideAnimation ? 1f : 0f;

            if (skipSlideAnimation)
            {
                _splashFadeProgress = 1f;
                _transitionDelayFrames = 2;
            }
            else
            {
                _splashFadeProgress = 0f;
            }

            ScanGalleryFilesSmart();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            if (_animProgress < 1f) SoundDefOf.Tick_High.PlayOneShotOnCamera();

            if (_roundedBackgroundTex == null) _roundedBackgroundTex = CreateRoundedTexture(380, 650, 20, new Color(0.12f, 0.12f, 0.12f, 1f));

            // FIXED: Load the real HD texture instead of creating a placeholder block
            if (_galleryIconTex == null) _galleryIconTex = ContentFinder<Texture2D>.Get("UI/RimPhone/Icon_Gallery", true);
        }

        protected override void SetInitialSizeAndPosition()
        {
            float startY = Verse.UI.screenHeight + 10f;
            float targetX = Verse.UI.screenWidth - InitialSize.x - 20f;
            windowRect = new Rect(targetX, startY, InitialSize.x, InitialSize.y);
        }

        private void ScanGalleryFilesSmart()
        {
            try
            {
                string path = RimPhoneCore.GalleryPath;
                if (Directory.Exists(path))
                {
                    DateTime currentWriteTime = Directory.GetLastWriteTime(path);
                    if (currentWriteTime > _lastDirWriteTime || _allFiles == null)
                    {
                        var dirInfo = new DirectoryInfo(path);
                        _allFiles = dirInfo.GetFiles("*.*").Where(f => f.Extension.ToLower() == ".jpg" || f.Extension.ToLower() == ".jpeg" || f.Extension.ToLower() == ".png").OrderByDescending(f => f.CreationTime).ToArray();
                        ApplyTimeFilter();
                        _lastDirWriteTime = currentWriteTime;
                    }
                }
            }
            catch (Exception ex) { Log.Error($"[RimPhone] Failed to scan gallery: {ex.Message}"); }
        }

        private void ApplyTimeFilter()
        {
            if (_allFiles == null) return;
            _groupedGallery.Clear(); DateTime now = DateTime.Now;
            foreach (var file in _allFiles)
            {
                TimeSpan age = now - file.CreationTime; bool keep = false;
                switch (_currentFilter)
                {
                    case TimeFilter.PastHour: keep = age.TotalHours <= 1; break;
                    case TimeFilter.PastDay: keep = age.TotalDays <= 1; break;
                    case TimeFilter.PastWeek: keep = age.TotalDays <= 7; break;
                    case TimeFilter.All: keep = true; break;
                }
                if (keep)
                {
                    DateTime dateKey = file.CreationTime.Date;
                    if (!_groupedGallery.ContainsKey(dateKey)) _groupedGallery[dateKey] = new List<string>();
                    _groupedGallery[dateKey].Add(file.FullName);
                }
            }
        }

        public override void WindowUpdate()
        {
            if (_closeDelayFrames > 0)
            {
                _closeDelayFrames--;
            }
            else if (_closeDelayFrames == 0)
            {
                _closeDelayFrames = -1;
                base.Close(false);
                return;
            }

            base.WindowUpdate();

            float targetY = Verse.UI.screenHeight - InitialSize.y - 20f; float offScreenY = Verse.UI.screenHeight + 10f;
            if (_isClosing) { _animProgress -= Time.deltaTime * _animSpeed; if (_animProgress <= 0f) base.Close(false); }
            else { _animProgress += Time.deltaTime * _animSpeed; if (_animProgress > 1f) _animProgress = 1f; }
            float ease = Mathf.SmoothStep(0f, 1f, _animProgress); windowRect.y = Mathf.Lerp(offScreenY, targetY, ease);

            if (_splashFadeProgress > 0f)
            {
                if (_transitionDelayFrames > 0) _transitionDelayFrames--;
                else
                {
                    _splashFadeProgress -= Time.deltaTime * 6f;
                    if (_splashFadeProgress < 0f) _splashFadeProgress = 0f;
                }
            }

            if (_isReturningToHome)
            {
                _returnFadeProgress += Time.deltaTime * 6f;
                if (_returnFadeProgress >= 1f)
                {
                    _returnFadeProgress = 1f;
                    _isReturningToHome = false;

                    RimPhoneStateManager.CurrentApp = "Home";
                    Find.WindowStack.Add(new RimOSHomeScreen(skipSlideAnimation: true, returningFromApp: "Gallery"));
                    _closeDelayFrames = 2;
                }
            }

            if (_isFilterRevealed) { _filterAnimProgress += Time.deltaTime * _filterAnimSpeed; if (_filterAnimProgress > 1f) _filterAnimProgress = 1f; if (Time.realtimeSinceStartup - _lastMouseHoverTime > _autoHideDelaySeconds) _isFilterRevealed = false; }
            else { _filterAnimProgress -= Time.deltaTime * _filterAnimSpeed; if (_filterAnimProgress < 0f) _filterAnimProgress = 0f; }

            if (_isRecipientMenuOpen) { _recipientAnimProgress += Time.deltaTime * _recipientAnimSpeed; if (_recipientAnimProgress > 1f) _recipientAnimProgress = 1f; }
            else { _recipientAnimProgress -= Time.deltaTime * _recipientAnimSpeed; if (_recipientAnimProgress < 0f) _recipientAnimProgress = 0f; }

            float targetAlpha = (Time.realtimeSinceStartup - _lastScrollInteractionTime < 1.5f) ? 1f : 0f; _scrollbarAlpha = Mathf.Lerp(_scrollbarAlpha, targetAlpha, Time.deltaTime * 8f);
            float targetColAlpha = (Time.realtimeSinceStartup - _lastColonistScrollInteractionTime < 1.5f) ? 1f : 0f; _colonistScrollbarAlpha = Mathf.Lerp(_colonistScrollbarAlpha, targetColAlpha, Time.deltaTime * 8f);
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current.type == EventType.MouseDown && !windowRect.Contains(Event.current.mousePosition)) { GUI.FocusControl(null); Verse.UI.UnfocusCurrentControl(); }
        }

        public override void PostClose() { base.PostClose(); }

        public void SlideDownAndClose() { if (!_isClosing) { _isClosing = true; SoundDefOf.Tick_Low.PlayOneShotOnCamera(); } }
        public void CloseInstantly() { base.Close(false); }
        public override void Close(bool doCloseSound = true) { SlideDownAndClose(); }

        public override void DoWindowContents(Rect inRect)
        {
            if (Event.current.type == EventType.Repaint) _texturesLoadedThisFrame = 0;

            Rect contentRect = inRect.ContractedBy(12f);
            float maxDrawerHeight = 35f; float currentDrawerHeight = Mathf.SmoothStep(0f, 1f, _filterAnimProgress) * maxDrawerHeight;
            float currentYMock = contentRect.y + 30f + 5f + currentDrawerHeight + (currentDrawerHeight > 0.1f ? 5f : 0f);
            float bottomReservedSpace = 100f;
            Rect gridRectMock = new Rect(contentRect.x, currentYMock, contentRect.width, contentRect.height - (currentYMock - contentRect.y) - bottomReservedSpace);
            float bottomYMock = gridRectMock.yMax + 10f;
            Rect inputRect = new Rect(contentRect.x, bottomYMock, contentRect.width - 40f, 35f);
            Rect atBtnRect = new Rect(inputRect.xMax + 5f, bottomYMock, 35f, 35f);

            if (Event.current.type == EventType.MouseDown && !inputRect.Contains(Event.current.mousePosition) && !atBtnRect.Contains(Event.current.mousePosition))
            { GUI.FocusControl(null); Verse.UI.UnfocusCurrentControl(); }

            GUI.color = Color.white; GUI.DrawTexture(inRect, _roundedBackgroundTex, ScaleMode.StretchToFill, true);

            Rect topInteractionArea = new Rect(contentRect.x, contentRect.y, contentRect.width, 40f + (_filterAnimProgress * 30f));
            if (topInteractionArea.Contains(Event.current.mousePosition)) _lastMouseHoverTime = Time.realtimeSinceStartup;

            Rect headerRect = new Rect(contentRect.x, contentRect.y, contentRect.width, 30f);
            Text.Font = GameFont.Medium; Widgets.Label(headerRect, "<b><color=#00FFFF>" + "RTRS_App_Gallery".Translate() + "</color></b>"); Text.Font = GameFont.Small;
            Rect filterBtnRect = new Rect(headerRect.xMax - 30f, headerRect.y + 4f, 30f, 20f);
            if (Widgets.ButtonText(filterBtnRect, "≡", drawBackground: false)) { _isFilterRevealed = !_isFilterRevealed; if (_isFilterRevealed) _lastMouseHoverTime = Time.realtimeSinceStartup; }

            float currentY = headerRect.yMax + 5f;
            if (currentDrawerHeight > 0.1f)
            {
                Rect drawerRect = new Rect(contentRect.x, currentY, contentRect.width, currentDrawerHeight);
                GUI.BeginGroup(drawerRect); DrawTabs(new Rect(0, currentDrawerHeight - maxDrawerHeight, contentRect.width, maxDrawerHeight)); GUI.EndGroup();
            }
            currentY += currentDrawerHeight + (currentDrawerHeight > 0.1f ? 5f : 0f);

            Rect gridRect = new Rect(contentRect.x, currentY, contentRect.width, contentRect.height - (currentY - contentRect.y) - bottomReservedSpace);
            float maxMenuHeight = 280f; float slideMenuHeight = Mathf.SmoothStep(0f, 1f, _recipientAnimProgress) * maxMenuHeight;
            Rect slideMenuRect = new Rect(contentRect.x, gridRect.yMax - slideMenuHeight, contentRect.width, slideMenuHeight);
            bool isMenuBlocking = _isRecipientMenuOpen && slideMenuHeight > 0.1f && slideMenuRect.Contains(Event.current.mousePosition);
            EventType originalEventType = Event.current.type;
            bool hideInputFromGallery = isMenuBlocking && (Event.current.isMouse || Event.current.type == EventType.ScrollWheel);

            if (hideInputFromGallery) Event.current.type = EventType.Ignore;
            DrawTimelineGrid(gridRect, isMenuBlocking);
            if (hideInputFromGallery) Event.current.type = originalEventType;
            if (slideMenuHeight > 0.1f) DrawRecipientMenu(slideMenuRect);

            float bottomY = gridRect.yMax + 10f; string prevText = _messageText; GUI.SetNextControlName("MessageInput");
            string newText = Widgets.TextField(inputRect, _messageText);
            if (newText != prevText)
            {
                if (newText.Length < prevText.Length && _selectedRecipient != null) { string tag = $"@{_selectedRecipient.Name.ToStringShort} "; if (prevText.Contains(tag) && !newText.Contains(tag)) { newText = prevText.Replace(tag, ""); _selectedRecipient = null; } }
                if (newText.EndsWith("@") && !_isRecipientMenuOpen) _isRecipientMenuOpen = true; _messageText = newText;
            }

            if (string.IsNullOrEmpty(_messageText) && GUI.GetNameOfFocusedControl() != "MessageInput")
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.8f); Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(inputRect.x + 5f, inputRect.y, inputRect.width, inputRect.height), "RTRS_Gallery_TypeToSelect".Translate());
                Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            }

            GUI.color = _isRecipientMenuOpen ? Color.cyan : new Color(0.2f, 0.2f, 0.2f, 0.9f);
            Widgets.DrawBoxSolid(atBtnRect, GUI.color); GUI.color = _isRecipientMenuOpen ? Color.black : Color.white;
            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
            if (Widgets.ButtonText(atBtnRect, "@", drawBackground: false)) { _isRecipientMenuOpen = !_isRecipientMenuOpen; if (_isRecipientMenuOpen && !_messageText.EndsWith("@")) { _messageText += "@"; GUI.FocusControl("MessageInput"); } }
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;

            Rect btnRowRect = new Rect(contentRect.x, inputRect.yMax + 10f, contentRect.width, 35f);
            Rect closeRect = new Rect(btnRowRect.x, btnRowRect.y, btnRowRect.width / 2f - 5f, btnRowRect.height);
            Rect sendRect = new Rect(closeRect.xMax + 10f, btnRowRect.y, btnRowRect.width / 2f - 5f, btnRowRect.height);

            GUI.color = new Color(0.2f, 0.6f, 0.6f); Widgets.DrawBoxSolid(closeRect, GUI.color); GUI.color = Color.white;

            if (Widgets.ButtonText(closeRect, "RTRS_Btn_Home".Translate(), drawBackground: false))
            {
                _isReturningToHome = true;
                _returnFadeProgress = 0f;
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            GUI.color = new Color(0.2f, 0.8f, 0.2f); Widgets.DrawBoxSolid(sendRect, GUI.color); GUI.color = Color.white;
            if (Widgets.ButtonText(sendRect, "RTRS_Btn_SendImage".Translate(), drawBackground: false))
            {
                if (_selectedRecipient == null) Verse.Messages.Message("RTRS_Msg_MustSelectColonist".Translate(), MessageTypeDefOf.RejectInput, false);
                else if (string.IsNullOrEmpty(_selectedImagePath)) Verse.Messages.Message("RTRS_Msg_MustSelectImage".Translate(), MessageTypeDefOf.RejectInput, false);
                else
                {
                    try
                    {
                        string cleanMessage = _messageText.Replace($"@{_selectedRecipient.Name.ToStringShort} ", "").Trim();
                        Pawn targetPawn = _selectedRecipient; string imagePath = _selectedImagePath;

                        // Delegate to the shared network service (can be moved to ChatProcessor in future)
                        DiscordNetworkService.ProcessAndSendImage(imagePath, targetPawn, cleanMessage);

                        Verse.Messages.Message("RTRS_Msg_UploadingImage".Translate(), MessageTypeDefOf.TaskCompletion, false);
                        _messageText = ""; _selectedRecipient = null; _selectedImagePath = null; GUI.FocusControl(null); SoundDefOf.Tick_High.PlayOneShotOnCamera();
                    }
                    catch (Exception ex) { Log.Error($"[RimPhone] Critical failure during payload injection: {ex.Message}"); Verse.Messages.Message("RTRS_Msg_ProcessImageFailed".Translate(), MessageTypeDefOf.RejectInput, false); }
                }
            }

            float overlayAlpha = Mathf.Max(_splashFadeProgress, _returnFadeProgress);
            if (overlayAlpha > 0.01f)
            {
                // FIXED: Re-added centerIconRect definition to resolve CS0103 error
                float centerIconSize = 110f;
                Rect centerIconRect = new Rect(inRect.center.x - centerIconSize / 2f, inRect.center.y - centerIconSize / 2f, centerIconSize, centerIconSize);

                GUI.color = new Color(1f, 1f, 1f, overlayAlpha);
                if (_galleryIconTex != null) GUI.DrawTexture(centerIconRect, _galleryIconTex);

                // FIXED: Removed the hardcoded 'G' letter rendering
                GUI.color = Color.white;
            }
        }

        private void DrawRecipientMenu(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.08f, 0.98f)); GUI.color = Color.cyan; Widgets.DrawBox(rect, 1); GUI.color = Color.white;
            List<Pawn> colonists = new List<Pawn>();
            if (Current.ProgramState == ProgramState.Playing) colonists = PawnsFinder.AllMaps_FreeColonists.ToList();

            if (colonists.Count == 0) { Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(rect, "<color=#888888>" + "RTRS_Gallery_NoColonists".Translate() + "</color>"); Text.Anchor = TextAnchor.UpperLeft; return; }

            float itemHeight = 45f; float viewHeight = colonists.Count * itemHeight; float scrollableRange = Mathf.Max(0, viewHeight - rect.height);
            float trackWidth = 12f; Rect trackRect = new Rect(rect.xMax - trackWidth, rect.y, trackWidth, rect.height);
            bool isMouseOverTrack = trackRect.Contains(Event.current.mousePosition);

            if (isMouseOverTrack || _isDraggingColonistScrollbar || Mathf.Abs(_colonistScrollPos.y - _lastColonistScrollPosY) > 0.1f) _lastColonistScrollInteractionTime = Time.realtimeSinceStartup;
            _lastColonistScrollPosY = _colonistScrollPos.y;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && isMouseOverTrack) { _isDraggingColonistScrollbar = true; Event.current.Use(); }
            if (Event.current.type == EventType.MouseUp && Event.current.button == 0) _isDraggingColonistScrollbar = false;

            if (_isDraggingColonistScrollbar && Event.current.type == EventType.MouseDrag)
            {
                float thumbHeight = Mathf.Max(20f, rect.height * (rect.height / viewHeight)); float thumbMovableRange = rect.height - thumbHeight;
                if (thumbMovableRange > 0) { float deltaScroll = Event.current.delta.y * (scrollableRange / thumbMovableRange); _colonistScrollPos.y += deltaScroll; _colonistScrollPos.y = Mathf.Clamp(_colonistScrollPos.y, 0f, scrollableRange); }
                Event.current.Use();
            }

            Rect viewRect = new Rect(0, 0, rect.width, viewHeight); Widgets.BeginScrollView(rect, ref _colonistScrollPos, viewRect, false);

            float currentY = 0f;
            foreach (Pawn pawn in colonists)
            {
                Rect itemRect = new Rect(0, currentY, viewRect.width, itemHeight);
                if (Mouse.IsOver(itemRect) && !_isDraggingColonistScrollbar) Widgets.DrawHighlight(itemRect);

                Rect portraitRect = new Rect(itemRect.x + 8f, itemRect.y + 4f, 36f, 36f);
                RenderTexture tex = PortraitsCache.Get(pawn, new Vector2(36f, 36f), Rot4.South);
                if (tex != null) GUI.DrawTexture(portraitRect, tex);

                Rect labelRect = new Rect(portraitRect.xMax + 12f, itemRect.y, viewRect.width - 60f, itemHeight);
                Text.Anchor = TextAnchor.MiddleLeft;
                // FIXED: Used .ToString() to prevent CS8957 ternary operator type mismatch
                string title = pawn.story?.TitleShort ?? "RTRS_Gallery_ColonistTitle".Translate().ToString();
                Widgets.Label(labelRect, $"<b>{pawn.Name.ToStringShort}</b> <color=#888888>({title})</color>"); Text.Anchor = TextAnchor.UpperLeft;

                bool isClicked = false;
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && itemRect.Contains(Event.current.mousePosition)) { isClicked = true; Event.current.Use(); }
                else if (Widgets.ButtonInvisible(itemRect)) isClicked = true;

                if (isClicked)
                {
                    GUI.FocusControl(null); Verse.UI.UnfocusCurrentControl();
                    if (_selectedRecipient != null) _messageText = _messageText.Replace($"@{_selectedRecipient.Name.ToStringShort} ", "");
                    _selectedRecipient = pawn; string newTag = $"@{pawn.Name.ToStringShort} ";
                    int atIndex = _messageText.LastIndexOf('@');
                    if (atIndex >= 0) _messageText = _messageText.Substring(0, atIndex) + newTag; else _messageText = newTag + _messageText;
                    _isRecipientMenuOpen = false;
                }
                currentY += itemHeight;
            }
            Widgets.EndScrollView();

            if (_colonistScrollbarAlpha > 0.01f && viewHeight > rect.height)
            {
                float thumbRatio = rect.height / viewHeight; float thumbHeight = Mathf.Max(20f, rect.height * thumbRatio); float scrollPct = _colonistScrollPos.y / scrollableRange;
                float thumbY = rect.y + scrollPct * (rect.height - thumbHeight); Rect thumbRect = new Rect(rect.xMax - 6f, thumbY, 4f, thumbHeight);
                float colorBoost = _isDraggingColonistScrollbar ? 0.9f : 0.6f; GUI.color = new Color(colorBoost, colorBoost, colorBoost, _colonistScrollbarAlpha);
                GUI.DrawTexture(thumbRect, BaseContent.WhiteTex); GUI.color = Color.white;
            }
        }

        private void DrawTabs(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.9f)); float tabWidth = rect.width / 4f;
            if (DrawTabButton(new Rect(rect.x, rect.y, tabWidth, rect.height), "RTRS_Filter_1Hour".Translate(), _currentFilter == TimeFilter.PastHour)) { _currentFilter = TimeFilter.PastHour; ApplyTimeFilter(); }
            if (DrawTabButton(new Rect(rect.x + tabWidth, rect.y, tabWidth, rect.height), "RTRS_Filter_24Hours".Translate(), _currentFilter == TimeFilter.PastDay)) { _currentFilter = TimeFilter.PastDay; ApplyTimeFilter(); }
            if (DrawTabButton(new Rect(rect.x + tabWidth * 2, rect.y, tabWidth, rect.height), "RTRS_Filter_7Days".Translate(), _currentFilter == TimeFilter.PastWeek)) { _currentFilter = TimeFilter.PastWeek; ApplyTimeFilter(); }
            if (DrawTabButton(new Rect(rect.x + tabWidth * 3, rect.y, tabWidth, rect.height), "RTRS_Filter_All".Translate(), _currentFilter == TimeFilter.All)) { _currentFilter = TimeFilter.All; ApplyTimeFilter(); }
        }

        private bool DrawTabButton(Rect rect, string label, bool isActive)
        {
            if (isActive) { Widgets.DrawBoxSolid(rect, new Color(0f, 0.5f, 0.5f, 0.4f)); GUI.color = Color.cyan; Widgets.DrawBox(rect, 1); }
            else if (Mouse.IsOver(rect)) Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.1f));
            GUI.color = isActive ? Color.white : new Color(0.7f, 0.7f, 0.7f); Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(rect, label); Text.Anchor = TextAnchor.UpperLeft; GUI.color = Color.white;
            return Widgets.ButtonInvisible(rect);
        }

        private void DrawTimelineGrid(Rect rect, bool isMenuBlocking)
        {
            if (_groupedGallery.Count == 0) { Text.Anchor = TextAnchor.MiddleCenter; Widgets.Label(rect, "<color=#666666>" + "RTRS_Gallery_NoPhotos".Translate() + "</color>"); Text.Anchor = TextAnchor.UpperLeft; return; }

            int columns = 3; float spacing = 5f; float itemSize = (rect.width - (spacing * (columns - 1))) / columns; float totalHeight = 0f;
            foreach (var kvp in _groupedGallery) { totalHeight += 24f; int rows = Mathf.CeilToInt((float)kvp.Value.Count / columns); totalHeight += rows * (itemSize + spacing) + 10f; }

            float scrollableRange = Mathf.Max(0, totalHeight - rect.height); float trackWidth = 12f; Rect trackRect = new Rect(rect.xMax - trackWidth, rect.y, trackWidth, rect.height);
            bool isMouseOverTrack = trackRect.Contains(Event.current.mousePosition) && !isMenuBlocking;

            if (isMouseOverTrack || _isDraggingScrollbar || Mathf.Abs(_scrollPosition.y - _lastScrollPosY) > 0.1f) _lastScrollInteractionTime = Time.realtimeSinceStartup;
            _lastScrollPosY = _scrollPosition.y;

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && isMouseOverTrack) { _isDraggingScrollbar = true; Event.current.Use(); }
            if (Event.current.type == EventType.MouseUp && Event.current.button == 0) _isDraggingScrollbar = false;

            if (_isDraggingScrollbar && Event.current.type == EventType.MouseDrag)
            {
                float thumbHeight = Mathf.Max(20f, rect.height * (rect.height / totalHeight)); float thumbMovableRange = rect.height - thumbHeight;
                if (thumbMovableRange > 0) { float deltaScroll = Event.current.delta.y * (scrollableRange / thumbMovableRange); _scrollPosition.y += deltaScroll; _scrollPosition.y = Mathf.Clamp(_scrollPosition.y, 0f, scrollableRange); }
                Event.current.Use();
            }

            Rect viewRect = new Rect(0, 0, rect.width, totalHeight); Widgets.BeginScrollView(rect, ref _scrollPosition, viewRect, false);

            float visibleTop = _scrollPosition.y;
            float visibleBottom = _scrollPosition.y + rect.height;

            float currentY = 0f; int globalImageIndex = 0; List<string> flatListForViewer = new List<string>();
            foreach (var kvp in _groupedGallery.OrderByDescending(k => k.Key))
            {
                string dateHeader = kvp.Key.ToString("MM-dd"); Widgets.Label(new Rect(0, currentY, viewRect.width, 24f), $"<color=#CCCCCC><b>{dateHeader}</b></color>"); currentY += 24f;
                List<string> dailyPhotos = kvp.Value;
                for (int i = 0; i < dailyPhotos.Count; i++)
                {
                    string path = dailyPhotos[i]; flatListForViewer.Add(path);
                    int row = i / columns; int col = i % columns;

                    float itemYPos = currentY + row * (itemSize + spacing);
                    Rect itemRect = new Rect(col * (itemSize + spacing), itemYPos, itemSize, itemSize);

                    bool isVisible = (itemYPos + itemSize >= visibleTop) && (itemYPos <= visibleBottom);

                    Texture2D tex = null;
                    if (isVisible)
                    {
                        if (_textureCache.TryGetValue(path, out Texture2D cachedTex))
                        {
                            tex = cachedTex;
                        }
                        else if (_animProgress >= 1f && _texturesLoadedThisFrame < 2)
                        {
                            tex = GetOrLoadTexture(path);
                            _texturesLoadedThisFrame++;
                        }
                    }

                    if (tex != null) GUI.DrawTexture(itemRect, tex, ScaleMode.ScaleAndCrop);
                    else Widgets.DrawBoxSolid(itemRect, new Color(0.2f, 0.2f, 0.2f));

                    bool isSelected = _selectedImagePath == path;

                    if (isSelected) { GUI.color = Color.cyan; Widgets.DrawBox(itemRect, 2); GUI.color = Color.white; }
                    else if (Mouse.IsOver(itemRect) && !isMenuBlocking && !_isDraggingScrollbar) { Widgets.DrawHighlight(itemRect); TooltipHandler.TipRegion(itemRect, new FileInfo(path).CreationTime.ToString("HH:mm")); }

                    int cachedGlobalIndex = globalImageIndex;
                    if (Widgets.ButtonInvisible(itemRect) && !isMenuBlocking && !_isDraggingScrollbar)
                    {
                        if (isSelected) RimPhoneImageViewer.OpenViewer(flatListForViewer, cachedGlobalIndex);
                        else { _selectedImagePath = path; SoundDefOf.Tick_Tiny.PlayOneShotOnCamera(); }
                    }
                    globalImageIndex++;
                }
                int rowsForThisDate = Mathf.CeilToInt((float)dailyPhotos.Count / columns); currentY += rowsForThisDate * (itemSize + spacing) + 10f;
            }
            Widgets.EndScrollView();

            if (_scrollbarAlpha > 0.01f && totalHeight > rect.height)
            {
                float thumbRatio = rect.height / totalHeight; float thumbHeight = Mathf.Max(20f, rect.height * thumbRatio); float scrollPct = _scrollPosition.y / scrollableRange;
                float thumbY = rect.y + scrollPct * (rect.height - thumbHeight); Rect thumbRect = new Rect(rect.xMax - 6f, thumbY, 4f, thumbHeight);
                float colorBoost = _isDraggingScrollbar ? 0.9f : 0.6f; GUI.color = new Color(colorBoost, colorBoost, colorBoost, _scrollbarAlpha);
                GUI.DrawTexture(thumbRect, BaseContent.WhiteTex); GUI.color = Color.white;
            }
        }

        private Texture2D GetOrLoadTexture(string path)
        {
            if (_textureCache.TryGetValue(path, out Texture2D cachedTex)) return cachedTex;
            try
            {
                if (_textureCache.Count > 200)
                {
                    foreach (var texToDestroy in _textureCache.Values) UnityEngine.Object.Destroy(texToDestroy);
                    _textureCache.Clear();
                }
                byte[] fileData = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);
                _textureCache[path] = tex;
                return tex;
            }
            catch { return null; }
        }

        private static Texture2D CreateRoundedTexture(int width, int height, int radius, Color color)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false); Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isCorner = false; float dist = 0;
                    if (x < radius && y < radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)); isCorner = true; }
                    else if (x > width - radius && y < radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(width - radius, radius)); isCorner = true; }
                    else if (x < radius && y > height - radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(radius, height - radius)); isCorner = true; }
                    else if (x > width - radius && y > height - radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(width - radius, height - radius)); isCorner = true; }
                    if (isCorner && dist > radius) pixels[y * width + x] = Color.clear; else pixels[y * width + x] = color;
                }
            }
            tex.SetPixels(pixels); tex.Apply(); return tex;
        }
    }
}