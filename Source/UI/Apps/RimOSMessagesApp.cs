using RimTalk.Data;
using RimTalk.Service;
using RimTalk.Source.Data;
using RimTalkRealitySync.Platforms.Discord;
using RimTalkRealitySync.Sync;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
using Color = UnityEngine.Color;
using Text = Verse.Text;

namespace RimTalkRealitySync.UI.Apps
{
    /// <summary>
    /// Lightweight UI Window for processing Inbox Messages.
    /// LLM Generation logic has been decoupled to RimPhoneChatProcessor.
    /// </summary>
    public class RimOSMessagesApp : Window
    {
        public override Vector2 InitialSize => new Vector2(380f, 650f);
        protected override float Margin => 0f;

        private bool _isClosing = false;
        public bool IsClosing => _isClosing;

        private float _animProgress = 0f;
        private readonly float _animSpeed = 4.0f;

        private float _splashFadeProgress = 0f;
        private float _returnFadeProgress = 0f;
        private bool _isReturningToHome = false;

        private int _closeDelayFrames = -1;
        private int _transitionDelayFrames = 0;

        private static Texture2D _roundedBackgroundTex;
        private static Texture2D _messagesIconTex;

        private Vector2 _scrollPosition;
        private bool _selectAll = false;

        private float _scrollbarAlpha = 0f;
        private float _lastScrollInteractionTime = 0f;
        private float _lastScrollPosY = 0f;
        private bool _isDraggingScrollbar = false;

        private DiscordMessage _viewingMessage = null;
        private float _viewAnimProgress = 0f;
        private bool _isClosingView = false;

        // =====================================================================
        // NEW: Inline Edit State for the View Overlay
        // =====================================================================
        private string _tempEditContent = "";

        private bool _showDeleteConfirm = false;

        private bool _isSettingsRevealed = false;
        private float _settingsAnimProgress = 0f;
        private readonly float _settingsAnimSpeed = 8.0f;
        private float _lastSettingsInteractionTime = 0f;
        private readonly float _autoHideDelaySeconds = 3.0f;

        public RimOSMessagesApp(bool skipSlideAnimation = false)
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

            Platforms.Discord.DiscordFetcher.SyncDiscordMessagesAsync(true);
        }

        public override void PreOpen()
        {
            base.PreOpen();
            if (_animProgress < 1f) RimWorld.SoundDefOf.Tick_High.PlayOneShotOnCamera();

            if (_roundedBackgroundTex == null) _roundedBackgroundTex = CreateRoundedTexture(380, 650, 20, new Color(0.1f, 0.12f, 0.15f, 1f));

            // FIXED: Load the real HD texture instead of creating a placeholder block
            if (_messagesIconTex == null) _messagesIconTex = ContentFinder<Texture2D>.Get("UI/RimPhone/Icon_Messages", true);
        }

        protected override void SetInitialSizeAndPosition()
        {
            float startY = Verse.UI.screenHeight + 10f;
            float targetX = Verse.UI.screenWidth - InitialSize.x - 20f;
            windowRect = new Rect(targetX, startY, InitialSize.x, InitialSize.y);
        }

        public void SlideDownAndClose() { if (!_isClosing) { _isClosing = true; RimWorld.SoundDefOf.Tick_Low.PlayOneShotOnCamera(); } }
        public void CloseInstantly() { base.Close(false); }
        public override void Close(bool doCloseSound = true) { SlideDownAndClose(); }

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

            float targetY = Verse.UI.screenHeight - InitialSize.y - 20f;
            float offScreenY = Verse.UI.screenHeight + 10f;

            if (_isClosing) { _animProgress -= Time.deltaTime * _animSpeed; if (_animProgress <= 0f) base.Close(false); }
            else { _animProgress += Time.deltaTime * _animSpeed; if (_animProgress > 1f) _animProgress = 1f; }

            float ease = Mathf.SmoothStep(0f, 1f, _animProgress);
            windowRect.y = Mathf.Lerp(offScreenY, targetY, ease);

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
                    Find.WindowStack.Add(new RimOSHomeScreen(skipSlideAnimation: true, returningFromApp: "Messages"));
                    _closeDelayFrames = 2;
                }
            }

            if (_isSettingsRevealed)
            {
                _settingsAnimProgress += Time.deltaTime * _settingsAnimSpeed;
                if (_settingsAnimProgress > 1f) _settingsAnimProgress = 1f;
                if (Time.realtimeSinceStartup - _lastSettingsInteractionTime > _autoHideDelaySeconds) _isSettingsRevealed = false;
            }
            else
            {
                _settingsAnimProgress -= Time.deltaTime * _settingsAnimSpeed;
                if (_settingsAnimProgress < 0f) _settingsAnimProgress = 0f;
            }

            float targetAlpha = (Time.realtimeSinceStartup - _lastScrollInteractionTime < 1.5f) ? 1f : 0f;
            _scrollbarAlpha = Mathf.Lerp(_scrollbarAlpha, targetAlpha, Time.deltaTime * 8f);

            foreach (var msg in DiscordNetworkService.Messages)
            {
                float target = msg.IsExpanded ? 1f : 0f;
                msg.AnimProgress = Mathf.MoveTowards(msg.AnimProgress, target, Time.deltaTime * 6f);
            }

            if (_viewingMessage != null)
            {
                if (_isClosingView)
                {
                    _viewAnimProgress -= Time.deltaTime * 6f;
                    if (_viewAnimProgress <= 0f)
                    {
                        _viewAnimProgress = 0f;
                        _viewingMessage.IsExpanded = false;
                        _viewingMessage.AnimProgress = 0f;
                        _viewingMessage = null;
                        _isClosingView = false;
                    }
                }
                else
                {
                    _viewAnimProgress += Time.deltaTime * 6f;
                    if (_viewAnimProgress > 1f) _viewAnimProgress = 1f;
                }
            }
        }

        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            if (Event.current.type == EventType.MouseDown)
            {
                if (!windowRect.Contains(Event.current.mousePosition))
                {
                    GUI.FocusControl(null); Verse.UI.UnfocusCurrentControl();
                }
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            GUI.DrawTexture(inRect, _roundedBackgroundTex, ScaleMode.StretchToFill, true);

            Rect contentRect = inRect.ContractedBy(12f);
            float headerHeight = 35f; float bottomBarHeight = 45f;

            float maxDrawerHeight = 40f;
            float currentDrawerHeight = Mathf.SmoothStep(0f, 1f, _settingsAnimProgress) * maxDrawerHeight;

            Rect topInteractionArea = new Rect(contentRect.x, contentRect.y, contentRect.width, headerHeight + (_settingsAnimProgress * maxDrawerHeight));
            if (topInteractionArea.Contains(Event.current.mousePosition)) _lastSettingsInteractionTime = Time.realtimeSinceStartup;

            Rect headerRect = new Rect(contentRect.x, contentRect.y, contentRect.width, headerHeight);

            float scrollStartY = headerRect.yMax + 5f + currentDrawerHeight + (currentDrawerHeight > 0.1f ? 5f : 0f);
            Rect scrollRect = new Rect(contentRect.x, scrollStartY, contentRect.width, contentRect.height - (scrollStartY - contentRect.y) - bottomBarHeight - 10f);
            Rect bottomBarRect = new Rect(contentRect.x, scrollRect.yMax + 5f, contentRect.width, bottomBarHeight);

            DrawMessageList(scrollRect);
            DrawFloatingHeader(headerRect);

            if (currentDrawerHeight > 0.1f)
            {
                Rect drawerRect = new Rect(contentRect.x, headerRect.yMax + 5f, contentRect.width, currentDrawerHeight);
                GUI.BeginGroup(drawerRect);
                DrawSettingsDrawer(new Rect(0, currentDrawerHeight - maxDrawerHeight, contentRect.width, maxDrawerHeight));
                GUI.EndGroup();
            }

            DrawBottomBar(bottomBarRect);

            if (_showDeleteConfirm) DrawDeleteConfirmationOverlay(inRect);
            if (_viewAnimProgress > 0.01f) DrawFullscreenViewOverlay(inRect);

            float overlayAlpha = Mathf.Max(_splashFadeProgress, _returnFadeProgress);
            if (overlayAlpha > 0.01f)
            {
                // FIXED: Re-added centerIconRect definition to resolve CS0103 error
                float centerIconSize = 110f;
                Rect centerIconRect = new Rect(inRect.center.x - centerIconSize / 2f, inRect.center.y - centerIconSize / 2f, centerIconSize, centerIconSize);

                GUI.color = new Color(1f, 1f, 1f, overlayAlpha);
                if (_messagesIconTex != null) GUI.DrawTexture(centerIconRect, _messagesIconTex);

                // FIXED: Removed the hardcoded 'M' letter rendering
                GUI.color = Color.white;
            }
        }

        private void DrawFloatingHeader(Rect headerRect)
        {
            Widgets.DrawBoxSolid(headerRect, new Color(0.15f, 0.18f, 0.22f, 0.95f));

            Rect selectAllRect = new Rect(headerRect.x + 5f, headerRect.y + 6f, 24f, 24f);
            bool prevSelectAll = _selectAll;
            Widgets.Checkbox(selectAllRect.x, selectAllRect.y, ref _selectAll);

            if (_viewingMessage != null) _selectAll = prevSelectAll;
            else if (_selectAll != prevSelectAll) { foreach (var msg in DiscordNetworkService.Messages) msg.IsSelected = _selectAll; }

            Text.Font = GameFont.Medium; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(headerRect, "<b><color=#00FFAA>" + "RTRS_MsgApp_Inbox".Translate() + "</color></b>");
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft;

            Rect filterBtnRect = new Rect(headerRect.xMax - 30f, headerRect.y + 8f, 30f, 20f);
            if (Widgets.ButtonText(filterBtnRect, "≡", drawBackground: false))
            {
                _isSettingsRevealed = !_isSettingsRevealed;
                if (_isSettingsRevealed) _lastSettingsInteractionTime = Time.realtimeSinceStartup;
            }
        }

        private void DrawSettingsDrawer(Rect rect)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.9f));

            float halfWidth = rect.width / 2f;

            Rect autoSendRect = new Rect(rect.x + 10f, rect.y + 8f, halfWidth - 10f, 24f);
            Widgets.CheckboxLabeled(autoSendRect, "RTRS_MsgApp_AutoSend".Translate(), ref DiscordNetworkService.AutoSendEnabled, placeCheckboxNearText: true);

            Rect syncBtnRect = new Rect(rect.xMax - 110f, rect.y + 8f, 100f, 24f);
            if (Platforms.Discord.DiscordFetcher.IsFetching)
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                int dots = Mathf.FloorToInt(Time.realtimeSinceStartup * 4f) % 4;
                Widgets.Label(syncBtnRect, "RTRS_MsgApp_Syncing".Translate() + new string('.', dots));
                GUI.color = Color.white;
            }
            else
            {
                if (Widgets.ButtonText(syncBtnRect, "RTRS_MsgApp_SyncBtn".Translate(), drawBackground: true))
                    Platforms.Discord.DiscordFetcher.SyncDiscordMessagesAsync(true);
            }
        }

        private void DrawMessageList(Rect scrollRect)
        {
            float baseItemHeight = 30f; float expandedExtraHeight = 100f; float totalHeight = 0f;
            foreach (var msg in DiscordNetworkService.Messages) totalHeight += baseItemHeight + (expandedExtraHeight * Mathf.SmoothStep(0f, 1f, msg.AnimProgress)) + 5f;

            if (DiscordNetworkService.Messages.Count == 0 && !Platforms.Discord.DiscordFetcher.IsFetching)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(scrollRect, "<color=#666666>" + "RTRS_MsgApp_NoMessages".Translate() + "</color>");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float scrollableRange = Mathf.Max(0, totalHeight - scrollRect.height);
            float trackWidth = 12f;
            Rect trackRect = new Rect(scrollRect.xMax - trackWidth, scrollRect.y, trackWidth, scrollRect.height);

            bool isMouseOverTrack = trackRect.Contains(Event.current.mousePosition) && _viewingMessage == null;

            if (isMouseOverTrack || _isDraggingScrollbar || Mathf.Abs(_scrollPosition.y - _lastScrollPosY) > 0.1f)
                _lastScrollInteractionTime = Time.realtimeSinceStartup;
            _lastScrollPosY = _scrollPosition.y;

            if (Event.current.rawType == EventType.MouseDown && Event.current.button == 0 && isMouseOverTrack) { _isDraggingScrollbar = true; Event.current.Use(); }
            if (Event.current.rawType == EventType.MouseUp && Event.current.button == 0) _isDraggingScrollbar = false;

            if (_isDraggingScrollbar && Event.current.type == EventType.MouseDrag)
            {
                float thumbHeight = Mathf.Max(20f, scrollRect.height * (scrollRect.height / totalHeight));
                float thumbMovableRange = scrollRect.height - thumbHeight;
                if (thumbMovableRange > 0)
                {
                    float deltaScroll = Event.current.delta.y * (scrollableRange / thumbMovableRange);
                    _scrollPosition.y += deltaScroll; _scrollPosition.y = Mathf.Clamp(_scrollPosition.y, 0f, scrollableRange);
                }
                Event.current.Use();
            }

            Rect viewRect = new Rect(0, 0, scrollRect.width, totalHeight);
            Widgets.BeginScrollView(scrollRect, ref _scrollPosition, viewRect, false);

            float currentY = 0f;
            foreach (var msg in DiscordNetworkService.Messages)
            {
                float currentItemHeight = baseItemHeight + (expandedExtraHeight * Mathf.SmoothStep(0f, 1f, msg.AnimProgress));
                Rect itemRect = new Rect(0, currentY, viewRect.width, currentItemHeight);

                Widgets.DrawBoxSolid(itemRect, new Color(0.2f, 0.2f, 0.2f, 0.5f));

                Rect cbRect = new Rect(itemRect.x + 5f, itemRect.y + 3f, 24f, 24f);
                bool prevCb = msg.IsSelected;
                Widgets.Checkbox(cbRect.x, cbRect.y, ref msg.IsSelected);
                if (_viewingMessage != null) msg.IsSelected = prevCb;

                Rect titleRect = new Rect(cbRect.xMax + 10f, itemRect.y + 5f, itemRect.width - cbRect.xMax - 15f, 20f);
                Widgets.Label(titleRect, $"<b>{msg.SenderName}</b> <color=#888888>@ {msg.TargetPawn}</color>");

                if (_viewingMessage == null && !_showDeleteConfirm && !_isDraggingScrollbar && Widgets.ButtonInvisible(titleRect))
                {
                    msg.IsExpanded = !msg.IsExpanded;
                    if (msg.IsExpanded) msg.LastHoverTime = Time.realtimeSinceStartup;
                    RimWorld.SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                }

                if (msg.IsExpanded)
                {
                    if (itemRect.Contains(Event.current.mousePosition) || _viewingMessage == msg)
                        msg.LastHoverTime = Time.realtimeSinceStartup;
                    else if (Time.realtimeSinceStartup - msg.LastHoverTime > 2.5f)
                        msg.IsExpanded = false;
                }

                if (msg.AnimProgress > 0.01f)
                {
                    float contentAlpha = Mathf.SmoothStep(0f, 1f, msg.AnimProgress);
                    GUI.color = new Color(1f, 1f, 1f, contentAlpha);

                    Rect contentArea = new Rect(itemRect.x + 10f, itemRect.y + baseItemHeight + 5f, itemRect.width - 20f, expandedExtraHeight - 35f);

                    string strippedContent = System.Text.RegularExpressions.Regex.Replace(msg.Content, @"<RIMPHONE_LOCAL_IMG:[^>]+>", "").Trim();
                    string localizedImgTag = "[" + "RTRS_Tag_AttachedImage".Translate().ToString().Replace("[", "").Replace("]", "") + ": ";
                    string displayText = msg.Content.Replace("<RIMPHONE_LOCAL_IMG:", localizedImgTag).Replace(">", "]");

                    if (string.IsNullOrEmpty(strippedContent))
                    {
                        displayText = "RTRS_Tag_SentAnImage".Translate() + "\n" + displayText;
                    }

                    string previewText = displayText.Length > 150 ? displayText.Substring(0, 150) + "..." : displayText;
                    Widgets.Label(contentArea, $"<color=#CCCCCC>{previewText}</color>");

                    Rect readBtnRect = new Rect(itemRect.width - 70f, itemRect.yMax - 30f, 60f, 24f);

                    // =====================================================================
                    // FIXED: Button directly opens Edit Mode, skipping the Read UI.
                    // =====================================================================
                    if (Widgets.ButtonText(readBtnRect, "RTRS_Btn_Edit".Translate()) && _viewingMessage == null && !_isDraggingScrollbar)
                    {
                        GUI.FocusControl(null);
                        _viewingMessage = msg;
                        _tempEditContent = msg.Content; // Load the text immediately for editing
                        _viewAnimProgress = 0f;
                        _isClosingView = false;
                    }
                    GUI.color = Color.white;
                }

                currentY += currentItemHeight + 5f;
            }
            Widgets.EndScrollView();

            if (_scrollbarAlpha > 0.01f && totalHeight > scrollRect.height)
            {
                float thumbRatio = scrollRect.height / totalHeight;
                float thumbHeight = Mathf.Max(20f, scrollRect.height * thumbRatio);
                float scrollPct = _scrollPosition.y / scrollableRange;

                float thumbY = scrollRect.y + scrollPct * (scrollRect.height - thumbHeight);

                Rect thumbRect = new Rect(scrollRect.xMax - 6f, thumbY, 4f, thumbHeight);
                float colorBoost = _isDraggingScrollbar ? 0.9f : 0.6f;
                GUI.color = new Color(colorBoost, colorBoost, colorBoost, _scrollbarAlpha);
                GUI.DrawTexture(thumbRect, BaseContent.WhiteTex);
                GUI.color = Color.white;
            }
        }

        private void DrawBottomBar(Rect bottomBarRect)
        {
            Rect homeBtnRect = new Rect(bottomBarRect.x, bottomBarRect.y, 80f, bottomBarRect.height);
            GUI.color = new Color(0.2f, 0.6f, 0.6f); Widgets.DrawBoxSolid(homeBtnRect, GUI.color); GUI.color = Color.white;

            if (_viewingMessage == null && !_showDeleteConfirm && Widgets.ButtonText(homeBtnRect, "RTRS_Btn_Home".Translate(), drawBackground: false))
            {
                _isReturningToHome = true;
                _returnFadeProgress = 0f;
                RimWorld.SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }

            Rect deleteBtnRect = new Rect(homeBtnRect.xMax + 10f, bottomBarRect.y, 80f, bottomBarRect.height);
            GUI.color = new Color(0.8f, 0.2f, 0.2f); Widgets.DrawBoxSolid(deleteBtnRect, GUI.color); GUI.color = Color.white;
            if (_viewingMessage == null && !_showDeleteConfirm && Widgets.ButtonText(deleteBtnRect, "RTRS_Btn_Delete".Translate(), drawBackground: false))
            {
                if (DiscordNetworkService.Messages.Any(m => m.IsSelected)) _showDeleteConfirm = true;
                else Verse.Messages.Message("RTRS_MsgApp_NoMessagesSelected".Translate(), MessageTypeDefOf.RejectInput, false);
            }

            Rect sendBtnRect = new Rect(deleteBtnRect.xMax + 10f, bottomBarRect.y, bottomBarRect.width - deleteBtnRect.xMax - 10f, bottomBarRect.height);
            GUI.color = new Color(0.2f, 0.8f, 0.2f); Widgets.DrawBoxSolid(sendBtnRect, GUI.color); GUI.color = Color.white;

            if (_viewingMessage == null && !_showDeleteConfirm && Widgets.ButtonText(sendBtnRect, "RTRS_Btn_SendSelected".Translate(), drawBackground: false))
            {
                var selected = DiscordNetworkService.Messages.Where(m => m.IsSelected).ToList();
                if (selected.Any())
                {
                    int sentCount = 0;
                    foreach (var msg in selected)
                    {
                        try
                        {
                            Pawn targetPawn = PawnsFinder.AllMaps_FreeColonists.FirstOrDefault(p => p.Name.ToStringShort == msg.TargetPawn);
                            if (targetPawn == null) targetPawn = PawnsFinder.AllMaps_FreeColonists.FirstOrDefault();

                            // NEW: Delegate LLM Call to Unified Central Brain
                            RimPhoneChatProcessor.InjectMessageIntoRimTalk(targetPawn, msg);

                            sentCount++;
                            msg.IsSelected = false;
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"[RimPhone] Error routing manual message. Detail: {ex.Message}");
                        }
                    }

                    DiscordNetworkService.Messages.RemoveAll(m => !m.IsSelected && selected.Contains(m));
                    _selectAll = false;
                    RimWorld.SoundDefOf.Tick_High.PlayOneShotOnCamera();
                }
                else
                {
                    Verse.Messages.Message("RTRS_MsgApp_NoMessagesSelected".Translate(), MessageTypeDefOf.RejectInput, false);
                }
            }
        }

        private void DrawDeleteConfirmationOverlay(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, new Color(0f, 0f, 0f, 0.8f));

            Rect popupRect = new Rect(inRect.center.x - 125f, inRect.center.y - 75f, 250f, 150f);
            Widgets.DrawBoxSolid(popupRect, new Color(0.15f, 0.15f, 0.15f, 1f));
            GUI.color = Color.red; Widgets.DrawBox(popupRect, 2); GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(popupRect.x, popupRect.y + 20f, popupRect.width, 30f), "RTRS_MsgApp_DeleteSelected".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(popupRect.x, popupRect.y + 60f, popupRect.width, 30f), "<color=#AAAAAA>" + "RTRS_MsgApp_CannotBeUndone".Translate() + "</color>");
            Text.Anchor = TextAnchor.UpperLeft;

            Rect cancelRect = new Rect(popupRect.x + 20f, popupRect.yMax - 40f, 90f, 30f);
            Rect confirmRect = new Rect(popupRect.xMax - 110f, popupRect.yMax - 40f, 90f, 30f);

            GUI.color = new Color(0.3f, 0.3f, 0.3f); Widgets.DrawBoxSolid(cancelRect, GUI.color); GUI.color = Color.white;
            if (Widgets.ButtonText(cancelRect, "RTRS_Btn_Cancel".Translate(), drawBackground: false)) _showDeleteConfirm = false;

            GUI.color = new Color(0.8f, 0.2f, 0.2f); Widgets.DrawBoxSolid(confirmRect, GUI.color); GUI.color = Color.white;
            if (Widgets.ButtonText(confirmRect, "RTRS_Btn_Confirm".Translate(), drawBackground: false))
            {
                DiscordNetworkService.Messages.RemoveAll(m => m.IsSelected); _selectAll = false; _showDeleteConfirm = false;
                RimWorld.SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }

        private void DrawFullscreenViewOverlay(Rect inRect)
        {
            float ease = Mathf.SmoothStep(0f, 1f, _viewAnimProgress);

            Rect overlayRect = new Rect(
                Mathf.Lerp(inRect.center.x, inRect.x, ease),
                Mathf.Lerp(inRect.center.y, inRect.y, ease),
                Mathf.Lerp(0f, inRect.width, ease),
                Mathf.Lerp(0f, inRect.height, ease)
            );

            Widgets.DrawBoxSolid(overlayRect, new Color(0.1f, 0.12f, 0.15f, 1f));

            if (_viewAnimProgress > 0.95f && !_isClosingView)
            {
                Rect contentRect = inRect.ContractedBy(15f);

                // =====================================================================
                // DIRECT INLINE EDIT MODE (Skipping Read Mode)
                // =====================================================================
                Text.Font = GameFont.Medium;
                Widgets.Label(new Rect(contentRect.x, contentRect.y, contentRect.width, 30f), "RTRS_MsgApp_EditMessageTitle".Translate($"<color=#00FFAA>{_viewingMessage.SenderName}</color>"));
                Text.Font = GameFont.Small;

                Rect textFieldRect = new Rect(contentRect.x, contentRect.y + 40f, contentRect.width, contentRect.height - 100f);

                float btnWidth = (contentRect.width - 10f) / 2f;
                Rect cancelRect = new Rect(contentRect.x, contentRect.yMax - 40f, btnWidth, 40f);
                Rect confirmRect = new Rect(cancelRect.xMax + 10f, contentRect.yMax - 40f, btnWidth, 40f);

                // Buttons FIRST to intercept clicks safely
                GUI.color = new Color(0.6f, 0.2f, 0.2f); Widgets.DrawBoxSolid(cancelRect, GUI.color); GUI.color = Color.white;
                if (Widgets.ButtonText(cancelRect, "RTRS_Btn_Cancel".Translate(), drawBackground: false))
                {
                    GUI.FocusControl(null);
                    _isClosingView = true; // Trigger close animation directly
                    RimWorld.SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }

                GUI.color = new Color(0.2f, 0.8f, 0.2f); Widgets.DrawBoxSolid(confirmRect, GUI.color); GUI.color = Color.white;
                if (Widgets.ButtonText(confirmRect, "RTRS_Btn_Confirm".Translate(), drawBackground: false))
                {
                    GUI.FocusControl(null);
                    _viewingMessage.Content = _tempEditContent; // Save edits
                    _isClosingView = true; // Trigger close animation directly
                    RimWorld.SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                }

                // TextArea LAST to prevent it from stealing input focus from the buttons
                _tempEditContent = Widgets.TextArea(textFieldRect, _tempEditContent);
            }
        }

        private static Texture2D CreateRoundedTexture(int width, int height, int radius, Color color)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool isCorner = false; float dist = 0;
                    if (x < radius && y < radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(radius, radius)); isCorner = true; }
                    else if (x > width - radius && y < radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(width - radius, radius)); isCorner = true; }
                    else if (x < radius && y > height - radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(radius, height - radius)); isCorner = true; }
                    else if (x > width - radius && y > height - radius) { dist = Vector2.Distance(new Vector2(x, y), new Vector2(width - radius, height - radius)); isCorner = true; }
                    if (isCorner && dist > radius) pixels[y * width + x] = Color.clear;
                    else pixels[y * width + x] = color;
                }
            }
            tex.SetPixels(pixels); tex.Apply(); return tex;
        }
    }
}