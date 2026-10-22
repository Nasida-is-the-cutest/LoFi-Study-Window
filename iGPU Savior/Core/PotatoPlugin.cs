using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using PotatoOptimization.Configuration;
using PotatoOptimization.Patches;
using PotatoOptimization.UI;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Core
{
    [BepInPlugin(Constants.PluginGUID, Constants.PluginName, Constants.PluginVersion)]
    public class PotatoPlugin : BaseUnityPlugin
    {
        public static PotatoPlugin Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public static new ConfigurationManager Config { get; private set; }
        public static MainThreadQueue MainThread { get; private set; }
        public static PotatoController Controller { get; private set; }
        public static void DispatchToMainThread(Action action) { MainThread?.Post(action); }
        public static string PatchHealth { get; private set; } = "Starting";
        private readonly List<Harmony> _patches = new List<Harmony>();
        private GameObject _runnerObject;

        void Awake()
        {
            // The game clears discoverable bootstrap objects before its first
            // frame. Match BepInEx's HideManagerGameObject protection without
            // requiring the player to edit the loader's global configuration.
            gameObject.hideFlags |= HideFlags.HideAndDontSave;
            Instance = this; Log = Logger;
            MainThread = new MainThreadQueue();
            Config = new ConfigurationManager(base.Config);
            ApplyHarmonyPatches();
            _runnerObject = new GameObject("PotatoRunner");
            DontDestroyOnLoad(_runnerObject); _runnerObject.hideFlags = HideFlags.HideAndDontSave;
            Controller = _runnerObject.AddComponent<PotatoController>();
            if (ModShared.ModSettingsManager.Instance == null)
            {
                var manager = new GameObject("ModSettingsManager"); manager.hideFlags = HideFlags.HideAndDontSave;
                DontDestroyOnLoad(manager); manager.AddComponent<ModShared.ModSettingsManager>();
            }
            Log.LogInfo(Constants.PluginName + " v" + Constants.PluginVersion + ": " + PatchHealth);
        }

        private void ApplyHarmonyPatches()
        {
            // Explicit typeof references keep all supported patch classes loaded.
            Type[] types = { typeof(TodoDeleteConfirmPatch), typeof(NoteDeleteConfirmPatch),
                typeof(NoteExportPatch), typeof(NoteExportSelectPagePatch), typeof(NoteExportSelectPageGuardPatch),
                typeof(NoteExportEditTitleGuardPatch), typeof(NoteExportStartReorderGuardPatch),
                typeof(NoteExportDragReorderGuardPatch), typeof(NoteExportEndReorderGuardPatch),
                typeof(TodoExportPatch), typeof(FacilityClickHeroineMirrorPatch), typeof(CursorServiceMirrorPatch),
                typeof(CostumePatch), typeof(ModSettingsIntegration), typeof(ModSettingsActivateHandler),
                typeof(LearningTimerClockPatch), typeof(LearningTimerBoundaryPatch) };
            int failures = 0;
            foreach (var type in types)
            {
                var harmony = new Harmony(Constants.PluginGUID + "." + type.Name);
                try { harmony.CreateClassProcessor(type).Patch(); _patches.Add(harmony); }
                catch (Exception error)
                {
                    failures++;
                    try { harmony.UnpatchSelf(); } catch (Exception cleanup) { Log.LogError(cleanup); }
                    Log.LogError("[Patch disabled] " + type.Name + ": " + error);
                }
            }
            PatchHealth = failures == 0 ? "Ready" : failures + " patch group(s) unavailable; see log";
        }

        void OnDestroy()
        {
            MainThread?.Stop();
            CleanUi(NoteExportPatch.Cleanup);
            CleanUi(ModSettingsIntegration.Cleanup);
            CleanUi(ExitConfirmationDialogHelper.Cleanup);
            CleanUi(NoteDeleteConfirmPatch.Cleanup);
            if (_runnerObject != null)
            { _runnerObject.GetComponent<PotatoController>()?.Cleanup(); Destroy(_runnerObject); }
            foreach (var harmony in _patches)
                try { harmony.UnpatchSelf(); } catch (Exception error) { Log.LogError(error); }
            _patches.Clear(); Controller = null; Instance = null;
            LearningTimerClock.Cleanup();
        }

        private static void CleanUi(Action cleanup)
        { try { cleanup(); } catch (Exception error) { Log.LogError("[UI cleanup] " + error); } }
    }
}
