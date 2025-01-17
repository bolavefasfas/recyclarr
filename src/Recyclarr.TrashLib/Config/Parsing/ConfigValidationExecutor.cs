using JetBrains.Annotations;
using Recyclarr.Common.FluentValidation;

namespace Recyclarr.TrashLib.Config.Parsing;

[UsedImplicitly]
public class ConfigValidationExecutor
{
    private readonly ILogger _log;
    private readonly IRuntimeValidationService _validationService;

    public ConfigValidationExecutor(ILogger log, IRuntimeValidationService validationService)
    {
        _log = log;
        _validationService = validationService;
    }

    public bool Validate(object config)
    {
        var result = _validationService.Validate(config);
        if (result.IsValid)
        {
            return true;
        }

        var numErrors = result.Errors.LogValidationErrors(_log, "Config Validation");
        if (numErrors == 0)
        {
            return true;
        }

        _log.Error("Config validation failed with {Count} errors", numErrors);
        return false;
    }
}
