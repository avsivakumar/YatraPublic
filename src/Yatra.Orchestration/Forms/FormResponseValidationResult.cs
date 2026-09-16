namespace Yatra.Orchestration.Forms;

public sealed record FormResponseValidationResult(
    IReadOnlyList<FormResponseValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0;

    public static FormResponseValidationResult Success { get; } = new([]);
}
