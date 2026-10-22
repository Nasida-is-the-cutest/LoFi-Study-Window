using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Bulbul;
using PotatoOptimization.Core;
using PotatoOptimization.Utilities;

namespace PotatoOptimization.Features
{
    public sealed class TodoExportManager
    {
        private readonly Func<TodoListData> _currentListProvider;
        private readonly NoteExportFolderPicker _folderPicker;
        private readonly Func<ExportLanguage> _languageProvider;

        public TodoExportManager(
            Func<TodoListData> currentListProvider,
            NoteExportFolderPicker folderPicker,
            Func<ExportLanguage> languageProvider = null)
        {
            _currentListProvider = currentListProvider ??
                throw new ArgumentNullException(nameof(currentListProvider));
            _folderPicker = folderPicker ??
                throw new ArgumentNullException(nameof(folderPicker));
            _languageProvider = languageProvider ?? (() => ExportLanguage.English);
        }

        public event Action<string> OnExportSuccess;

        public event Action<string> OnExportFailure;

        internal Action<Action> DispatchToMainThread { get; set; } = PotatoPlugin.DispatchToMainThread;
        private int _generation, _exporting;
        public bool IsExporting => Volatile.Read(ref _exporting) != 0;
        public void CancelPending() { Interlocked.Increment(ref _generation); Interlocked.Exchange(ref _exporting, 0); }

        public void ExportCurrentList()
        {
            if (Interlocked.CompareExchange(ref _exporting, 1, 0) != 0) return;
            int generation = Interlocked.Increment(ref _generation);
            TodoExportSnapshot snapshot;
            try
            {
                snapshot = CreateSnapshot(_currentListProvider());
            }
            catch (Exception ex)
            {
                LogError("[TodoExport] Failed to capture todo data: " + ex);
                Interlocked.Exchange(ref _exporting, 0);
                OnExportFailure?.Invoke("Todo data is unavailable.");
                return;
            }

            if (snapshot == null)
            {
                Interlocked.Exchange(ref _exporting, 0);
                OnExportFailure?.Invoke("Todo data is unavailable.");
                return;
            }

            try
            {
                ExportLanguage language = _languageProvider();
                _folderPicker.BeginPickFolder(result => ContinueExport(result, snapshot, language, generation));
            }
            catch (Exception error)
            { Interlocked.Exchange(ref _exporting, 0); LogError("[TodoExport] " + error); OnExportFailure?.Invoke("Export failed."); }
        }

        internal static TodoExportSnapshot CreateSnapshot(TodoListData listData)
        {
            if (listData == null)
            {
                return null;
            }

            var items = new List<TodoExportItem>();
            if (listData.TodoOrderList != null && listData.TodoDic != null)
            {
                foreach (ulong todoId in listData.TodoOrderList)
                {
                    if (!listData.TodoDic.TryGetValue(todoId, out TodoData todoData) ||
                        todoData == null)
                    {
                        continue;
                    }

                    items.Add(new TodoExportItem(
                        todoData.TodoText,
                        todoData.CurrentState == TodoState.Complete,
                        todoData.Expire));
                }
            }

            return new TodoExportSnapshot(listData.TitleText, items);
        }


        private void ContinueExport(NoteExportFolderPicker.FolderPickerResult result,
            TodoExportSnapshot snapshot, ExportLanguage language, int generation)
        {
            if (generation != Volatile.Read(ref _generation)) return;
            string message = null;
            bool success = false;
            if (result == null || (!result.WasCancelled && (!result.Succeeded || string.IsNullOrWhiteSpace(result.FolderPath))))
                message = "Folder selection failed.";
            else if (!result.WasCancelled)
            {
                try
                {
                    var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string path = NoteExportNaming.GetUniqueExportFilePath(result.FolderPath, snapshot.Title,
                        ExportLocalization.GetTodoFallbackTitle(language), reserved);
                    ExportFileWriter.WriteNew(path, TodoExportFormatter.Format(snapshot.Items, language));
                    message = $"Exported {snapshot.Items.Count} todo(s) to {path}"; success = true;
                }
                catch (Exception error) { LogError("[TodoExport] " + error); message = "Export failed."; }
            }
            DispatchToMainThread(() =>
            {
                if (generation != Volatile.Read(ref _generation)) return;
                Interlocked.Exchange(ref _exporting, 0);
                if (message == null) return;
                if (success) OnExportSuccess?.Invoke(message); else OnExportFailure?.Invoke(message);
            });
        }

        private static void LogError(string message)
        {
            PotatoPlugin.Log?.LogError(message);
        }
    }

    public sealed class TodoExportSnapshot
    {
        public TodoExportSnapshot(string title, IReadOnlyList<TodoExportItem> items)
        {
            Title = title ?? string.Empty;
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public string Title { get; }

        public IReadOnlyList<TodoExportItem> Items { get; }
    }
}
