using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using PotatoOptimization.Features;
using PotatoOptimization.Utilities;
using Xunit;

namespace IGPUSavior.Tests
{
    public class ExportThreadSafetyTests
    {
        [Fact]
        public void WorkerUsesCapturedDataAndPublishesOnlyOnOwnerThread()
        {
            string folder = Path.Combine(Path.GetTempPath(), "igpu-export-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var queue = new MainThreadQueue();
                int owner = Environment.CurrentManagedThreadId, completionThread = 0;
                Action<NoteExportFolderPicker.FolderPickerResult> complete = null;
                var picker = Picker(folder, (work, callback) => complete = callback);
                var service = new NoteService(); service.SetPage(1, "Before", "Original");
                var manager = new NoteExportManager(service, picker) { DispatchToMainThread = queue.Post };
                manager.OnExportSuccess += _ => completionThread = Environment.CurrentManagedThreadId;
                manager.EnterSelectionMode(); manager.ToggleSelection(1); manager.ExportSelected();
                service.SetPage(1, "After", "Changed while dialog was open");
                Exception error = null;
                var thread = new Thread(() => { try { complete(NoteExportFolderPicker.FolderPickerResult.Success(folder)); } catch (Exception ex) { error = ex; } });
                thread.Start(); Assert.True(thread.Join(5000)); Assert.Null(error);
                Assert.True(manager.IsExporting); Assert.True(manager.IsSelectionMode); Assert.Equal(0, completionThread);
                string file = Assert.Single(Directory.GetFiles(folder, "*.txt"));
                Assert.StartsWith("Before_", Path.GetFileName(file));
                Assert.Equal(NoteExportNaming.FormatExportContent("Before", "Original"), File.ReadAllText(file, Encoding.UTF8));
                queue.Drain(_ => Assert.Fail("Completion failed"));
                Assert.Equal(owner, completionThread); Assert.False(manager.IsExporting); Assert.False(manager.IsSelectionMode);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void CancelledGenerationCannotWriteOrPublishLateCompletion()
        {
            string folder = Path.Combine(Path.GetTempPath(), "igpu-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Action<NoteExportFolderPicker.FolderPickerResult> complete = null;
                var queue = new MainThreadQueue(); var service = new NoteService(); service.SetPage(1, "Title", "Body");
                var manager = new NoteExportManager(service, Picker(folder, (work, callback) => complete = callback)) { DispatchToMainThread = queue.Post };
                int events = 0; manager.OnExportSuccess += _ => events++; manager.OnExportFailure += _ => events++;
                manager.EnterSelectionMode(); manager.ToggleSelection(1); manager.ExportSelected(); manager.ExitSelectionMode();
                complete(NoteExportFolderPicker.FolderPickerResult.Success(folder)); queue.Drain(_ => Assert.Fail("Unexpected callback"));
                Assert.Empty(Directory.GetFiles(folder)); Assert.Equal(0, events); Assert.False(manager.IsExporting);
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void FolderFailureReenablesExportWithoutDiscardingSelection()
        {
            var queue = new MainThreadQueue(); var service = new NoteService(); service.SetPage(1, "Title", "Body");
            var manager = new NoteExportManager(service, Picker("unused", (work, callback) => callback(NoteExportFolderPicker.FolderPickerResult.Failed(new IOException())))) { DispatchToMainThread = queue.Post };
            bool sawIdle = false;
            manager.OnSelectionChanged += _ => { if (!manager.IsExporting) sawIdle = true; };
            manager.EnterSelectionMode(); manager.ToggleSelection(1); sawIdle = false;
            manager.ExportSelected(); Assert.True(manager.IsExporting); queue.Drain(_ => Assert.Fail("Unexpected error"));
            Assert.True(sawIdle); Assert.False(manager.IsExporting); Assert.True(manager.IsSelected(1));
        }

        [Fact]
        public void NestedCoroutineFailureRunsRollbackAndReleasesBusyState()
        {
            bool busy = false, rollback = false; int failures = 0;
            IEnumerator Child() { yield return null; throw new IOException("Window failed"); }
            IEnumerator Parent() { busy = true; try { yield return Child(); } finally { busy = false; rollback = true; } }
            var guard = CoroutineGuard.Run(Parent(), _ => failures++);
            Assert.True(guard.MoveNext()); Assert.True(busy); Assert.False(guard.MoveNext());
            Assert.False(busy); Assert.True(rollback); Assert.Equal(1, failures);
        }

        private static NoteExportFolderPicker Picker(string folder,
            Action<Func<NoteExportFolderPicker.FolderPickerResult>, Action<NoteExportFolderPicker.FolderPickerResult>> run)
        { return new NoteExportFolderPicker(() => IntPtr.Zero, work => work(),
            _ => NoteExportFolderPicker.FolderPickerResult.Success(folder), _ => { }, _ => { }, run); }
    }
}
