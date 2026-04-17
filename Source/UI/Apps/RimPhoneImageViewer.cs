using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace RimTalkRealitySync.UI.Apps
{
    /// <summary>
    /// A standalone windowed image viewer. 
    /// Can be dragged around like Windows Photo Viewer.
    /// Intercepts scroll wheel events safely and features auto-hiding immersive UI.
    /// </summary>
    public class RimPhoneImageViewer : Window
    {
        private List<string> _imagePaths;
        private int _currentIndex;

        private Texture2D _currentTexture;

        // Viewer State
        private float _zoomLevel = 1.0f;
        private Vector2 _panPosition = Vector2.zero;
        private Vector2 _lastMousePos;
        private bool _isDragging = false;

        // Immersive UI Auto-Hide State
        private float _lastInteractionTime = 0f;
        private readonly float _uiHideDelay = 1.5f;

        public override Vector2 InitialSize => new Vector2(800f, 600f);
        protected override float Margin => 0f;

        // Strict lock. If the viewer is already open or queued, do absolutely nothing.
        public static void OpenViewer(List<string> paths, int startIndex)
        {
            if (Find.WindowStack.IsOpen(typeof(RimPhoneImageViewer)) || Find.WindowStack.WindowOfType<RimPhoneImageViewer>() != null)
            {
                return;
            }
            Find.WindowStack.Add(new RimPhoneImageViewer(paths, startIndex));
        }

        public RimPhoneImageViewer(List<string> paths, int startIndex)
        {
            this._imagePaths = paths;
            this._currentIndex = startIndex;

            this.doCloseX = true;
            this.doCloseButton = false;
            this.closeOnClickedOutside = true;
            this.draggable = true;

            this.forcePause = RimTalkRealitySyncMod.Settings.ImageViewerPausesGame;

            // Creates an invisible background shield to prevent clicks punching through to the gallery UI.
            this.absorbInputAroundWindow = true;

            this.layer = WindowLayer.Super;

            LoadCurrentImage();
        }

        public override void PreOpen()
        {
            base.PreOpen();
            // Ultimate fallback anti-spam guard. 
            if (Find.WindowStack.WindowOfType<RimPhoneImageViewer>() != this)
            {
                this.Close(false);
            }
        }

        private void LoadCurrentImage()
        {
            if (_currentTexture != null)
            {
                UnityEngine.Object.Destroy(_currentTexture);
                _currentTexture = null;
            }

            _zoomLevel = 1.0f;
            _panPosition = Vector2.zero;
            _lastInteractionTime = Time.realtimeSinceStartup;

            try
            {
                string path = _imagePaths[_currentIndex];
                byte[] fileData = File.ReadAllBytes(path);
                _currentTexture = new Texture2D(2, 2);
                _currentTexture.LoadImage(fileData);
            }
            catch (Exception ex)
            {
                Log.Error($"[RimPhone] Failed to load full image: {ex.Message}");
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            if (_currentTexture != null)
            {
                UnityEngine.Object.Destroy(_currentTexture);
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.DrawBoxSolid(inRect, new Color(0.1f, 0.1f, 0.1f, 0.95f));

            if (_currentTexture != null)
            {
                HandleInput(inRect);
                DrawImage(inRect);
            }

            float timeSinceLastInteraction = Time.realtimeSinceStartup - _lastInteractionTime;
            float uiAlpha = 1f;

            if (timeSinceLastInteraction > _uiHideDelay)
            {
                uiAlpha = Mathf.Clamp01(1f - ((timeSinceLastInteraction - _uiHideDelay) / 0.5f));
            }

            if (uiAlpha > 0.01f)
            {
                GUI.color = new Color(1f, 1f, 1f, uiAlpha);
                DrawImmersiveUI(inRect);
                GUI.color = Color.white;
            }
        }

        private void DrawImmersiveUI(Rect inRect)
        {
            Rect topBar = new Rect(0, 0, inRect.width, 35f);
            Widgets.DrawBoxSolid(topBar, new Color(0f, 0f, 0f, 0.7f));

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            string dateStr = new FileInfo(_imagePaths[_currentIndex]).CreationTime.ToString("yyyy-MM-dd HH:mm");
            Widgets.Label(topBar, $"<color=#00FFFF>{_currentIndex + 1} / {_imagePaths.Count}</color>  |  <color=#CCCCCC>{dateStr}</color>");
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;

            Widgets.Label(new Rect(10, inRect.height - 25, 400, 25), "<color=#888888>" + "RTRS_Viewer_Controls".Translate() + "</color>");
        }

        private void HandleInput(Rect inRect)
        {
            Event e = Event.current;

            if ((e.type == EventType.MouseMove || e.type == EventType.MouseDrag || e.type == EventType.ScrollWheel) && inRect.Contains(e.mousePosition))
            {
                _lastInteractionTime = Time.realtimeSinceStartup;
            }

            if (e.type == EventType.ScrollWheel && inRect.Contains(e.mousePosition))
            {
                float scrollDelta = e.delta.y;
                _zoomLevel -= scrollDelta * 0.1f;
                _zoomLevel = Mathf.Clamp(_zoomLevel, 0.2f, 5.0f);
                e.Use();
            }

            if (e.type == EventType.MouseDown && e.button == 0 && inRect.Contains(e.mousePosition))
            {
                _isDragging = true;
                _lastMousePos = e.mousePosition;
            }
            else if (e.type == EventType.MouseUp && e.button == 0)
            {
                _isDragging = false;
            }
            else if (e.type == EventType.MouseDrag && _isDragging)
            {
                Vector2 delta = e.mousePosition - _lastMousePos;
                _panPosition += delta;
                _lastMousePos = e.mousePosition;
                e.Use();
            }

            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.LeftArrow && _currentIndex > 0)
                {
                    _currentIndex--;
                    LoadCurrentImage();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.RightArrow && _currentIndex < _imagePaths.Count - 1)
                {
                    _currentIndex++;
                    LoadCurrentImage();
                    e.Use();
                }
            }
        }

        private void DrawImage(Rect inRect)
        {
            float imageAspect = (float)_currentTexture.width / _currentTexture.height;
            float screenAspect = inRect.width / inRect.height;

            float baseWidth, baseHeight;

            if (imageAspect > screenAspect)
            {
                baseWidth = inRect.width;
                baseHeight = inRect.width / imageAspect;
            }
            else
            {
                baseHeight = inRect.height;
                baseWidth = inRect.height * imageAspect;
            }

            float finalWidth = baseWidth * _zoomLevel;
            float finalHeight = baseHeight * _zoomLevel;

            Rect drawRect = new Rect(
                inRect.center.x - (finalWidth / 2f) + _panPosition.x,
                inRect.center.y - (finalHeight / 2f) + _panPosition.y,
                finalWidth,
                finalHeight
            );

            GUI.DrawTexture(drawRect, _currentTexture, ScaleMode.StretchToFill);
        }
    }
}