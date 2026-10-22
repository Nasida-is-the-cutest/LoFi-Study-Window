using System;
using PotatoOptimization.Utilities;
using Xunit;

namespace IGPUSavior.Tests
{
    public class MonotonicTimerTests
    {
        [Theory]
        [InlineData(1500)]
        [InlineData(300)]
        public void DurationStaysAccurateAcrossIrregularLowFrameRates(int duration)
        {
            var progress = new MonotonicTimerProgress(); progress.Rebase(0, 0, true);
            double now = 0; float elapsed = 0; int frame = 0;
            while(now < duration)
            {
                double step = ++frame % 43 == 0 ? 1.3 : (frame % 2 == 0 ? 0.1 : 1.0 / 15);
                now = Math.Min(duration, now + step);
                elapsed = progress.Advance(now, elapsed, true); progress.Accept(elapsed);
            }
            Assert.InRange(Math.Abs(elapsed-duration), 0, 0.001);
        }

        [Fact]
        public void PauseAndResumeExcludePausedTime()
        {
            var progress = new MonotonicTimerProgress(); progress.Rebase(0, 0, true);
            float elapsed = progress.Advance(10, 0, true); progress.Accept(elapsed);
            progress.Rebase(10, elapsed, false);
            Assert.Equal(10, progress.Advance(100, elapsed, false));
            progress.Rebase(100, elapsed, true);
            Assert.Equal(30, progress.Advance(120, elapsed, true));
        }

        [Fact]
        public void ManualRemainingTimeChangeRebasesExistingTimer()
        {
            var progress = new MonotonicTimerProgress(); progress.Rebase(0, 0, true);
            var elapsed = progress.Advance(10, 0, true); progress.Accept(elapsed);
            progress.Rebase(10, 1200, true);
            Assert.Equal(1201, progress.Advance(11, 1200, true));
        }

        [Fact]
        public void BackwardsClockDoesNotCountTheSameIntervalTwice()
        {
            var progress = new MonotonicTimerProgress(); progress.Rebase(0, 0, true);
            float elapsed = progress.Advance(10, 0, true); progress.Accept(elapsed);
            elapsed = progress.Advance(9, elapsed, true); progress.Accept(elapsed);
            Assert.Equal(11, progress.Advance(11, elapsed, true));
        }
    }
}
