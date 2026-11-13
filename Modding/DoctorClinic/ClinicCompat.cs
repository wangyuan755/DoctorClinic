using System;
using System.Reflection;
using UnityEngine;

namespace DoctorClinic
{
    /// <summary>
    /// Bridges the ModHelper members that only exist on 1.0.0.14 (100-Rev14, open beta).
    /// The same DLL therefore loads on the public build (100-Rev13), where endings,
    /// IsGameOver and mod-owned debug panels are absent: the tribunal falls back to the
    /// vanilla game-over screen, IsGameOver reads that screen, and debug buttons land in
    /// the vanilla Modding column.
    /// </summary>
    internal static class ClinicCompat
    {
        private delegate bool TriggerEndingDelegate(string modId, string endingId);

        private static readonly MethodInfo AddEndingMethod = Probe("AddEnding");
        private static readonly MethodInfo TriggerEndingMethod = Probe("TriggerEnding");
        private static readonly MethodInfo RemoveEndingsMethod = Probe("RemoveEndings");
        private static readonly MethodInfo IsGameOverMethod = Probe("IsGameOver");
        private static readonly MethodInfo AddDebugPanelMethod = Probe("AddDebugPanel");

        private static readonly TriggerEndingDelegate TriggerEndingCall = BindTriggerEnding();

        private static TriggerEndingDelegate BindTriggerEnding()
        {
            if (TriggerEndingMethod == null) return null;
            try
            {
                return TriggerEndingMethod.CreateDelegate(typeof(TriggerEndingDelegate)) as TriggerEndingDelegate;
            }
            catch (Exception e)
            {
                WarnOnce("TriggerEnding", e);
                return null;
            }
        }

        private static bool warned;

        internal static bool HasDebugPanels => AddDebugPanelMethod != null;

        private static MethodInfo Probe(string name) =>
            typeof(ModHelper).GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        internal static void AddEnding(string modId, string endingId, Func<string> title, Func<string> description)
        {
            if (AddEndingMethod == null) return;
            try
            {
                AddEndingMethod.Invoke(null, new object[] { modId, endingId, title, description, false, null });
            }
            catch (Exception e)
            {
                WarnOnce("AddEnding", e);
            }
        }

        internal static int RemoveEndings(string modId)
        {
            if (RemoveEndingsMethod == null) return 0;
            try
            {
                return (int)RemoveEndingsMethod.Invoke(null, new object[] { modId });
            }
            catch (Exception e)
            {
                WarnOnce("RemoveEndings", e);
                return 0;
            }
        }

        /// <summary>
        /// Ends the run with the clinic's tribunal. Rev14 shows it as a registered ending;
        /// the public build has no ending registry, so the tribunal takes over the vanilla
        /// game-over screen with the clinic's own title and description.
        /// </summary>
        internal static bool TriggerEnding(string modId, string endingId, Func<string> title, Func<string> description)
        {
            if (TriggerEndingCall != null)
            {
                try
                {
                    if (TriggerEndingCall(modId, endingId)) return true;
                }
                catch (Exception e)
                {
                    WarnOnce("TriggerEnding", e);
                }
            }
            return ShowGameOverScreen(title?.Invoke(), description?.Invoke());
        }

        internal static bool IsGameOver()
        {
            if (IsGameOverMethod != null)
            {
                try
                {
                    return (bool)IsGameOverMethod.Invoke(null, null);
                }
                catch (Exception e)
                {
                    WarnOnce("IsGameOver", e);
                }
            }
            // Public build: the game-over screen (or the run summary) being on screen is
            // the only ending state the game exposes.
            return GameOverScreenShowing();
        }

        internal static bool AddDebugPanel(string modId, string id, string title, string[] categories, bool searchable)
        {
            if (AddDebugPanelMethod == null) return false;
            try
            {
                return (bool)AddDebugPanelMethod.Invoke(null, new object[] { modId, id, title, categories, searchable });
            }
            catch (Exception e)
            {
                WarnOnce("AddDebugPanel", e);
                return false;
            }
        }

        // Own debug panel on Rev14; the vanilla Modding column otherwise.
        internal static string DebugCategory(string category) => HasDebugPanels ? category : ModHelper.DEBUG_CATEGORY_MODDING;

        // The shared Modding column needs the tab name to tell the buttons apart.
        internal static string DebugLabel(string tab, string name) => HasDebugPanels ? name : tab + " / " + name;

        private static bool ShowGameOverScreen(string title, string description)
        {
            try
            {
                var type = typeof(GameOverUIManager);
                var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return false;
                type.GetMethod("SetEnding", BindingFlags.Public | BindingFlags.Instance)?.Invoke(instance, new object[] { "" });
                var display = type.GetMethod("DisplayUI", BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(string), typeof(string), typeof(bool) }, null);
                if (display == null) return false;
                display.Invoke(instance, new object[] { title, description, false });
                return true;
            }
            catch (Exception e)
            {
                WarnOnce("GameOverUIManager", e);
                return false;
            }
        }

        private static bool GameOverScreenShowing()
        {
            try
            {
                var type = typeof(GameOverUIManager);
                var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (instance == null) return false;
                return type.GetProperty("IsShowing", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance) is true;
            }
            catch
            {
                return false;
            }
        }

        private static void WarnOnce(string member, Exception e)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning($"[DoctorClinic] ModHelper ending/debug bridge failed on {member}; the matching feature stays disabled. {e.Message}");
        }
    }
}
