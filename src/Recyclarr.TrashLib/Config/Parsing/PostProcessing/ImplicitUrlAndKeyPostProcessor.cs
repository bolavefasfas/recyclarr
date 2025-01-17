using Recyclarr.TrashLib.Config.Secrets;

namespace Recyclarr.TrashLib.Config.Parsing.PostProcessing;

public class ImplicitUrlAndKeyPostProcessor : IConfigPostProcessor
{
    private readonly ILogger _log;
    private readonly ISecretsProvider _secrets;

    public ImplicitUrlAndKeyPostProcessor(ILogger log, ISecretsProvider secrets)
    {
        _log = log;
        _secrets = secrets;
    }

    public RootConfigYaml Process(RootConfigYaml config)
    {
        return new RootConfigYaml
        {
            Radarr = ProcessService(config.Radarr),
            Sonarr = ProcessService(config.Sonarr)
        };
    }

    private IReadOnlyDictionary<string, T>? ProcessService<T>(IReadOnlyDictionary<string, T>? services)
        where T : ServiceConfigYaml
    {
        if (services is null)
        {
            return null;
        }

        var updatedServices = new Dictionary<string, T>();
        foreach (var (name, service) in services)
        {
            updatedServices.Add(name, FillUrlAndKey(name, service));
        }

        return updatedServices;
    }

    private T FillUrlAndKey<T>(string instanceName, T config)
        where T : ServiceConfigYaml
    {
        return config with
        {
            ApiKey = config.ApiKey ?? GetSecret(instanceName, "api_key"),
            BaseUrl = config.BaseUrl ?? GetSecret(instanceName, "base_url")
        };
    }

    private string? GetSecret(string instanceName, string property)
    {
        _log.Debug("Obtain {Property} implicitly for instance {InstanceName}", property, instanceName);
        return _secrets.Secrets.GetValueOrDefault($"{instanceName}_{property}");
    }
}
