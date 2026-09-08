// <copyright file="CampaignProgress.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public sealed record PageAnchor(int Act, int SourceIndex);

public sealed class CampaignProgress
{
    private string? observedAreaId;
    private DateTime observedSince;
    private bool hasInitialObservation;
    private bool hasObservedTransition;
    private bool hasProgressState;

    public int Index { get; private set; }

    public DateTime ManualGraceUntilUtc { get; private set; }

    public string? LastReachedAreaId { get; private set; }

    public PageAnchor? CurrentAnchor { get; private set; }

    public PageAnchor? LastReachedAnchor { get; private set; }

    public bool HasProgressState => hasProgressState;

    public void Restore(
        int index,
        string? lastReachedAreaId,
        IReadOnlyList<CampaignPage> pages,
        PageAnchor? currentAnchor = null,
        PageAnchor? lastReachedAnchor = null,
        bool hasPersistedProgress = true)
    {
        Index = Clamp(index, pages.Count);
        LastReachedAreaId = lastReachedAreaId;
        hasProgressState = hasPersistedProgress;
        CurrentAnchor = currentAnchor;
        LastReachedAnchor = lastReachedAnchor;
        if (hasPersistedProgress)
        {
            CurrentAnchor ??= AnchorAt(pages, Index);
            Rebase(pages);
        }
    }

    public void SetManual(
        int index,
        DateTime now,
        TimeSpan grace,
        IReadOnlyList<CampaignPage> pages)
    {
        Index = Clamp(index, pages.Count);
        CurrentAnchor = AnchorAt(pages, Index);
        hasProgressState = true;
        ManualGraceUntilUtc = now + grace;
        RequireNewTransition();
    }

    public void Observe(
        string? areaId,
        DateTime now,
        TimeSpan stabilization,
        IReadOnlyList<CampaignPage> pages)
    {
        if (string.IsNullOrWhiteSpace(areaId) || pages.Count == 0)
        {
            return;
        }

        if (!hasInitialObservation)
        {
            hasInitialObservation = true;
            observedAreaId = areaId;
            observedSince = now;
            if (!hasProgressState)
            {
                var matchingIndex = FindFirstTargetAtOrAfter(pages, areaId, Index);
                if (matchingIndex >= 0)
                {
                    Index = matchingIndex;
                }
            }

            CurrentAnchor ??= AnchorAt(pages, Index);
            hasProgressState = true;
            return;
        }

        if (!StringComparer.OrdinalIgnoreCase.Equals(observedAreaId, areaId))
        {
            observedAreaId = areaId;
            observedSince = now;
            hasObservedTransition = true;
            return;
        }

        if (!hasObservedTransition || now - observedSince < stabilization || now < ManualGraceUntilUtc || Index >= pages.Count)
        {
            return;
        }

        var current = pages[Index];
        if (!StringComparer.OrdinalIgnoreCase.Equals(current.TargetAreaId, areaId))
        {
            return;
        }

        LastReachedAreaId = areaId;
        LastReachedAnchor = Anchor(current);
        Index = Math.Min(Index + 1, pages.Count - 1);
        CurrentAnchor = AnchorAt(pages, Index);
        observedSince = DateTime.MaxValue;
    }

    public void RebaseAfterGuideUpdate(IReadOnlyList<CampaignPage> pages, string? currentAreaId)
    {
        RequireNewTransition();
        if (pages.Count == 0)
        {
            Index = 0;
            CurrentAnchor = null;
            return;
        }

        var reachedIndex = FindAreaNearest(pages, LastReachedAreaId, LastReachedAnchor);
        var currentIndex = -1;
        if (!string.IsNullOrWhiteSpace(currentAreaId))
        {
            if (reachedIndex >= 0)
            {
                currentIndex = Enumerable.Range(reachedIndex, pages.Count - reachedIndex)
                    .FirstOrDefault(
                        index => StringComparer.OrdinalIgnoreCase.Equals(pages[index].TargetAreaId, currentAreaId),
                        -1);
            }
            else
            {
                currentIndex = FindAreaNearest(pages, currentAreaId, CurrentAnchor);
            }
        }

        if (currentIndex >= 0)
        {
            Index = currentIndex;
            CurrentAnchor = AnchorAt(pages, Index);
            return;
        }

        if (reachedIndex >= 0)
        {
            Index = Math.Min(reachedIndex + 1, pages.Count - 1);
            CurrentAnchor = AnchorAt(pages, Index);
            return;
        }

        Rebase(pages);
    }

    public void Rebase(IReadOnlyList<CampaignPage> pages)
    {
        RequireNewTransition();
        if (pages.Count == 0)
        {
            Index = 0;
            CurrentAnchor = null;
            return;
        }

        var currentIndex = Find(pages, CurrentAnchor);
        if (currentIndex >= 0)
        {
            Index = currentIndex;
            return;
        }

        var reachedIndex = Find(pages, LastReachedAnchor);
        if (reachedIndex >= 0)
        {
            Index = Math.Min(reachedIndex + 1, pages.Count - 1);
            CurrentAnchor = AnchorAt(pages, Index);
            return;
        }

        if (CurrentAnchor is not null)
        {
            var nextIndex = pages
                .Select((page, index) => (Page: page, Index: index))
                .FirstOrDefault(item => ComesAfter(item.Page, CurrentAnchor));
            if (nextIndex.Page is not null)
            {
                Index = nextIndex.Index;
                CurrentAnchor = Anchor(nextIndex.Page);
                return;
            }
        }

        Index = Clamp(Index, pages.Count);
        CurrentAnchor = AnchorAt(pages, Index);
    }

    private void RequireNewTransition()
    {
        hasObservedTransition = false;
        observedSince = DateTime.MaxValue;
    }

    private static bool ComesAfter(CampaignPage page, PageAnchor anchor)
    {
        return page.Act > anchor.Act || (page.Act == anchor.Act && page.SourceIndex > anchor.SourceIndex);
    }

    private static int FindFirstTargetAtOrAfter(IReadOnlyList<CampaignPage> pages, string areaId, int startIndex)
    {
        for (var index = Clamp(startIndex, pages.Count); index < pages.Count; index++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(pages[index].TargetAreaId, areaId))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindAreaNearest(
        IReadOnlyList<CampaignPage> pages,
        string? areaId,
        PageAnchor? approximateAnchor)
    {
        if (string.IsNullOrWhiteSpace(areaId))
        {
            return -1;
        }

        var matches = pages
            .Select((page, index) => (Page: page, Index: index))
            .Where(item => StringComparer.OrdinalIgnoreCase.Equals(item.Page.TargetAreaId, areaId));
        if (approximateAnchor is not null)
        {
            matches = matches
                .OrderBy(item => Math.Abs(item.Page.Act - approximateAnchor.Act))
                .ThenBy(item => Math.Abs(item.Page.SourceIndex - approximateAnchor.SourceIndex));
        }

        return matches.Select(item => item.Index).FirstOrDefault(-1);
    }

    private static int Find(IReadOnlyList<CampaignPage> pages, PageAnchor? anchor)
    {
        if (anchor is null)
        {
            return -1;
        }

        for (var index = 0; index < pages.Count; index++)
        {
            if (pages[index].Act == anchor.Act && pages[index].SourceIndex == anchor.SourceIndex)
            {
                return index;
            }
        }

        return -1;
    }

    private static int Clamp(int index, int count)
    {
        return Math.Clamp(index, 0, Math.Max(0, count - 1));
    }

    private static PageAnchor? AnchorAt(IReadOnlyList<CampaignPage> pages, int index)
    {
        return pages.Count == 0 ? null : Anchor(pages[Clamp(index, pages.Count)]);
    }

    private static PageAnchor Anchor(CampaignPage page)
    {
        return new PageAnchor(page.Act, page.SourceIndex);
    }
}
