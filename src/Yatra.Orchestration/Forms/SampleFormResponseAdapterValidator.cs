using Yatra.AgentIntegration;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Routing;

namespace Yatra.Orchestration.Forms;

public sealed class SampleFormResponseAdapterValidator : IFormResponseValidator
{
    public ValueTask<bool> IsValidAsync(
        FormResponsePayload response,
        PendingReply pendingReply,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(SampleFormResponseValidator.Validate(response).IsValid);
    }
}
