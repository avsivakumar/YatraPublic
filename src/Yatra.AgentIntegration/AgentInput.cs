using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;

namespace Yatra.AgentIntegration;

public sealed record AgentInput(
    YatraMessage Message,
    ReturnPath? ResolvedReturnPath = null);
