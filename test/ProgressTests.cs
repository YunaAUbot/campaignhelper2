namespace CampaignHelper.Tests;

using Xunit;

public sealed class ProgressTests
{
    private static readonly CampaignPage[] Pages =
    {
        new(1, 0, new[] { "go" }, "a"),
        new(1, 1, new[] { "go" }, "b"),
        new(1, 2, new[] { "done" }, null),
    };

    [Fact]
    public void FreshInitialObservationMapsToEarliestMatchingTargetWithoutReachingOrAdvancing()
    {
        var pages = new[]
        {
            new CampaignPage(1, 0, new[] { "start" }, "start"),
            new CampaignPage(1, 1, new[] { "first town" }, "town"),
            new CampaignPage(1, 2, new[] { "field" }, "field"),
            new CampaignPage(1, 3, new[] { "later town" }, "town"),
        };
        var progress = new CampaignProgress();
        var now = DateTime.UtcNow;

        progress.Observe("town", now, TimeSpan.Zero, pages);
        progress.Observe("town", now.AddSeconds(1), TimeSpan.Zero, pages);

        Assert.Equal(1, progress.Index);
        Assert.Null(progress.LastReachedAreaId);
        Assert.Null(progress.LastReachedAnchor);
        Assert.Equal(new PageAnchor(1, 1), progress.CurrentAnchor);
    }

    [Fact]
    public void RestoredProgressNeverRemapsFromInitialObservation()
    {
        var progress = new CampaignProgress();
        var now = DateTime.UtcNow;
        progress.Restore(0, null, Pages);

        progress.Observe("b", now, TimeSpan.Zero, Pages);
        progress.Observe("b", now.AddSeconds(1), TimeSpan.Zero, Pages);

        Assert.Equal(0, progress.Index);
        Assert.Null(progress.LastReachedAreaId);
    }

    [Fact]
    public void InitialObservationNeverAdvancesAndOnlyLaterTargetTransitionAdvances()
    {
        var progress = new CampaignProgress();
        var now = DateTime.UtcNow;

        progress.Observe("a", now, TimeSpan.FromSeconds(1), Pages);
        progress.Observe("a", now.AddSeconds(2), TimeSpan.FromSeconds(1), Pages);
        Assert.Equal(0, progress.Index);

        progress.Observe("future", now.AddSeconds(3), TimeSpan.FromSeconds(1), Pages);
        progress.Observe("future", now.AddSeconds(5), TimeSpan.FromSeconds(1), Pages);
        Assert.Equal(0, progress.Index);

        progress.Observe("a", now.AddSeconds(6), TimeSpan.FromSeconds(1), Pages);
        progress.Observe("a", now.AddSeconds(8), TimeSpan.FromSeconds(1), Pages);
        Assert.Equal(1, progress.Index);
    }

    [Fact]
    public void ManualNavigationRequiresAnotherDistinctAreaTransitionBeforeAutoAdvance()
    {
        var pages = new[]
        {
            new CampaignPage(1, 0, new[] { "a" }, "a"),
            new CampaignPage(1, 1, new[] { "b" }, "b"),
            new CampaignPage(1, 2, new[] { "c" }, "c"),
        };
        var progress = new CampaignProgress();
        var now = DateTime.UtcNow;
        progress.Observe("x", now, TimeSpan.Zero, pages);
        progress.Observe("b", now.AddSeconds(1), TimeSpan.Zero, pages);
        progress.SetManual(1, now.AddSeconds(2), TimeSpan.FromSeconds(3), pages);

        progress.Observe("b", now.AddSeconds(6), TimeSpan.Zero, pages);

        Assert.Equal(1, progress.Index);
    }

    [Fact]
    public void ManualGracePreventsAutomaticAdvance()
    {
        var progress = new CampaignProgress();
        var now = DateTime.UtcNow;
        progress.Observe("a", now, TimeSpan.Zero, Pages);
        progress.SetManual(1, now, TimeSpan.FromSeconds(3), Pages);

        progress.Observe("b", now.AddSeconds(1), TimeSpan.Zero, Pages);
        Assert.Equal(1, progress.Index);
        progress.Observe("b", now.AddSeconds(4), TimeSpan.Zero, Pages);
        Assert.Equal(2, progress.Index);
    }

    [Fact]
    public void RebaseKeepsStableSourcePageAcrossInsertedAndFilteredPages()
    {
        var progress = new CampaignProgress();
        progress.Restore(1, null, Pages);
        var inserted = new[]
        {
            new CampaignPage(1, 0, new[] { "go" }, "a"),
            new CampaignPage(1, 99, new[] { "inserted" }, "x"),
            new CampaignPage(1, 1, new[] { "go" }, "b"),
            new CampaignPage(1, 2, new[] { "done" }, null),
        };

        progress.Rebase(inserted);
        Assert.Equal(2, progress.Index);

        progress.Rebase(new[] { inserted[0], inserted[2], inserted[3] });
        Assert.Equal(1, progress.Index);
    }

    [Fact]
    public void GuideUpdateRebaseUsesReachedTargetAndCurrentAreaInsteadOfShiftedRawIndex()
    {
        var oldPages = new[]
        {
            new CampaignPage(1, 0, new[] { "start" }, "start"),
            new CampaignPage(1, 1, new[] { "forest" }, "forest"),
            new CampaignPage(1, 2, new[] { "town" }, "town"),
            new CampaignPage(1, 3, new[] { "cave" }, "cave"),
        };
        var progress = new CampaignProgress();
        progress.Restore(
            2,
            "forest",
            oldPages,
            new PageAnchor(1, 2),
            new PageAnchor(1, 1));
        var updatedPages = new[]
        {
            new CampaignPage(1, 0, new[] { "earlier town" }, "town"),
            new CampaignPage(1, 1, new[] { "inserted" }, "new"),
            new CampaignPage(1, 2, new[] { "forest" }, "forest"),
            new CampaignPage(1, 3, new[] { "current town" }, "town"),
            new CampaignPage(1, 4, new[] { "cave" }, "cave"),
        };

        progress.RebaseAfterGuideUpdate(updatedPages, "town");

        Assert.Equal(3, progress.Index);
        Assert.Equal(new PageAnchor(1, 3), progress.CurrentAnchor);
    }

    [Fact]
    public void RebaseUsesReachedSourceIdentityWithRepeatedTownTargets()
    {
        var repeated = new[]
        {
            new CampaignPage(1, 0, new[] { "town one" }, "town"),
            new CampaignPage(1, 1, new[] { "field" }, "field"),
            new CampaignPage(1, 2, new[] { "town two" }, "town"),
            new CampaignPage(1, 3, new[] { "after" }, "after"),
        };
        var progress = new CampaignProgress();
        var now = DateTime.UtcNow;
        progress.Restore(2, null, repeated);
        progress.Observe("field", now, TimeSpan.Zero, repeated);
        progress.Observe("town", now.AddSeconds(1), TimeSpan.Zero, repeated);
        progress.Observe("town", now.AddSeconds(2), TimeSpan.Zero, repeated);
        Assert.Equal(3, progress.Index);

        var withInsertedTown = new[]
        {
            repeated[0],
            new CampaignPage(1, 9, new[] { "another town" }, "town"),
            repeated[1],
            repeated[2],
            repeated[3],
        };
        progress.Rebase(withInsertedTown);
        Assert.Equal(4, progress.Index);
    }
}
