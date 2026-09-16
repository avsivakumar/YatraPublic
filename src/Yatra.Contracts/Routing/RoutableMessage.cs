using Yatra.Contracts.Messages;

namespace Yatra.Contracts.Routing;

public sealed record RoutableMessage(
    YatraMessage Message,
    ReturnPath? ReturnPath = null);
