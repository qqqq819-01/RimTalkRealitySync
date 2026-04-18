using RimWorld;
using System;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimTalkRealitySync.UI.Apps
{
    /// <summary>
    /// The core Desktop OS for RimPhone. 
    /// Handles the grid of apps, real-world time widgets, and modern smooth iOS-style splash/return animations.
    /// Features NEW 2-frame seamless handoff engine to completely eliminate flickering.
    /// </summary>
    public class RimOSHomeScreen : Window
    {
        public override Vector2 InitialSize => new Vector2(380f, 650f);
        protected override float Margin => 0f;

        private bool _isClosing = false;
        public bool IsClosing => _isClosing;

        private float _animProgress = 0f;
        private readonly float _animSpeed = 4.0f;

        // App Transition Animation State
        private bool _isLaunching = false;
        private bool _isReturning = false;
        private string _activeAnimApp = null;
        private float _transitionProgress = 0f;
        private readonly float _transitionSpeed = 5.0f;

        // Anti-Flicker sync frames
        private int _closeDelayFrames = -1;
        private int _transitionDelayFrames = 0;

        private static Texture2D _roundedBackgroundTex;
        private static Texture2D _galleryIconTex;
        private static Texture2D _messagesIconTex;
        private static Texture2D _settingsIconTex;

        // Opacity 1.0f backgrounds to prevent alpha-blending color shifts during handoff
        private static Texture2D _galleryBgTex;
        private static Texture2D _messagesBgTex;

        private readonly Rect _centerIconRect = new Rect(135f, 270f, 110f, 110f);

        public RimOSHomeScreen(bool skipSlideAnimation = false, string returningFromApp = null)
        {
            this.doCloseX = false;
            this.doCloseButton = false;
            this.draggable = false;
            this.resizeable = false;
            this.preventCameraMotion = false;
            this.layer = WindowLayer.Dialog;
            this.doWindowBackground = false;
            this.drawShadow = false;

            _animProgress = skipSlideAnimation ? 1f : 0f;

            if (returningFromApp != null)
            {
                _isReturning = true;
                _activeAnimApp = returningFromApp;
                _transitionProgress = 0f;
                _transitionDelayFrames = 2; // Wait 2 frames to let the previous app safely close/hide
            }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            if (_animProgress < 1f) RimWorld.SoundDefOf.Tick_High.PlayOneShotOnCamera();

            if (_roundedBackgroundTex == null) _roundedBackgroundTex = CreateRoundedTexture(380, 650, 20, new Color(0.08f, 0.12f, 0.18f, 1f));

            //placeholder
            //if (_galleryIconTex == null) _galleryIconTex = CreateRoundedTexture(100, 100, 22, new Color(0.95f, 0.95f, 0.95f));
            //if (_messagesIconTex == null) _messagesIconTex = CreateRoundedTexture(100, 100, 22, new Color(0.2f, 0.8f, 0.4f));
            //if (_settingsIconTex == null) _settingsIconTex = CreateRoundedTexture(100, 100, 22, new Color(0.4f, 0.4f, 0.4f));

            //picture
            if (_galleryIconTex == null) _galleryIconTex = ContentFinder<Texture2D>.Get("UI/RimPhone/Icon_Gallery", true);
            if (_messagesIconTex == null) _messagesIconTex = ContentFinder<Texture2D>.Get("UI/RimPhone/Icon_Messages", true);
            if (_settingsIconTex == null) _settingsIconTex = ContentFinder<Texture2D>.Get("UI/RimPhone/Icon_Settings", true);

            if (_galleryBgTex == null) _galleryBgTex = CreateRoundedTexture(380, 650, 20, new Color(0.12f, 0.12f, 0.12f, 1f));
            if (_messagesBgTex == null) _messagesBgTex = CreateRoundedTexture(380, 650, 20, new Color(0.1f, 0.12f, 0.15f, 1f));
        }

        protected override void SetInitialSizeAndPosition()
        {
            float startY = Verse.UI.screenHeight + 10f;
            float targetX = Verse.UI.screenWidth - InitialSize.x - 20f;
            windowRect = new Rect(targetX, startY, InitialSize.x, InitialSize.y);
        }

        public override void WindowUpdate()
        {
            // Ultimate Anti-Flicker Protocol
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

            if (_isClosing)
            {
                _animProgress -= Time.deltaTime * _animSpeed;
                if (_animProgress <= 0f) base.Close(false);
            }
            else
            {
                _animProgress += Time.deltaTime * _animSpeed;
                if (_animProgress > 1f) _animProgress = 1f;
            }

            float ease = Mathf.SmoothStep(0f, 1f, _animProgress);
            windowRect.y = Mathf.Lerp(offScreenY, targetY, ease);

            if (_isLaunching)
            {
                _transitionProgress += Time.deltaTime * _transitionSpeed;
                if (_transitionProgress >= 1f)
                {
                    _transitionProgress = 1f;
                    _isLaunching = false;
                    RimPhoneStateManager.CurrentApp = _activeAnimApp;

                    if (_activeAnimApp == "Gallery")
                        Find.WindowStack.Add(new RimOSGalleryApp(skipSlideAnimation: true));
                    else if (_activeAnimApp == "Messages")
                        Find.WindowStack.Add(new RimOSMessagesApp(skipSlideAnimation: true));

                    // Safely delay the close so the new window has time to draw its first frame
                    _closeDelayFrames = 2;
                }
            }
            else if (_isReturning)
            {
                if (_transitionDelayFrames > 0) _transitionDelayFrames--;
                else
                {
                    _transitionProgress += Time.deltaTime * _transitionSpeed;
                    if (_transitionProgress >= 1f)
                    {
                        _transitionProgress = 1f;
                        _isReturning = false;
                        _activeAnimApp = null;
                    }
                }
            }
        }

        public void SlideDownAndClose()
        {
            if (!_isClosing)
            {
                _isClosing = true;
                RimWorld.SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }

        public void CloseInstantly() { base.Close(false); }
        public override void Close(bool doCloseSound = true) { SlideDownAndClose(); }

        public override void DoWindowContents(Rect inRect)
        {
            GUI.DrawTexture(inRect, _roundedBackgroundTex, ScaleMode.StretchToFill, true);

            float ease = Mathf.SmoothStep(0f, 1f, _transitionProgress);
            float appBgAlpha = 0f;
            float iconsAlpha = 1f;

            if (_isLaunching) { appBgAlpha = ease; iconsAlpha = 1f - ease; }
            else if (_isReturning) { appBgAlpha = 1f - ease; iconsAlpha = ease; }

            GUI.color = new Color(1f, 1f, 1f, iconsAlpha);

            // FIXED: GUI State Leakage Shield
            Text.Font = GameFont.Tiny;

            // =====================================================================
            // Fetch dynamic weather data from RealWorldProvider directly!
            // =====================================================================
            string currentTemp = RealWorldProvider.GetRealTemperature();
            string currentCondition = RealWorldProvider.GetRealWeather();
            Widgets.Label(new Rect(20, 10, 200, 20), $"<b><color=#00FFFF>{currentTemp} | {currentCondition}</color></b>");

            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(inRect.width - 200, 10, 180, 20), "<b>" + DateTime.Now.ToString("HH:mm") + "</b>");

            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            Rect clockWidgetRect = new Rect(0, 60f, inRect.width, 80f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Medium;
            Widgets.Label(clockWidgetRect, $"<size=50><b>{DateTime.Now.ToString("HH:mm")}</b></size>\n<color=#CCCCCC><size=16>{DateTime.Now.ToString("MM-dd dddd")}</size></color>");
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;

            if (iconsAlpha > 0.01f)
            {
                // Note: Keep internal string names for DrawAppIcon/GetIconRect logic, only translate the Label.
                if (_activeAnimApp != "Gallery") DrawAppIcon("Gallery", GetIconRect("Gallery"), iconsAlpha);
                if (_activeAnimApp != "Messages") DrawAppIcon("Messages", GetIconRect("Messages"), iconsAlpha);
                DrawAppIcon("Settings", GetIconRect("Settings"), iconsAlpha);

                // TRANSLATION HOOKS for Labels
                DrawAppLabel("RTRS_App_Gallery".Translate(), GetIconRect("Gallery"), iconsAlpha);
                DrawAppLabel("RTRS_App_Messages".Translate(), GetIconRect("Messages"), iconsAlpha);
                DrawAppLabel("RTRS_App_Settings".Translate(), GetIconRect("Settings"), iconsAlpha);
            }

            if (appBgAlpha > 0.01f && (_activeAnimApp == "Gallery" || _activeAnimApp == "Messages"))
            {
                GUI.color = new Color(1f, 1f, 1f, appBgAlpha);
                Texture2D targetBg = _activeAnimApp == "Gallery" ? _galleryBgTex : _messagesBgTex;
                if (targetBg != null) GUI.DrawTexture(inRect, targetBg, ScaleMode.StretchToFill, true);
                GUI.color = Color.white;
            }

            if ((_isLaunching || _isReturning) && _activeAnimApp != null && _activeAnimApp != "Settings")
            {
                Rect startRect = GetIconRect(_activeAnimApp);
                Rect currentRect = _isLaunching
                    ? LerpRect(startRect, _centerIconRect, ease)
                    : LerpRect(_centerIconRect, startRect, ease);

                DrawAppIcon(_activeAnimApp, currentRect, 1f);
            }
            else if (!_isLaunching && !_isReturning)
            {
                DrawAppIcon("Gallery", GetIconRect("Gallery"), 1f);
                if (Widgets.ButtonInvisible(GetIconRect("Gallery"))) StartAppLaunch("Gallery");

                DrawAppIcon("Messages", GetIconRect("Messages"), 1f);
                if (Widgets.ButtonInvisible(GetIconRect("Messages"))) StartAppLaunch("Messages");

                DrawAppIcon("Settings", GetIconRect("Settings"), 1f);
                if (Widgets.ButtonInvisible(GetIconRect("Settings")))
                {
                    RimWorld.SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                    // Note: Settings window is defined in the Root namespace
                    Find.WindowStack.Add(new RealitySyncSettingsFloatingWindow());
                }
            }
        }

        private void StartAppLaunch(string appName)
        {
            _isLaunching = true;
            _activeAnimApp = appName;
            _transitionProgress = 0f;
            RimWorld.SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
        }

        private Rect GetIconRect(string appName)
        {
            float startY = 160f;
            if (appName == "Gallery") return new Rect(35f, startY, 64f, 64f);
            if (appName == "Messages") return new Rect(124f, startY, 64f, 64f);
            if (appName == "Settings") return new Rect(213f, startY, 64f, 64f);
            return new Rect(35f, startY, 64f, 64f);
        }

        private void DrawAppIcon(string appName, Rect rect, float alpha)
        {
            // FIXED: Removed hardcoded letters (G, M, S) and font rendering logic.
            // Now it only renders the clean Texture2D images.
            Texture2D iconTex = null;
            if (appName == "Gallery") iconTex = _galleryIconTex;
            else if (appName == "Messages") iconTex = _messagesIconTex;
            else if (appName == "Settings") iconTex = _settingsIconTex;

            GUI.color = new Color(1f, 1f, 1f, alpha);
            if (iconTex != null) GUI.DrawTexture(rect, iconTex);
            GUI.color = Color.white;
        }

        private void DrawAppLabel(string appName, Rect iconRect, float alpha)
        {
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Text.Font = GameFont.Tiny; // FIXED: Use Tiny font to prevent long text truncation

            // FIXED: Widen the bounding box to ensure multi-language text fits
            Rect labelRect = new Rect(iconRect.x - 25f, iconRect.yMax + 5f, iconRect.width + 50f, 24f);

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(labelRect, appName);

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        private Rect LerpRect(Rect a, Rect b, float t)
        {
            return new Rect(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));
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