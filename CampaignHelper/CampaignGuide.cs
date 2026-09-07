// <copyright file="CampaignGuide.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public sealed record GuideFilter(bool LeagueStart, bool Optional);

public sealed record CampaignArea(string Id, string Name, int Act);

public enum GuideLineRole
{
    Task,
    Destination,
    Optional,
    Tip,
}

public sealed record CampaignGuideLine(string Text, GuideLineRole Role);

public sealed record CampaignPage(
    int Act,
    int SourceIndex,
    IReadOnlyList<string> Lines,
    string? TargetAreaId,
    IReadOnlyList<CampaignGuideLine>? DetailedLines = null);

public sealed class CampaignGuide
{
    private static readonly Regex FormattingToken = new(
        @"\((?:color|hint)(?::[^)]*)?\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex ImageToken = new(
        @"\(img:([^)]*)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);


    private static readonly Regex AngleTag = new(
        @"<([^>]*)>",
        RegexOptions.Compiled);

    private static readonly Regex AreaToken = new(
        @"\bareaid([a-z0-9_]+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Qualifier = new(
        @"^\s*(leaguestart|twinkrun|optional)\s*:\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly JArray guide;
    private readonly Dictionary<GuideFilter, IReadOnlyList<CampaignPage>> pagesByFilter = new();

    private CampaignGuide(JArray guide, IReadOnlyDictionary<string, CampaignArea> areas)
    {
        this.guide = guide;
        Areas = areas;
    }

    public IReadOnlyDictionary<string, CampaignArea> Areas { get; }

    public static CampaignGuide Parse(string guideJson, string areasJson)
    {
        var guide = ParseArray(guideJson, "guide");
        var areaActs = ParseArray(areasJson, "areas");
        if (guide.Count == 0 || areaActs.Count == 0 || guide.Count > 20 || areaActs.Count > 20)
        {
            throw new InvalidDataException("Empty or excessive campaign data.");
        }

        var areas = ParseAreas(areaActs);
        var result = new CampaignGuide(guide, areas);
        var filters = new[]
        {
            new GuideFilter(true, true),
            new GuideFilter(true, false),
            new GuideFilter(false, true),
            new GuideFilter(false, false),
        };

        foreach (var filter in filters)
        {
            _ = result.PagesFor(filter);
        }

        if (!result.PagesFor(new(true, true)).Any() || !result.PagesFor(new(false, true)).Any())
        {
            throw new InvalidDataException("Guide has no pages.");
        }

        return result;
    }

    public IReadOnlyList<CampaignPage> PagesFor(GuideFilter filter)
    {
        if (!pagesByFilter.TryGetValue(filter, out var pages))
        {
            pages = BuildPages(filter).ToArray();
            pagesByFilter.Add(filter, pages);
        }

        return pages;
    }

    private IEnumerable<CampaignPage> BuildPages(GuideFilter filter)
    {
        for (var actIndex = 0; actIndex < guide.Count; actIndex++)
        {
            if (guide[actIndex] is not JArray act || act.Count == 0 || act.Count > 1000)
            {
                throw new InvalidDataException("Invalid guide act.");
            }

            for (var pageIndex = 0; pageIndex < act.Count; pageIndex++)
            {
                var raw = GetPageLines(act[pageIndex], filter);
                if (raw is null)
                {
                    continue;
                }

                var lines = new List<string>();
                var classified = new List<(string Text, GuideLineRole Role, string[] References)>();
                string? target = null;
                foreach (var token in raw)
                {
                    var original = token.Type == JTokenType.String
                        ? token.Value<string>()!
                        : throw new InvalidDataException("Guide lines must be strings.");
                    if (original.Length > 4096)
                    {
                        throw new InvalidDataException("Guide line exceeds the display limit.");
                    }

                    var routed = ApplyQualifiers(original, filter);
                    if (routed is null)
                    {
                        continue;
                    }

                    var isHint = IsHintLine(original);
                    var isOptional = HasQualifier(original, "optional");
                    var references = AreaToken.Matches(routed).Select(match => match.Groups[1].Value).ToArray();
                    if (!isHint)
                    {
                        foreach (var areaId in references)
                        {
                            if (!Areas.ContainsKey(areaId))
                            {
                                throw new InvalidDataException($"Unknown target area '{areaId}'.");
                            }
                        }

                        if (!isOptional)
                        {
                            target = references.LastOrDefault() ?? target;
                        }
                    }

                    var clean = Sanitize(routed, Areas);
                    if (clean.Length > 0)
                    {
                        lines.Add(clean);
                        var role = isHint
                            ? GuideLineRole.Tip
                            : isOptional
                                ? GuideLineRole.Optional
                                : GuideLineRole.Task;
                        var display = role == GuideLineRole.Optional
                            ? Regex.Replace(
                                Regex.Replace(clean, @"(?:^|\s)\[Optional\](?=\s|$)", string.Empty),
                                @"\s+",
                                " ").Trim()
                            : clean;
                        classified.Add((display, role, references));
                    }
                }

                var destinationIndex = target is null
                    ? -1
                    : classified.FindLastIndex(line =>
                        line.Role == GuideLineRole.Task &&
                        line.References.Any(reference => reference.Equals(target, StringComparison.OrdinalIgnoreCase)));
                var detailed = classified
                    .Select((line, index) => new CampaignGuideLine(
                        line.Text,
                        index == destinationIndex ? GuideLineRole.Destination : line.Role))
                    .ToArray();
                yield return new CampaignPage(actIndex + 1, pageIndex, lines, target, detailed);
            }
        }
    }

    public static string Sanitize(string value, IReadOnlyDictionary<string, CampaignArea> areas)
    {
        var withoutAreaSuffix = Regex.Replace(value, @"\s*;;.*$", string.Empty);
        var withoutFormatting = FormattingToken.Replace(withoutAreaSuffix, string.Empty);
        withoutFormatting = ImageToken.Replace(withoutFormatting, match => RenderImage(match.Groups[1].Value));
        withoutFormatting = ReplaceQuestTokens(withoutFormatting);
        withoutFormatting = AngleTag.Replace(withoutFormatting, match => HumanizeIdentifier(match.Groups[1].Value));
        withoutFormatting = Regex.Replace(withoutFormatting, @"\(edge\)", "along edge", RegexOptions.IgnoreCase);
        withoutFormatting = withoutFormatting.Replace("||", " | ");
        var withAreaNames = AreaToken.Replace(withoutFormatting, match =>
        {
            var id = match.Groups[1].Value;
            return areas.TryGetValue(id, out var area) ? HumanizeIdentifier(area.Name) : string.Empty;
        });
        var clean = withAreaNames.Replace('_', ' ');
        clean = Regex.Replace(clean, @"\bfollow 2\b", "follow route 2", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s+([,:])", "$1");
        clean = Regex.Replace(clean, @"([,:])(?=\S)", "$1 ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', ':', ',');
        return Regex.Replace(
            clean,
            @"^((?:\[[^]]+\]\s*)*)([a-z])",
            match => match.Groups[1].Value + char.ToUpperInvariant(match.Groups[2].Value[0]));
    }

    private static string RenderImage(string value)
    {
        var label = value.ToLowerInvariant() switch
        {
            "checkpoint" => "Checkpoint",
            "quest_2" => "Quest",
            "waypoint" => "Waypoint",
            "portal" => "Portal",
            "support" or "support2" => "Support Gem",
            "skill" or "skill2" => "Skill Gem",
            "flasks" => "Flasks",
            "in-out2" => "Exit",
            "spirit2" => "Spirit Gem",
            "exa" => "Exalted Orb",
            "jeweller" => "Jeweller's Orb",
            "artificer" => "Artificer's Orb",
            "gcp" => "Gemcutter's Prism",
            "b-rune" or "rune" => "Rune",
            "regal" => "Regal Orb",
            "town" => "Town",
            "0" or "1" or "2" or "3" or "5" or "6" or "7" or "arena" => "Route",
            _ => HumanizeIdentifier(value),
        };
        return $"[{label}]";
    }

    private static string ReplaceQuestTokens(string value)
    {
        const string prefix = "(quest:";
        var offset = 0;
        StringBuilder? output = null;
        while (true)
        {
            var start = value.IndexOf(prefix, offset, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                if (output is null)
                {
                    return value;
                }

                output.Append(value, offset, value.Length - offset);
                return output.ToString();
            }

            output ??= new StringBuilder(value.Length);
            output.Append(value, offset, start - offset);
            var contentStart = start + prefix.Length;
            var depth = 0;
            var end = -1;
            for (var index = contentStart; index < value.Length; index++)
            {
                if (value[index] == '(')
                {
                    depth++;
                }
                else if (value[index] == ')' && depth > 0)
                {
                    depth--;
                }
                else if (value[index] == ')')
                {
                    end = index;
                    break;
                }
            }

            if (end < 0)
            {
                throw new InvalidDataException("Malformed quest marker.");
            }

            var label = HumanizeIdentifier(value[contentStart..end]);
            if (label.Length == 0)
            {
                throw new InvalidDataException("Empty quest marker.");
            }

            output.Append('[').Append(label).Append(']');
            offset = end + 1;
        }
    }

    private static string HumanizeIdentifier(string value)
    {
        var words = Regex.Replace(value.Trim('(', ')', '?').Replace('_', ' '), @"\s+", " ").Trim();
        return string.Join(
            " ",
            words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
                word.ToLowerInvariant() switch
                {
                    "atk" => "Attack",
                    "weap" => "Weapon",
                    _ => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant(),
                }));
    }

    private static IReadOnlyDictionary<string, CampaignArea> ParseAreas(JArray areaActs)
    {
        var areas = new Dictionary<string, CampaignArea>(StringComparer.OrdinalIgnoreCase);
        for (var actIndex = 0; actIndex < areaActs.Count; actIndex++)
        {
            if (areaActs[actIndex] is not JArray act || act.Count == 0 || act.Count > 500)
            {
                throw new InvalidDataException("Invalid areas act.");
            }

            foreach (var item in act)
            {
                if (item is not JObject value || value.Properties().Any(property => property.Name is not ("id" or "name" or "recommendation")))
                {
                    throw new InvalidDataException("Unknown area shape.");
                }

                var rawId = value.Value<string>("id");
                var rawName = value.Value<string>("name");
                if (rawId?.Length > 128 || rawName?.Length > 512)
                {
                    throw new InvalidDataException("Area display data exceeds its limit.");
                }

                var id = rawId?.Trim();
                var name = rawName?.Trim();
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !areas.TryAdd(id, new(id, name, actIndex + 1)))
                {
                    throw new InvalidDataException("Area IDs must be unique and nonblank.");
                }
            }
        }

        return areas;
    }

    private static JArray? GetPageLines(JToken node, GuideFilter filter)
    {
        if (node is JArray lines)
        {
            ValidateLines(lines);
            return lines;
        }

        if (node is not JObject value || value.Properties().Any(property => property.Name is not ("condition" or "lines")))
        {
            throw new InvalidDataException("Unknown conditional page shape.");
        }

        if (value["condition"] is not JArray condition || condition.Count != 2 ||
            condition[0]?.Type != JTokenType.String || condition[1]?.Type != JTokenType.String ||
            !string.Equals(condition[0]!.Value<string>(), "league-start", StringComparison.Ordinal) ||
            condition[1]!.Value<string>() is not ("yes" or "no"))
        {
            throw new InvalidDataException("Unknown conditional page shape.");
        }

        if (value["lines"] is not JArray conditionalLines)
        {
            throw new InvalidDataException("Unknown conditional page shape.");
        }

        ValidateLines(conditionalLines);
        var leagueStartPage = condition[1]!.Value<string>() == "yes";
        return leagueStartPage == filter.LeagueStart ? conditionalLines : null;
    }

    private static void ValidateLines(JArray lines)
    {
        if (lines.Count == 0 || lines.Count > 100)
        {
            throw new InvalidDataException("Invalid guide page.");
        }
    }

    private static string? ApplyQualifiers(string value, GuideFilter filter)
    {
        var remaining = value;
        var labels = new List<string>();
        while (Qualifier.Match(remaining) is { Success: true } match)
        {
            var qualifier = match.Groups[1].Value;
            if ((qualifier.Equals("leaguestart", StringComparison.OrdinalIgnoreCase) && !filter.LeagueStart) ||
                (qualifier.Equals("twinkrun", StringComparison.OrdinalIgnoreCase) && filter.LeagueStart) ||
                (qualifier.Equals("optional", StringComparison.OrdinalIgnoreCase) && !filter.Optional))
            {
                return null;
            }

            var label = qualifier.ToLowerInvariant() switch
            {
                "optional" => "[Optional]",
                "leaguestart" => "[League Start]",
                "twinkrun" => "[Twink]",
                _ => string.Empty,
            };
            if (label.Length > 0 && !labels.Contains(label, StringComparer.Ordinal))
            {
                labels.Add(label);
            }

            remaining = remaining[match.Length..];
        }

        return labels.Count == 0 ? remaining : string.Join(" ", labels) + " " + remaining;
    }

    private static bool IsHintLine(string value)
    {
        var remaining = value;
        while (Qualifier.Match(remaining) is { Success: true } match)
        {
            remaining = remaining[match.Length..];
        }

        return remaining.TrimStart().StartsWith("(hint)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasQualifier(string value, string expected)
    {
        var remaining = value;
        while (Qualifier.Match(remaining) is { Success: true } match)
        {
            if (match.Groups[1].Value.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            remaining = remaining[match.Length..];
        }

        return false;
    }

    private static JArray ParseArray(string json, string name)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 2_000_000)
        {
            throw new InvalidDataException($"Invalid {name} size.");
        }

        using var reader = new JsonTextReader(new StringReader(json))
        {
            MaxDepth = 16,
            DateParseHandling = DateParseHandling.None,
        };
        try
        {
            var result = JToken.ReadFrom(reader) as JArray
                ?? throw new InvalidDataException($"{name} must be an array.");
            if (reader.Read())
            {
                throw new InvalidDataException($"{name} has trailing content.");
            }

            return result;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Invalid {name} JSON.", exception);
        }
    }
}
