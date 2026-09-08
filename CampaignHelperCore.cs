// <copyright file="CampaignHelperCore.cs" company="None">Copyright (c) None.</copyright>
namespace CampaignHelper;

using System.Numerics;
using GameHelper;
using GameHelper.Plugin;
using GameHelper.RemoteEnums;
using ImGuiNET;
using Newtonsoft.Json;

public sealed class CampaignHelperCore : PCore<CampaignHelperSettings>
{
    private static readonly Vector4 TaskColor = new(1f, .82f, .38f, 1f);
    private static readonly Vector4 DestinationColor = new(.42f, 1f, .58f, 1f);
    private static readonly Vector4 OptionalColor = new(.78f, .58f, 1f, 1f);
    private static readonly Vector4 TipColor = new(.58f, .74f, .88f, 1f);

    private CampaignGuide? guide;
    private readonly CampaignProgress progress = new();
    private GuideUpdater? updater;
    private UpdateCandidate? candidate;
    private Task<CheckOutcome>? checkTask;
    private string updateStatus = "Not checked";
    private DateTime? lastCheckedUtc;
    private bool showDetails;

    private string SettingsPath => Path.Combine(DllDirectory, "config", "settings.json");

    private string BundledDataDirectory => Path.Combine(DllDirectory, "Data");

    private string ActiveDataDirectory => Path.Combine(DllDirectory, "config", "guide-data");

    private IReadOnlyList<CampaignPage> Pages =>
        guide?.PagesFor(new(Settings.LeagueStart, Settings.IncludeOptional)) ?? [];

    public override void OnEnable(bool isGameOpened)
    {
        checkTask = null;
        candidate = null;
        updateStatus = "Not checked";
        showDetails = false;
        try
        {
            if (File.Exists(SettingsPath))
            {
                Settings = JsonConvert.DeserializeObject<CampaignHelperSettings>(
                    BoundedTextFile.Read(SettingsPath, BoundedTextFile.SmallMaxBytes)) ?? new();
            }
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[CampaignHelper] Settings load failed: {exception.Message}");
        }

        try
        {
            GuideDataBootstrap.EnsureActiveData(BundledDataDirectory, ActiveDataDirectory);
            LoadGuide();
        }
        catch (Exception exception)
        {
            guide = null;
            updateStatus = $"Guide load failed: {exception.Message}";
        }
        var pages = Pages;
        var hasPersistedProgress = Settings.ProgressInitialized ||
            Settings.ProgressIndex != 0 ||
            Settings.LastReachedAreaId is not null ||
            Settings.CurrentAnchorAct.HasValue ||
            Settings.CurrentAnchorSourceIndex.HasValue ||
            Settings.LastReachedAnchorAct.HasValue ||
            Settings.LastReachedAnchorSourceIndex.HasValue;
        progress.Restore(
            Settings.ProgressIndex,
            Settings.LastReachedAreaId,
            pages,
            ReadAnchor(Settings.CurrentAnchorAct, Settings.CurrentAnchorSourceIndex),
            ReadAnchor(Settings.LastReachedAnchorAct, Settings.LastReachedAnchorSourceIndex),
            hasPersistedProgress);
        updater = new GuideUpdater();
        var metadata = ReadMetadata();
        lastCheckedUtc = Settings.LastCheckedUtc ?? metadata?.CheckedUtc;
        MaybeStartAutomaticCheck();
    }

    public override void OnDisable()
    {
        updater?.Dispose();
        updater = null;
        SaveSettings();
        if (!PendingTaskDrain.DrainAndClear(ref checkTask, TimeSpan.FromSeconds(16)))
        {
            throw new TimeoutException("Campaign guide update check did not stop; refusing an unsafe plugin unload.");
        }

        candidate = null;
        updateStatus = "Not checked";
        showDetails = false;
    }

    public override void SaveSettings()
    {
        try
        {
            Settings.ProgressIndex = progress.Index;
            Settings.ProgressInitialized = progress.HasProgressState;
            Settings.LastReachedAreaId = progress.LastReachedAreaId;
            WriteAnchor(progress.CurrentAnchor, out Settings.CurrentAnchorAct, out Settings.CurrentAnchorSourceIndex);
            WriteAnchor(progress.LastReachedAnchor, out Settings.LastReachedAnchorAct, out Settings.LastReachedAnchorSourceIndex);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var temporary = SettingsPath + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            File.Move(temporary, SettingsPath, true);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"[CampaignHelper] Settings save failed: {exception.Message}");
        }
    }

    public override void DrawUI()
    {
        ConsumeCheck();
        MaybeStartAutomaticCheck();
        if (guide is null)
        {
            DrawDetachedUpdateNotice(false);
            return;
        }

        var area = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
        var areaId = area.Id;
        var canDrawCampaign = CampaignUiGate.CanDraw(
            Core.States.GameCurrentState == GameStateTypes.InGameState,
            areaId,
            guide.Areas.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
        if (!canDrawCampaign)
        {
            DrawDetachedUpdateNotice(false);
            return;
        }

        var pages = Pages;
        if (pages.Count == 0)
        {
            return;
        }

        var before = progress.Index;
        var hadProgressState = progress.HasProgressState;
        if (!Settings.Paused)
        {
            progress.Observe(
                areaId,
                DateTime.UtcNow,
                TimeSpan.FromMilliseconds(Math.Clamp(Settings.StabilizationMilliseconds, 100, 5000)),
                pages);
        }

        if (before != progress.Index || hadProgressState != progress.HasProgressState)
        {
            SaveSettings();
        }

        ImGui.SetNextWindowSize(new Vector2(430, 0), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin(PluginText.Title("window.title", "Campaign Helper", "CampaignHelper")))
        {
            ImGui.End();
            return;
        }

        var updateNotice = CampaignUpdateNotice.MessageFor(candidate is not null);
        if (updateNotice is not null)
        {
            ImGui.TextColored(new Vector4(1, .75f, .2f, 1), updateNotice);
            ImGui.Separator();
        }

        var page = pages[Math.Clamp(progress.Index, 0, pages.Count - 1)];
        var level = Core.States.InGameStateObject.CurrentAreaInstance.CurrentAreaLevel;
        ImGui.Text(CampaignUiText.AreaHeader(area.Name, level));
        ImGui.Text(CampaignUiText.PageHeader(page.Act, progress.Index + 1, pages.Count));
        ImGui.Separator();
        var detailedLines = page.DetailedLines ?? page.Lines
            .Select(line => new CampaignGuideLine(line, GuideLineRole.Task))
            .ToArray();
        var taskNumber = 0;
        foreach (var line in detailedLines)
        {
            switch (line.Role)
            {
                case GuideLineRole.Task:
                    DrawColoredWrapped(TaskColor, CampaignUiText.TaskLine(++taskNumber, line.Text));
                    break;
                case GuideLineRole.Destination:
                    DrawColoredWrapped(DestinationColor, CampaignUiText.DestinationLine(++taskNumber, line.Text));
                    break;
                case GuideLineRole.Optional:
                    DrawColoredWrapped(OptionalColor, CampaignUiText.OptionalLine(line.Text));
                    break;
                case GuideLineRole.Tip:
                    ImGui.Indent(16);
                    DrawColoredWrapped(TipColor, CampaignUiText.TipLine(line.Text));
                    ImGui.Unindent(16);
                    break;
            }

            ImGui.Spacing();
        }

        ImGui.Separator();
        var target = page.TargetAreaId is not null && guide.Areas.TryGetValue(page.TargetAreaId, out var targetArea)
            ? targetArea.Name
            : null;
        ImGui.Text(CampaignUiText.Target(target));
        if (ImGui.Button("Back"))
        {
            progress.SetManual(progress.Index - 1, DateTime.UtcNow, TimeSpan.FromSeconds(3), pages);
            SaveSettings();
        }

        ImGui.SameLine();
        if (ImGui.Button("Next"))
        {
            progress.SetManual(progress.Index + 1, DateTime.UtcNow, TimeSpan.FromSeconds(3), pages);
            SaveSettings();
        }

        ImGui.SameLine();
        if (ImGui.Button(Settings.Paused ? "Resume" : "Pause"))
        {
            Settings.Paused = !Settings.Paused;
            SaveSettings();
        }

        ImGui.End();
    }

    private static void DrawColoredWrapped(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    private void DrawDetachedUpdateNotice(bool canDrawCampaign)
    {
        if (!CampaignUpdateNotice.ShouldUseDetachedWindow(candidate is not null, canDrawCampaign))
        {
            return;
        }

        ImGui.SetNextWindowSize(new Vector2(430, 0), ImGuiCond.FirstUseEver);
        if (ImGui.Begin(PluginText.Title("update.window.title", "Campaign Helper Update", "CampaignHelperUpdate")))
        {
            ImGui.TextColored(
                new Vector4(1, .75f, .2f, 1),
                CampaignUpdateNotice.MessageFor(true));
        }

        ImGui.End();
    }

    public override void DrawSettings()
    {
        ConsumeCheck();
        MaybeStartAutomaticCheck();
        if (ImGui.Checkbox("League-start route", ref Settings.LeagueStart))
        {
            FilterChanged();
        }

        if (ImGui.Checkbox("Include optional content", ref Settings.IncludeOptional))
        {
            FilterChanged();
        }

        ImGui.Checkbox("Automatic daily update check", ref Settings.AutomaticDailyUpdateCheck);
        ImGui.SliderInt("Transition stabilization (ms)", ref Settings.StabilizationMilliseconds, 100, 5000);
        if (ImGui.Button("Check now"))
        {
            StartCheck();
        }

        ImGui.SameLine();
        ImGui.Text(updateStatus);
        if (candidate is not null)
        {
            ImGui.TextColored(new Vector4(1, .75f, .2f, 1), "Guide update available");
            if (ImGui.Button("Details"))
            {
                showDetails = !showDetails;
            }

            ImGui.SameLine();
            if (ImGui.Button("Update guide"))
            {
                ApplyCandidate();
            }

            ImGui.SameLine();
            if (ImGui.Button("Later"))
            {
                candidate = null;
            }
        }

        if (showDetails)
        {
            ImGui.TextWrapped(
                $"Installed hash: {InstalledHash() ?? "unavailable"}\n" +
                $"Guide source: {GuideUpdater.GuideUrl}\n" +
                $"Areas source: {GuideUpdater.AreasUrl}\n" +
                $"Last check: {lastCheckedUtc?.ToString("u") ?? "never"}\n" +
                $"Status: {updateStatus}");
        }
    }

    private static PageAnchor? ReadAnchor(int? act, int? sourceIndex)
    {
        return act.HasValue && sourceIndex.HasValue ? new PageAnchor(act.Value, sourceIndex.Value) : null;
    }

    private static void WriteAnchor(PageAnchor? anchor, out int? act, out int? sourceIndex)
    {
        act = anchor?.Act;
        sourceIndex = anchor?.SourceIndex;
    }

    private void FilterChanged()
    {
        progress.Rebase(Pages);
        SaveSettings();
    }

    private void LoadGuide()
    {
        try
        {
            guide = CampaignGuide.Parse(
                BoundedTextFile.Read(Path.Combine(ActiveDataDirectory, "guide.json"), BoundedTextFile.GuideMaxBytes),
                BoundedTextFile.Read(Path.Combine(ActiveDataDirectory, "areas.json"), BoundedTextFile.GuideMaxBytes));
        }
        catch (Exception exception)
        {
            guide = null;
            updateStatus = $"Guide load failed: {exception.Message}";
        }
    }

    private string? InstalledHash()
    {
        return GuideInstallation.InstalledHash(ActiveDataDirectory);
    }

    private GuideMetadata? ReadMetadata()
    {
        try
        {
            var path = Path.Combine(ActiveDataDirectory, "metadata.json");
            return File.Exists(path)
                ? JsonConvert.DeserializeObject<GuideMetadata>(BoundedTextFile.Read(path, BoundedTextFile.SmallMaxBytes))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void StartCheck()
    {
        if (updater is null || checkTask is { IsCompleted: false })
        {
            return;
        }

        updateStatus = CampaignUiText.CheckingStatus;
        checkTask = CheckAsync(updater);
    }

    private void MaybeStartAutomaticCheck()
    {
        if (UpdateSchedule.IsDue(
                Settings.AutomaticDailyUpdateCheck,
                lastCheckedUtc,
                DateTime.UtcNow,
                checkTask is { IsCompleted: false }))
        {
            StartCheck();
        }
    }

    private static async Task<CheckOutcome> CheckAsync(GuideUpdater activeUpdater)
    {
        try
        {
            var result = await activeUpdater.CheckAsync().ConfigureAwait(false);
            return new CheckOutcome(result, null, DateTime.UtcNow);
        }
        catch (Exception exception)
        {
            return new CheckOutcome(null, exception.Message, DateTime.UtcNow);
        }
    }

    private void ConsumeCheck()
    {
        if (checkTask is not { IsCompletedSuccessfully: true })
        {
            return;
        }

        var result = checkTask.Result;
        checkTask = null;
        lastCheckedUtc = result.CheckedUtc;
        Settings.LastCheckedUtc = result.CheckedUtc;
        if (result.Error is not null)
        {
            updateStatus = $"Check failed: {result.Error}";
        }
        else
        {
            candidate = result.Candidate!.Hash == InstalledHash() ? null : result.Candidate;
            updateStatus = candidate is null ? "Guide is current" : "Guide update available";
        }

        SaveSettings();
    }

    private void ApplyCandidate()
    {
        var next = candidate;
        if (next is null)
        {
            return;
        }

        try
        {
            guide = GuideUpdater.Apply(ActiveDataDirectory, next);
            var currentAreaId = Core.States.GameCurrentState == GameStateTypes.InGameState
                ? Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.Id
                : null;
            progress.RebaseAfterGuideUpdate(Pages, currentAreaId);
            candidate = null;
            updateStatus = "Guide updated";
            SaveSettings();
        }
        catch (Exception exception)
        {
            updateStatus = $"Update failed: {exception.Message}";
        }
    }

    private sealed record CheckOutcome(UpdateCandidate? Candidate, string? Error, DateTime CheckedUtc);
}
