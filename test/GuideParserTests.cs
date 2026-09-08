namespace CampaignHelper.Tests;

using Xunit;

public sealed class GuideParserTests
{
    private const string Areas = "[[{\"id\":\"g1_1\",\"name\":\"Riverbank\"},{\"id\":\"g1_2\",\"name\":\"Clearfell\"}]]";

    [Fact]
    public void ParsesConditionsFiltersKindsSanitizesAndExtractsNonHintTarget()
    {
        const string guide = "[[[\"optional: (img:skill) Get_it\",\"twinkrun: fast\",\"enter areaidg1_2 ;; Clearfell\",\"(hint)__ areaidmissing\"],{\"condition\":[\"league-start\",\"yes\"],\"lines\":[\"leaguestart: (color:red)buy\"]},{\"condition\":[\"league-start\",\"no\"],\"lines\":[\"normal\"]}]]";
        var parsed = CampaignGuide.Parse(guide, Areas);

        var league = parsed.PagesFor(new GuideFilter(true, true)).ToArray();
        Assert.Equal(2, league.Length);
        Assert.Contains("Get it", league[0].Lines[0]);
        Assert.DoesNotContain(league[0].Lines, x => x.Contains("fast"));
        Assert.Equal("g1_2", league[0].TargetAreaId);
        Assert.Contains("Buy", league[1].Lines.Single());

        var normal = parsed.PagesFor(new GuideFilter(false, false)).ToArray();
        Assert.DoesNotContain(normal[0].Lines, x => x.Contains("Get it"));
        Assert.Contains("Normal", normal[1].Lines.Single());
    }

    [Fact]
    public void RejectsUnknownAreaReferenceInAnyIncludedLineAndInvalidConditions()
    {
        Assert.Throws<InvalidDataException>(() => CampaignGuide.Parse("[[[\"visit areaidmissing\",\"enter areaidg1_1\"]]]", Areas));
        Assert.Throws<InvalidDataException>(() => CampaignGuide.Parse("[[{\"condition\":[\"league-start\",\"maybe\"],\"lines\":[\"enter areaidg1_1\"]}]]", Areas));
        Assert.Throws<InvalidDataException>(() => CampaignGuide.Parse("[[{\"condition\":[\"unknown\",\"yes\"],\"lines\":[\"enter areaidg1_1\"]}]]", Areas));
    }

    [Fact]
    public void DoesNotRequireHintOnlyAreaIdsAfterRoutingQualifiers()
    {
        const string guide = "[[[\"optional: (hint) areaidnot_a_real_area\",\"enter areaidg1_1\"]]]";

        var parsed = CampaignGuide.Parse(guide, Areas);

        Assert.Single(parsed.PagesFor(new(true, true)));
    }

    [Fact]
    public void AppliesEveryLeadingQualifierAndRemovesThemFromDisplay()
    {
        const string guide = "[[[\"leaguestart: optional: check vendors\",\"twinkrun: optional: twink task\",\"enter areaidg1_1\"]]]";
        var parsed = CampaignGuide.Parse(guide, Areas);

        Assert.DoesNotContain(parsed.PagesFor(new(true, false)).Single().Lines, line => line.Contains("vendors"));
        Assert.Contains("[League Start] [Optional] Check vendors", parsed.PagesFor(new(true, true)).Single().Lines);
        Assert.Contains("[Twink] [Optional] Twink task", parsed.PagesFor(new(false, true)).Single().Lines);
        Assert.DoesNotContain(parsed.PagesFor(new(false, true)).Single().Lines, line => line.Contains("leaguestart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RendersScreenshotGuideMarkupAsReadableAsciiText()
    {
        const string areas = "[[{\"id\":\"g1_5\",\"name\":\"the red vale\"},{\"id\":\"g1_6\",\"name\":\"the grim tangle\"}]]";
        const string guide = "[[[\"optional: ward rune: (color:cc99ff)league\",\"optional: (img:flasks) + (img:support) <witch>: (img:checkpoint) (color:cc99ff)hut || (img:skill) arena:bramble\",\"(color:ff00ff)follow_2: glow-roots to get (img:waypoint) ,\",\"(hint)__ (color:aqua)mushrooms to (img:in-out2) areaidg1_6: (img:waypoint) ;; the grim tangle\",\"follow river upstream: (img:checkpoint) areaidg1_5 ;; the red vale\"]]]";

        var lines = CampaignGuide.Parse(guide, areas).PagesFor(new(true, true)).Single().Lines;

        Assert.Equal(
            new[]
            {
                "[Optional] Ward rune: league",
                "[Optional] [Flasks] + [Support Gem] Witch: [Checkpoint] hut | [Skill Gem] arena: bramble",
                "Follow route 2: glow-roots to get [Waypoint]",
                "Mushrooms to [Exit] The Grim Tangle: [Waypoint]",
                "Follow river upstream: [Checkpoint] The Red Vale",
            },
            lines);
        Assert.All(lines.SelectMany(line => line), character => Assert.InRange((int)character, 32, 126));
    }

    [Fact]
    public void ClassifiesTasksTipsOptionalStepsAndDestination()
    {
        const string areas = "[[{\"id\":\"g1_5\",\"name\":\"the red vale\"},{\"id\":\"g1_6\",\"name\":\"the grim tangle\"}]]";
        const string guide = "[[[\"optional: loot the hut\",\"follow roots to get (img:waypoint)\",\"(hint)__ mushrooms point to areaidg1_6\",\"follow river to areaidg1_5\"]]]";

        var page = CampaignGuide.Parse(guide, areas).PagesFor(new(true, true)).Single();

        Assert.Equal(
            new[]
            {
                GuideLineRole.Optional,
                GuideLineRole.Task,
                GuideLineRole.Tip,
                GuideLineRole.Destination,
            },
            page.DetailedLines!.Select(line => line.Role));
        Assert.Equal("Loot the hut", page.DetailedLines![0].Text);
        Assert.Equal("Mushrooms point to The Grim Tangle", page.DetailedLines[2].Text);
    }

    [Fact]
    public void MarksOnlyTheFinalMatchingTargetLineAsDestination()
    {
        const string guide = "[[[\"visit areaidg1_1\",\"do the task\",\"return to areaidg1_1\"]]]";

        var lines = CampaignGuide.Parse(guide, Areas).PagesFor(new(true, true)).Single().DetailedLines!;

        Assert.Equal(GuideLineRole.Task, lines[0].Role);
        Assert.Equal(GuideLineRole.Destination, lines[2].Role);
    }

    [Fact]
    public void OptionalAreaDoesNotReplaceRequiredDestination()
    {
        const string guide = "[[[\"enter areaidg1_1\",\"optional: visit areaidg1_2\"]]]";

        var page = CampaignGuide.Parse(guide, Areas).PagesFor(new(true, true)).Single();

        Assert.Equal("g1_1", page.TargetAreaId);
        Assert.Equal(GuideLineRole.Destination, page.DetailedLines![0].Role);
        Assert.Equal(GuideLineRole.Optional, page.DetailedLines[1].Role);
    }

    [Fact]
    public void RemovesOptionalLabelWhenAnotherQualifierComesFirst()
    {
        const string guide = "[[[\"leaguestart: optional: check vendors\",\"enter areaidg1_1\"]]]";

        var optional = CampaignGuide.Parse(guide, Areas).PagesFor(new(true, true)).Single().DetailedLines![0];

        Assert.Equal(GuideLineRole.Optional, optional.Role);
        Assert.Equal("[League Start] Check vendors", optional.Text);
    }

    [Fact]
    public void RendersAreaNamesAndRemovesAllFormattingMarkers()
    {
        const string guide = "[[[\"(quest:foo) Go from areaidg1_1 to (color:red)areaidg1_2 (img:x) (hint)\"]]]";
        var page = CampaignGuide.Parse(guide, Areas).PagesFor(new(true, true)).Single();

        Assert.Equal("g1_2", page.TargetAreaId);
        Assert.Equal("[Foo] Go from Riverbank to Clearfell [X]", page.Lines.Single());
        Assert.DoesNotContain("areaid", page.Lines.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HumanizesNestedQuestMarkers()
    {
        Assert.Equal("[Book]", CampaignGuide.Sanitize("(quest:(book))", new Dictionary<string, CampaignArea>()));
    }

    [Fact]
    public void RejectsMalformedQuestMarkerWithoutRegexBacktracking()
    {
        var malformed = "[[[\"(quest:" + new string('(', 4000) + "\"]]]";

        Assert.Throws<InvalidDataException>(() => CampaignGuide.Parse(malformed, Areas));
    }

    [Fact]
    public void RejectsGuideLineOverDisplayLimit()
    {
        var guide = "[[[\"" + new string('x', 4097) + "\"]]]";

        Assert.Throws<InvalidDataException>(() => CampaignGuide.Parse(guide, Areas));
    }

    [Fact]
    public void RejectsAreaIdOverDisplayLimit()
    {
        var areas = "[[{\"id\":\"" + new string('a', 129) + "\",\"name\":\"A\"}]]";

        Assert.Throws<InvalidDataException>(() => CampaignGuide.Parse("[[[\"go\"]]]", areas));
    }

    [Fact]
    public void RejectsAreaNameOverDisplayLimit()
    {
        var areas = "[[{\"id\":\"a\",\"name\":\"" + new string('n', 513) + "\"}]]";

        Assert.Throws<InvalidDataException>(() => CampaignGuide.Parse("[[[\"go\"]]]", areas));
    }

    [Fact]
    public void RejectsTrailingContentAfterTopLevelArray()
    {
        Assert.Throws<InvalidDataException>(() =>
            CampaignGuide.Parse("[[[\"go\"]]] {}", Areas));
        Assert.Throws<InvalidDataException>(() =>
            CampaignGuide.Parse("[[[\"go\"]]]", Areas + " trailing"));
    }

    [Fact]
    public void CachesOneMaterializedPageListPerFilter()
    {
        const string guide = "[[[\"optional: task\",\"enter areaidg1_1\"]]]";
        var parsed = CampaignGuide.Parse(guide, Areas);

        var first = parsed.PagesFor(new(true, true));
        var repeated = parsed.PagesFor(new(true, true));
        var withoutOptional = parsed.PagesFor(new(true, false));

        Assert.Same(first, repeated);
        Assert.NotSame(first, withoutOptional);
        Assert.Contains("[Optional] Task", first.Single().Lines);
        Assert.DoesNotContain(withoutOptional.Single().Lines, line => line.Contains("Task"));
    }
}
