using Verse;
using UnityEngine;
using RimTalkRealitySync.UI.Apps;

namespace RimTalkRealitySync.UI
{
    /// <summary>
    /// Global state manager for the RimPhone.
    /// Ensures apps stay on the screen they were left on, and handles the universal toggle.
    /// </summary>
    public static class RimPhoneStateManager
    {
        // Remembers which app the user was last looking at
        public static string CurrentApp = "Home"; // Options: "Home", "Gallery", "Messages"

        private static float _lastToggleTime = 0f;

        public static void ToggleWindow()
        {
            // Hardware debounce to prevent spamming the hotkey
            if (Time.realtimeSinceStartup - _lastToggleTime < 0.25f) return;
            _lastToggleTime = Time.realtimeSinceStartup;

            var home = Find.WindowStack.WindowOfType<RimOSHomeScreen>();
            var gallery = Find.WindowStack.WindowOfType<RimOSGalleryApp>();
            var messages = Find.WindowStack.WindowOfType<RimOSMessagesApp>();

            // If any part of the phone is open, tell it to slide down and close securely
            if (home != null && !home.IsClosing)
            {
                home.SlideDownAndClose();
            }
            else if (gallery != null && !gallery.IsClosing)
            {
                gallery.SlideDownAndClose();
            }
            else if (messages != null && !messages.IsClosing)
            {
                messages.SlideDownAndClose();
            }
            else
            {
                // Phone is closed. Open exactly where the user left off!
                if (CurrentApp == "Gallery")
                    Find.WindowStack.Add(new RimOSGalleryApp(skipSlideAnimation: false));
                else if (CurrentApp == "Messages")
                    Find.WindowStack.Add(new RimOSMessagesApp(skipSlideAnimation: false));
                else
                    Find.WindowStack.Add(new RimOSHomeScreen(skipSlideAnimation: false));
            }
        }
    }
}