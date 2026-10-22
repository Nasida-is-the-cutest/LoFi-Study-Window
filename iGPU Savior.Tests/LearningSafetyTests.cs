using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PotatoOptimization.Utilities;
using Xunit;

public class LearningSafetyTests
{
    [Theory]
    [InlineData(LearningView.Static, true, false, 10, 10, true)]
    [InlineData(LearningView.Animated, true, false, 15, 3, false)]
    [InlineData(LearningView.Static, true, true, 15, 1, true)]
    [InlineData(LearningView.Animated, true, true, 15, 1, false)]
    [InlineData(LearningView.Static, false, true, 10, 10, true)]
    [InlineData(LearningView.Animated, false, true, 10, 10, true)]
    public void HiddenStateHasPriorityAndLogicRemainsIndependent(LearningView view, bool visible,
        bool interacting, int logic, int interval, bool freeze)
    {
        var profile = LearningModePolicy.Select(view, visible, interacting);
        Assert.Equal(logic, profile.LogicFps); Assert.Equal(interval, profile.RenderInterval);
        Assert.Equal(freeze, profile.FreezeScene);
        Assert.True(profile.LogicFps >= 10);
    }

    [Fact]
    public void GeometryClampsDisconnectedMonitorAndPreservesAspectSizedWindow()
    {
        var placement = WindowGeometry.Clamp(4000, -900, 480, 270, 0, 0, 1920, 1040, 1, false);
        Assert.Equal(1440, placement.X); Assert.Equal(0, placement.Y);
        Assert.Equal(480, placement.Width); Assert.Equal(270, placement.Height);
    }

    [Fact]
    public void GeometryFirstUseHandlesDpiAndNegativeMonitorCoordinates()
    {
        var placement = WindowGeometry.Clamp(int.MinValue, int.MinValue, 720, 405, -1920, 0, 0, 1080, 1.5, true);
        Assert.Equal(-756, placement.X); Assert.Equal(639, placement.Y);
    }

    [Fact]
    public void GeometryFitsEvenWhenWorkAreaIsSmallerThanMinimum()
    {
        var placement = WindowGeometry.Clamp(0, 0, 1000, 1000, 20, 30, 220, 130, 2, false);
        Assert.Equal(20, placement.X); Assert.Equal(30, placement.Y);
        Assert.Equal(200, placement.Width); Assert.Equal(100, placement.Height);
    }

    [Fact]
    public void WorkerCallbacksAreQueuedUntilOwnerThreadDrains()
    {
        var queue = new MainThreadQueue(); int owner = Thread.CurrentThread.ManagedThreadId;
        int observed = 0;
        var worker = new Thread(() => queue.Post(() => observed = Thread.CurrentThread.ManagedThreadId));
        worker.Start(); worker.Join(); Assert.Equal(0, observed);
        queue.Drain(error => throw error); Assert.Equal(owner, observed);
    }

    [Fact]
    public void WorkerCannotDrainAndOneFailingActionDoesNotDiscardTheNext()
    {
        var queue = new MainThreadQueue(); Exception workerError = null;
        var worker = new Thread(() => { try { queue.Drain(null); } catch (Exception error) { workerError = error; } });
        worker.Start(); worker.Join(); Assert.IsType<InvalidOperationException>(workerError);
        int completed = 0; var errors = new List<Exception>();
        queue.Post(() => throw new InvalidOperationException("test")); queue.Post(() => completed++);
        queue.Drain(errors.Add); Assert.Single(errors); Assert.Equal(1, completed);
    }

    [Fact]
    public void QueueStopDropsLateAndPendingCallbacks()
    {
        var queue = new MainThreadQueue(); bool ran = false;
        queue.Post(() => ran = true); queue.Stop(); queue.Post(() => ran = true);
        queue.Drain(error => throw error); Assert.False(ran);
    }

    [Fact]
    public void OwnedSettingRestoresExactOriginalValue()
    {
        int value = 60; var setting = new OwnedSetting<int>(() => value, v => value = v);
        setting.Apply(10); setting.Apply(15); setting.Restore(); Assert.Equal(60, value);
    }

    [Fact]
    public void OwnedSettingPreservesExternalChangesAndNewOriginalOnReapply()
    {
        int value = 60; var setting = new OwnedSetting<int>(() => value, v => value = v);
        setting.Apply(10); value = 120; setting.Restore(); Assert.Equal(120, value);
        setting.Apply(10); value = 144; setting.Apply(15); setting.Restore(); Assert.Equal(144, value);
    }

    [Fact]
    public void OwnedSettingDetectsUnsupportedWrites()
    {
        int value = 60; var setting = new OwnedSetting<int>(() => value, v => { });
        Assert.Throws<InvalidOperationException>(() => setting.Apply(10)); Assert.Equal(60, value);
    }

    [Fact]
    public void RejectedClampedWriteCanStillRestoreOriginal()
    {
        int value = 60;
        var setting = new OwnedSetting<int>(() => value, v => value = v == 10 ? 5 : v);
        Assert.Throws<InvalidOperationException>(() => setting.Apply(10));
        Assert.Equal(5, value);
        setting.Restore(); Assert.Equal(60, value);
    }

    [Fact]
    public void SetterFailureAfterMutationCanStillRestoreOriginal()
    {
        int value = 60;
        var setting = new OwnedSetting<int>(() => value, v =>
        { value = v; if (v == 10) throw new IOException("Setter failed after writing"); });
        Assert.Throws<IOException>(() => setting.Apply(10));
        Assert.Equal(10, value);
        setting.Restore(); Assert.Equal(60, value);
    }

    [Fact]
    public void ExportWriterNeverOverwritesAndCleansItsTemporaryFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "igpu-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "note.txt");
            ExportFileWriter.WriteNew(file, "original");
            Assert.Throws<IOException>(() => ExportFileWriter.WriteNew(file, "replacement"));
            Assert.Equal("original", File.ReadAllText(file)); Assert.Single(Directory.GetFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
