// <copyright file="CampaignUiText.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

public static class CampaignUiText
{
    public const string CheckingStatus = "Checking...";

    public static string AreaHeader(string areaName, int level) => $"{areaName} | Level {level}";

    public static string PageHeader(int act, int page, int total) => $"Act {act} | Page {page}/{total}";

    public static string GuideLine(string line) => $"- {line}";

    public static string TaskLine(int number, string line) => $"{number}. {line}";

    public static string DestinationLine(int number, string line) => $"{number}. GOAL: {line}";

    public static string OptionalLine(string line) => $"+ OPTIONAL: {line}";

    public static string TipLine(string line) => $"Tip: {line}";

    public static string Target(string? areaName) => $"Target: {areaName ?? "none"}";
}
