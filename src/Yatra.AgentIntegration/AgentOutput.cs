using Yatra.Contracts.Messages;
using Yatra.Contracts.Routing;

namespace Yatra.AgentIntegration;

public sealed record AgentOutput(
    YatraMessage Message,
    ReturnPath? ReturnPath = null);
