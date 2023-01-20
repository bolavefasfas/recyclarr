using Recyclarr.Common.Extensions;
using Recyclarr.TrashLib.Config.Services;
using Recyclarr.TrashLib.Services.ReleaseProfile.Api;
using Recyclarr.TrashLib.Services.ReleaseProfile.Api.Objects;
using Recyclarr.TrashLib.Services.ReleaseProfile.Filters;
using Recyclarr.TrashLib.Services.ReleaseProfile.Guide;
using Recyclarr.TrashLib.Services.Sonarr.Api;
using Recyclarr.TrashLib.Services.Sonarr.Api.Objects;
using Recyclarr.TrashLib.Services.Sonarr.Config;
using Spectre.Console;

namespace Recyclarr.TrashLib.Services.ReleaseProfile;

public class ReleaseProfileUpdater : IReleaseProfileUpdater
{
    private readonly IReleaseProfileApiService _releaseProfileApi;
    private readonly IReleaseProfileFilterPipeline _pipeline;
    private readonly IAnsiConsole _console;
    private readonly IReleaseProfileGuideService _guide;
    private readonly ISonarrTagApiService _tagApiService;
    private readonly ILogger _log;

    public ReleaseProfileUpdater(
        ILogger logger,
        IReleaseProfileGuideService guide,
        ISonarrTagApiService tagApiService,
        IReleaseProfileApiService releaseProfileApi,
        IReleaseProfileFilterPipeline pipeline,
        IAnsiConsole console)
    {
        _log = logger;
        _guide = guide;
        _tagApiService = tagApiService;
        _releaseProfileApi = releaseProfileApi;
        _pipeline = pipeline;
        _console = console;
    }

    public async Task Process(bool isPreview, SonarrConfiguration config)
    {
        var profilesFromGuide = _guide.GetReleaseProfileData();

        var filteredProfiles = new List<(ReleaseProfileData Profile, IReadOnlyCollection<string> Tags)>();

        var configProfiles = config.ReleaseProfiles.SelectMany(x => x.TrashIds.Select(y => (TrashId: y, Config: x)));
        foreach (var (trashId, configProfile) in configProfiles)
        {
            // For each release profile specified in our YAML config, find the matching profile in the guide.
            var selectedProfile = profilesFromGuide.FirstOrDefault(x => x.TrashId.EqualsIgnoreCase(trashId));
            if (selectedProfile is null)
            {
                _log.Warning("A release profile with Trash ID {TrashId} does not exist", trashId);
                continue;
            }

            _log.Debug("Found Release Profile: {ProfileName} ({TrashId})", selectedProfile.Name,
                selectedProfile.TrashId);

            selectedProfile = _pipeline.Process(selectedProfile, configProfile);
            filteredProfiles.Add((selectedProfile, configProfile.Tags));
        }

        if (isPreview)
        {
            PreviewReleaseProfiles(filteredProfiles.Select(x => x.Profile));
            return;
        }

        await ProcessReleaseProfiles(config, filteredProfiles);
    }

    private void PreviewReleaseProfiles(IEnumerable<ReleaseProfileData> profiles)
    {
        var tree = new Tree("Release Profiles [red](Preview)[/]");

        foreach (var profile in profiles)
        {
            PrintTermsAndScores(tree, profile);
        }

        _console.WriteLine();
        _console.Write(tree);
    }

    private void PrintTermsAndScores(Tree tree, ReleaseProfileData profile)
    {
        var rpNode = tree.AddNode($"[yellow]{profile.Name}[/]");

        var incPreferred = profile.IncludePreferredWhenRenaming ? "[green]YES[/]" : "[red]NO[/]";
        rpNode.AddNode($"Include Preferred when Renaming? {incPreferred}");

        PrintTerms(rpNode, "Must Contain", profile.Required);
        PrintTerms(rpNode, "Must Not Contain", profile.Ignored);
        PrintPreferredTerms(rpNode, "Preferred", profile.Preferred);

        _console.WriteLine("");
    }

    private static void PrintTerms(TreeNode tree, string title, IReadOnlyCollection<TermData> terms)
    {
        if (terms.Count == 0)
        {
            return;
        }

        var table = new Table()
            .AddColumn("[bold]Term[/]");

        foreach (var term in terms)
        {
            table.AddRow(Markup.Escape(term.Term));
        }

        tree.AddNode(title)
            .AddNode(table);
    }

    private static void PrintPreferredTerms(TreeNode tree, string title,
        IReadOnlyCollection<PreferredTermData> preferredTerms)
    {
        if (preferredTerms.Count <= 0)
        {
            return;
        }

        var table = new Table()
            .AddColumn("[bold]Score[/]")
            .AddColumn("[bold]Term[/]");

        foreach (var (score, terms) in preferredTerms)
        {
            foreach (var term in terms)
            {
                table.AddRow(score.ToString(), Markup.Escape(term.Term));
            }
        }

        tree.AddNode(title)
            .AddNode(table);
    }

    private async Task ProcessReleaseProfiles(
        IServiceConfiguration config,
        List<(ReleaseProfileData Profile, IReadOnlyCollection<string> Tags)> profilesAndTags)
    {
        // Obtain all of the existing release profiles first. If any were previously created by our program
        // here, we favor replacing those instead of creating new ones, which would just be mostly duplicates
        // (but with some differences, since there have likely been updates since the last run).
        var existingProfiles = await _releaseProfileApi.GetReleaseProfiles(config);

        foreach (var (profile, tags) in profilesAndTags)
        {
            // If tags were provided, ensure they exist. Tags that do not exist are added first, so that we
            // may specify them with the release profile request payload.
            var tagIds = await CreateTagsInSonarr(config, tags);

            var title = BuildProfileTitle(profile.Name);
            var profileToUpdate = GetProfileToUpdate(existingProfiles, title);
            if (profileToUpdate != null)
            {
                _log.Information("Update existing profile: {ProfileName}", title);
                await UpdateExistingProfile(config, profileToUpdate, profile, tagIds);
            }
            else
            {
                _log.Information("Create new profile: {ProfileName}", title);
                await CreateNewProfile(config, title, profile, tagIds);
            }
        }

        // Any profiles with `[Trash]` in front of their name are managed exclusively by Recyclarr. As such, if
        // there are any still in Sonarr that we didn't update, those are most certainly old and shouldn't be kept
        // around anymore.
        await DeleteOldManagedProfiles(config, profilesAndTags, existingProfiles);
    }

    private async Task DeleteOldManagedProfiles(
        IServiceConfiguration config,
        IEnumerable<(ReleaseProfileData Profile, IReadOnlyCollection<string> Tags)> profilesAndTags,
        IEnumerable<SonarrReleaseProfile> sonarrProfiles)
    {
        var profiles = profilesAndTags.Select(x => x.Profile).ToList();
        var sonarrProfilesToDelete = sonarrProfiles
            .Where(sonarrProfile =>
            {
                return sonarrProfile.Name.StartsWithIgnoreCase("[Trash]") &&
                    !profiles.Any(profile => sonarrProfile.Name.EndsWithIgnoreCase(profile.Name));
            });

        foreach (var profile in sonarrProfilesToDelete)
        {
            _log.Information("Deleting old Trash release profile: {ProfileName}", profile.Name);
            await _releaseProfileApi.DeleteReleaseProfile(config, profile.Id);
        }
    }

    private async Task<IReadOnlyCollection<int>> CreateTagsInSonarr(
        IServiceConfiguration config,
        IReadOnlyCollection<string> tags)
    {
        if (!tags.Any())
        {
            return Array.Empty<int>();
        }

        var sonarrTags = await _tagApiService.GetTags(config);
        await CreateMissingTags(config, sonarrTags, tags);
        return sonarrTags
            .Where(t => tags.Any(ct => ct.EqualsIgnoreCase(t.Label)))
            .Select(t => t.Id)
            .ToList();
    }

    private async Task CreateMissingTags(
        IServiceConfiguration config,
        ICollection<SonarrTag> sonarrTags,
        IEnumerable<string> configTags)
    {
        var missingTags = configTags.Where(t => !sonarrTags.Any(t2 => t2.Label.EqualsIgnoreCase(t)));
        foreach (var tag in missingTags)
        {
            _log.Debug("Creating Tag: {Tag}", tag);
            var newTag = await _tagApiService.CreateTag(config, tag);
            sonarrTags.Add(newTag);
        }
    }

    private const string ProfileNamePrefix = "[Trash]";

    private static string BuildProfileTitle(string profileName)
    {
        return $"{ProfileNamePrefix} {profileName}";
    }

    private static SonarrReleaseProfile? GetProfileToUpdate(IEnumerable<SonarrReleaseProfile> profiles,
        string profileName)
    {
        return profiles.FirstOrDefault(p => p.Name == profileName);
    }

    private static void SetupProfileRequestObject(SonarrReleaseProfile profileToUpdate, ReleaseProfileData profile,
        IReadOnlyCollection<int> tagIds)
    {
        profileToUpdate.Preferred = profile.Preferred
            .SelectMany(x => x.Terms.Select(termData => new SonarrPreferredTerm(x.Score, termData.Term)))
            .ToList();

        profileToUpdate.Ignored = profile.Ignored.Select(x => x.Term).ToList();
        profileToUpdate.Required = profile.Required.Select(x => x.Term).ToList();
        profileToUpdate.IncludePreferredWhenRenaming = profile.IncludePreferredWhenRenaming;
        profileToUpdate.Tags = tagIds;
    }

    private async Task UpdateExistingProfile(
        IServiceConfiguration config,
        SonarrReleaseProfile profileToUpdate,
        ReleaseProfileData profile,
        IReadOnlyCollection<int> tagIds)
    {
        _log.Debug("Update existing profile with id {ProfileId}", profileToUpdate.Id);
        SetupProfileRequestObject(profileToUpdate, profile, tagIds);
        await _releaseProfileApi.UpdateReleaseProfile(config, profileToUpdate);
    }

    private async Task CreateNewProfile(
        IServiceConfiguration config,
        string title,
        ReleaseProfileData profile,
        IReadOnlyCollection<int> tagIds)
    {
        var newProfile = new SonarrReleaseProfile {Name = title, Enabled = true};
        SetupProfileRequestObject(newProfile, profile, tagIds);
        await _releaseProfileApi.CreateReleaseProfile(config, newProfile);
    }
}
