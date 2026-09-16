using Yatra.Contracts.Forms;
using Yatra.Contracts.Routing;

namespace Yatra.AgentIntegration;

public interface IFormResponseValidator
{
    ValueTask<bool> IsValidAsync(
        FormResponsePayload response,
        PendingReply pendingReply,
        CancellationToken cancellationToken);
}
