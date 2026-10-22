using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using PotatoOptimization.Core;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Features
{
    public class NoteExportManager
    {
        private readonly NoteExportSelectionState _selectionState = new NoteExportSelectionState();
        private readonly NoteService _noteService;
        private readonly NoteExportFolderPicker _folderPicker;
        private readonly Func<ExportLanguage> _languageProvider;

        public event Action<bool> OnSelectionModeChanged;
        public event Action<IReadOnlyCollection<ulong>> OnSelectionChanged;
        public event Action<string> OnExportSuccess;
        public event Action<string> OnExportFailure;

        internal Action<Action> DispatchToMainThread { get; set; } = PotatoPlugin.DispatchToMainThread;
        private int _generation;
        private int _exporting;
        public bool IsExporting => Volatile.Read(ref _exporting) != 0;
        public void CancelPending() { Interlocked.Increment(ref _generation); Interlocked.Exchange(ref _exporting, 0); }

        public NoteExportManager(
            NoteService noteService,
            NoteExportFolderPicker folderPicker,
            Func<ExportLanguage> languageProvider = null)
        {
            _noteService = noteService;
            _folderPicker = folderPicker;
            _languageProvider = languageProvider ?? (() => ExportLanguage.English);
        }

        public bool IsSelectionMode => _selectionState.IsSelectionMode;

        public IReadOnlyCollection<ulong> SelectedPageIds => _selectionState.SelectedPageIds;

        public int SelectedCount => _selectionState.SelectedPageIds.Count;

        internal static bool ShouldReuseManagerForService(NoteExportManager manager, NoteService noteService)
        {
            return manager != null && ReferenceEquals(manager._noteService, noteService);
        }

        internal static NoteExportManager RebindManagerSubscriptions(
            NoteExportManager currentManager,
            NoteExportManager replacementManager,
            Action<bool> selectionModeChangedHandler,
            Action<IReadOnlyCollection<ulong>> selectionChangedHandler)
        {
            if (ReferenceEquals(currentManager, replacementManager))
            {
                return currentManager;
            }

            if (currentManager != null)
            {
                currentManager.CancelPending();
                if (selectionModeChangedHandler != null)
                {
                    currentManager.OnSelectionModeChanged -= selectionModeChangedHandler;
                }

                if (selectionChangedHandler != null)
                {
                    currentManager.OnSelectionChanged -= selectionChangedHandler;
                }
            }

            if (replacementManager != null)
            {
                if (selectionModeChangedHandler != null)
                {
                    replacementManager.OnSelectionModeChanged += selectionModeChangedHandler;
                }

                if (selectionChangedHandler != null)
                {
                    replacementManager.OnSelectionChanged += selectionChangedHandler;
                }
            }

            return replacementManager;
        }

        public void EnterSelectionMode()
        {
            CancelPending();
            _selectionState.EnterSelectionMode();
            OnSelectionModeChanged?.Invoke(_selectionState.IsSelectionMode);
            OnSelectionChanged?.Invoke(_selectionState.SelectedPageIds);
        }

        public void ToggleSelection(ulong pageId)
        {
            if (!_selectionState.IsSelectionMode)
            {
                return;
            }

            _selectionState.ToggleSelection(pageId);
            OnSelectionChanged?.Invoke(_selectionState.SelectedPageIds);
        }

        public void ExitSelectionMode()
        {
            CancelPending();
            _selectionState.ExitSelectionMode();
            OnSelectionModeChanged?.Invoke(_selectionState.IsSelectionMode);
            OnSelectionChanged?.Invoke(_selectionState.SelectedPageIds);
        }

        public bool IsSelected(ulong pageId)
        {
            return _selectionState.IsSelected(pageId);
        }


        private sealed class Snapshot
        {
            public ulong Id;
            public string Title, Content;
            public bool Available;
        }

        public void ExportSelected()
        {
            if (SelectedCount == 0) { OnExportFailure?.Invoke("No notes selected."); return; }
            if (_noteService == null || _folderPicker == null)
            { OnExportFailure?.Invoke("Export dependencies are unavailable."); return; }
            if (Interlocked.CompareExchange(ref _exporting, 1, 0) != 0) return;
            int generation = Interlocked.Increment(ref _generation);
            try
            {
                var snapshots = new List<Snapshot>();
                // Game objects/services are read before opening the worker-thread dialog.
                foreach (ulong id in GetStableExportOrder(SelectedPageIds))
                {
                    var page = _noteService.GetPageSaveData(id);
                    string title = _noteService.GetPageTitle(id) ?? string.Empty;
                    snapshots.Add(new Snapshot { Id = id, Title = title, Available = page != null,
                        Content = page != null ? NoteExportNaming.FormatExportContent(title, page.MainText) : null });
                }
                var language = _languageProvider();
                OnSelectionChanged?.Invoke(SelectedPageIds);
                _folderPicker.BeginPickFolder(result => ExportSnapshots(result, snapshots, language, generation));
            }
            catch (Exception error)
            {
                Interlocked.Exchange(ref _exporting, 0);
                LogError("[NoteExport] Capture failed: " + error);
                OnExportFailure?.Invoke("Export data is unavailable.");
            }
        }

        private void ExportSnapshots(NoteExportFolderPicker.FolderPickerResult result,
            IReadOnlyList<Snapshot> snapshots, ExportLanguage language, int generation)
        {
            var completed = new List<ulong>();
            int failed = 0;
            string errorMessage = null;
            bool cancelled = result != null && result.WasCancelled;
            if (result == null || (!cancelled && (!result.Succeeded || string.IsNullOrWhiteSpace(result.FolderPath))))
                errorMessage = "Folder selection failed.";
            else if (!cancelled)
            {
                var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var snapshot in snapshots)
                {
                    if (generation != Volatile.Read(ref _generation)) return;
                    if (!snapshot.Available) { failed++; continue; }
                    try
                    {
                        string path = NoteExportNaming.GetUniqueExportFilePath(result.FolderPath, snapshot.Title,
                            ExportLocalization.GetNoteFallbackTitle(language), reserved);
                        ExportFileWriter.WriteNew(path, snapshot.Content);
                        completed.Add(snapshot.Id);
                    }
                    catch (Exception error) { failed++; LogError("[NoteExport] File failed: " + error); }
                }
            }
            DispatchToMainThread(() =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                Interlocked.Exchange(ref _exporting, 0);
                if (cancelled) { OnSelectionChanged?.Invoke(SelectedPageIds); return; }
                if (errorMessage != null) { OnSelectionChanged?.Invoke(SelectedPageIds); OnExportFailure?.Invoke(errorMessage); return; }
                if (completed.Count > 0 && failed == 0)
                { ExitSelectionMode(); OnExportSuccess?.Invoke($"Exported {completed.Count} note(s) to {result.FolderPath}"); return; }
                foreach (ulong id in completed) if (IsSelected(id)) _selectionState.ToggleSelection(id);
                OnSelectionChanged?.Invoke(SelectedPageIds);
                OnExportFailure?.Invoke(completed.Count > 0
                    ? $"Export partially failed: exported {completed.Count} of {snapshots.Count} note(s). Retry remaining selected notes."
                    : "Export failed.");
            });
        }

        internal static IReadOnlyList<ulong> GetStableExportOrder(IReadOnlyCollection<ulong> selectedPageIds)
        {
            return selectedPageIds
                .OrderBy(pageId => pageId)
                .ToArray();
        }

        private static void LogWarning(string message)
        {
            if (PotatoPlugin.Log != null)
            {
                PotatoPlugin.Log.LogWarning(message);
            }
        }

        private static void LogError(string message)
        {
            if (PotatoPlugin.Log != null)
            {
                PotatoPlugin.Log.LogError(message);
            }
        }
    }
}
