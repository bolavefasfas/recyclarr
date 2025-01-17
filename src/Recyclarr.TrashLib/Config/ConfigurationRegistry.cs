using System.IO.Abstractions;
using Recyclarr.TrashLib.Config.Parsing;
using Recyclarr.TrashLib.Config.Parsing.ErrorHandling;
using Recyclarr.TrashLib.Config.Services;
using Recyclarr.TrashLib.ExceptionTypes;

namespace Recyclarr.TrashLib.Config;

public class ConfigurationRegistry : IConfigurationRegistry
{
    private readonly IConfigurationLoader _loader;
    private readonly IConfigurationFinder _finder;
    private readonly IFileSystem _fs;

    public ConfigurationRegistry(IConfigurationLoader loader, IConfigurationFinder finder, IFileSystem fs)
    {
        _loader = loader;
        _finder = finder;
        _fs = fs;
    }

    public IReadOnlyCollection<IServiceConfiguration> FindAndLoadConfigs(ConfigFilterCriteria? filterCriteria = null)
    {
        filterCriteria ??= new ConfigFilterCriteria();

        var manualConfigs = filterCriteria.ManualConfigFiles;
        var configs = manualConfigs is not null && manualConfigs.Any()
            ? PrepareManualConfigs(manualConfigs)
            : _finder.GetConfigFiles();

        return LoadAndFilterConfigs(configs, filterCriteria).ToList();
    }

    private IReadOnlyCollection<IFileInfo> PrepareManualConfigs(IEnumerable<string> manualConfigs)
    {
        var configFiles = manualConfigs
            .Select(x => _fs.FileInfo.New(x))
            .ToLookup(x => x.Exists);

        if (configFiles[false].Any())
        {
            throw new InvalidConfigurationFilesException(configFiles[false].ToList());
        }

        return configFiles[true].ToList();
    }

    private IEnumerable<IServiceConfiguration> LoadAndFilterConfigs(
        IEnumerable<IFileInfo> configs,
        ConfigFilterCriteria filterCriteria)
    {
        var loadedConfigs = configs.SelectMany(x => _loader.Load(x)).ToList();

        var invalidInstances = loadedConfigs.GetInvalidInstanceNames(filterCriteria).ToList();
        if (invalidInstances.Any())
        {
            throw new InvalidInstancesException(invalidInstances);
        }

        var splitInstances = loadedConfigs.GetSplitInstances().ToList();
        if (splitInstances.Any())
        {
            throw new SplitInstancesException(splitInstances);
        }

        return loadedConfigs.GetConfigsBasedOnSettings(filterCriteria);
    }
}
