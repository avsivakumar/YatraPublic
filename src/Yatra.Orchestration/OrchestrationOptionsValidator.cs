using Microsoft.Extensions.Options;

namespace Yatra.Orchestration;

public sealed class OrchestrationOptionsValidator : IValidateOptions<OrchestrationOptions>
{
    public ValidateOptionsResult Validate(string? name, OrchestrationOptions options)
    {
        var failures = new List<string>();

        if (!string.Equals(options.Provider, "Fake", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Provider must be either Fake or OpenAI.");
        }

        if (string.Equals(options.Provider, "OpenAI", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add("Model is required when Provider is OpenAI.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            failures.Add("TimeoutSeconds must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
