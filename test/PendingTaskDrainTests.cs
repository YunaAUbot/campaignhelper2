namespace CampaignHelper.Tests;

using System.Diagnostics;
using Xunit;

public sealed class PendingTaskDrainTests
{
    [Fact]
    public void ClearsJoinedTasksButRetainsTimedOutTaskForSafeUnloadRetry()
    {
        Task<int>? completed = Task.FromResult(1);
        Assert.True(PendingTaskDrain.DrainAndClear(ref completed, TimeSpan.FromMilliseconds(50)));
        Assert.Null(completed);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<int>? canceled = Task.FromCanceled<int>(cancellation.Token);
        Assert.True(PendingTaskDrain.DrainAndClear(ref canceled, TimeSpan.FromMilliseconds(50)));
        Assert.Null(canceled);

        var source = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int>? pending = source.Task;
        var stopwatch = Stopwatch.StartNew();
        Assert.False(PendingTaskDrain.DrainAndClear(ref pending, TimeSpan.FromMilliseconds(20)));
        Assert.Same(source.Task, pending);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));

        source.SetResult(2);
        Assert.True(PendingTaskDrain.DrainAndClear(ref pending, TimeSpan.FromMilliseconds(50)));
        Assert.Null(pending);
    }
}
