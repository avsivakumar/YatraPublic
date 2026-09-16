namespace Yatra.Contracts.Messages;

public sealed record YatraMessageValidationError(
    string Field,
    string Code,
    string Message);
