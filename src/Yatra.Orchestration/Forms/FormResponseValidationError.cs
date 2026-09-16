namespace Yatra.Orchestration.Forms;

public sealed record FormResponseValidationError(
    string Field,
    string Code,
    string Message);
