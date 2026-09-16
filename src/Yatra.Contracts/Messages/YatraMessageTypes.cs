namespace Yatra.Contracts.Messages;

public static class YatraMessageTypes
{
    public const string UserText = "user.text";
    public const string AgentText = "agent.text";
    public const string FormRequest = "form.request";
    public const string FormResponse = "form.response";
    public const string Status = "status";
    public const string Error = "error";

    public static bool IsKnown(string messageType) =>
        messageType is UserText
            or AgentText
            or FormRequest
            or FormResponse
            or Status
            or Error;
}
