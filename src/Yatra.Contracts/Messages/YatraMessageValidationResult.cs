namespace Yatra.Contracts.Messages;

public sealed record YatraMessageValidationResult(
    IReadOnlyList<YatraMessageValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static YatraMessageValidationResult Success { get; } = new([]);
}
