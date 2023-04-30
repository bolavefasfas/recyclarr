using Recyclarr.Cli.Pipelines.CustomFormat.Models;
using Recyclarr.Common.Extensions;
using Recyclarr.TrashLib.Config.Services;
using Recyclarr.TrashLib.Models;

namespace Recyclarr.Cli.Pipelines.QualityProfile.PipelinePhases;

public record ProcessedQualityProfileData(QualityProfileConfig Profile)
{
    public Dictionary<int, int> CfScores { get; init; } = new();
}

public class QualityProfileConfigPhase
{
    private readonly ILogger _log;
    private readonly ProcessedCustomFormatCache _cache;

    public QualityProfileConfigPhase(ILogger log, ProcessedCustomFormatCache cache)
    {
        _log = log;
        _cache = cache;
    }

    public IReadOnlyCollection<ProcessedQualityProfileData> Execute(IServiceConfiguration config)
    {
        ProcessLegacyResetUnmatchedScores(config);

        // 1. For each group of CFs that has a quality profile specified
        // 2. For each quality profile score config in that CF group
        // 3. For each CF in the group above, match it to a Guide CF object and pair it with the quality profile config
        var profileAndCfs = config.CustomFormats
            .Where(x => x.QualityProfiles.IsNotEmpty())
            .SelectMany(x => x.QualityProfiles
                .Select(y => (Config: x, Profile: y)))
            .SelectMany(x => x.Config.TrashIds
                .Select(_cache.LookupByTrashId)
                .NotNull()
                .Select(y => (x.Profile, Cf: y)));

        var allProfiles =
            new Dictionary<string, ProcessedQualityProfileData>(StringComparer.InvariantCultureIgnoreCase);

        foreach (var (profile, cf) in profileAndCfs)
        {
            if (!allProfiles.TryGetValue(profile.Name, out var profileCfs))
            {
                profileCfs = new ProcessedQualityProfileData(
                    config.QualityProfiles.FirstOrDefault(
                        x => x.Name.EqualsIgnoreCase(profile.Name),
                        // If the user did not specify a quality profile in their config, we still create the QP object
                        // for consistency (at the very least for the name).
                        new QualityProfileConfig {Name = profile.Name}));
                allProfiles[profile.Name] = profileCfs;
            }

            AddCustomFormatScoreData(profileCfs.CfScores, profile, cf);
        }

        return allProfiles.Values
            .Where(x => x.CfScores.IsNotEmpty())
            .ToList();
    }

    private void ProcessLegacyResetUnmatchedScores(IServiceConfiguration config)
    {
        // todo: Remove this method later; it is for backward compatibility
        var legacyResetUnmatchedScores = config.CustomFormats
            .SelectMany(x => x.QualityProfiles)
            .Where(x => x.ResetUnmatchedScores is not null)
            .ToList();

        if (legacyResetUnmatchedScores.Count > 0)
        {
            _log.Warning(
                "DEPRECATION: Support for using `reset_unmatched_scores` under `custom_formats.quality_profiles` " +
                "will be removed in a future release. Move it to the top level `quality_profiles` instead");
        }

        // Propagate the quality_profile version of ResetUnmatchedScores to the top-level quality_profile config.
        var profilesThatNeedResetUnmatchedScores = legacyResetUnmatchedScores
            .Where(x => x.ResetUnmatchedScores is true)
            .Select(x => x.Name)
            .Distinct(StringComparer.InvariantCultureIgnoreCase);

        var newQualityProfiles = config.QualityProfiles.ToList();

        foreach (var profileName in profilesThatNeedResetUnmatchedScores)
        {
            var match = config.QualityProfiles.FirstOrDefault(x => x.Name.EqualsIgnoreCase(profileName));
            if (match is null)
            {
                _log.Debug(
                    "Root-level quality profile created to promote reset_unmatched_scores from CF score config: {Name}",
                    profileName);
                newQualityProfiles.Add(new QualityProfileConfig {Name = profileName, ResetUnmatchedScores = true});
            }
            else if (match.ResetUnmatchedScores is null)
            {
                _log.Debug(
                    "Score-based reset_unmatched_scores propagated to existing root-level " +
                    "quality profile config: {Name}", profileName);
                match.ResetUnmatchedScores = true;
            }
        }

        // Down-cast to avoid having to make the property mutable in the interface
        ((ServiceConfiguration) config).QualityProfiles = newQualityProfiles;
    }

    private void AddCustomFormatScoreData(
        IDictionary<int, int> existingScoreData,
        QualityProfileScoreConfig profile,
        CustomFormatData cf)
    {
        var scoreToUse = profile.Score ?? cf.TrashScore;
        if (scoreToUse is null)
        {
            _log.Information("No score in guide or config for CF {Name} ({TrashId})", cf.Name, cf.TrashId);
            return;
        }

        if (existingScoreData.TryGetValue(cf.Id, out var existingScore))
        {
            if (existingScore != scoreToUse)
            {
                _log.Warning(
                    "Custom format {Name} ({TrashId}) is duplicated in quality profile {ProfileName} with a score " +
                    "of {NewScore}, which is different from the original score of {OriginalScore}",
                    cf.Name, cf.TrashId, profile.Name, scoreToUse, existingScore);
            }
            else
            {
                _log.Debug("Skipping duplicate score for {Name} ({TrashId})", cf.Name, cf.TrashId);
            }

            return;
        }

        existingScoreData.Add(cf.Id, scoreToUse.Value);
    }
}