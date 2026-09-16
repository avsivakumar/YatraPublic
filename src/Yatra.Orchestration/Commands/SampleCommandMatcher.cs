namespace Yatra.Orchestration.Commands;

public static class SampleCommandMatcher
{
    public static SampleCommand Match(string userText)
    {
        var normalized = userText.Trim().ToLowerInvariant();

        return normalized switch
        {
            "show contact form" => SampleCommand.ContactForm,
            "request application access" => SampleCommand.AccessRequestForm,
            "confirm an action" => SampleCommand.ConfirmationForm,
            "run sample skill" => SampleCommand.SampleSkill,
            _ => SampleCommand.AskLlm
        };
    }
}
