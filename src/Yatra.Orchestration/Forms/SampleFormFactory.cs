using Yatra.Contracts.Forms;

namespace Yatra.Orchestration.Forms;

public static class SampleFormFactory
{
    public static FormRequestPayload CreateContactForm() =>
        new()
        {
            FormId = "contact",
            FormVersion = "1.0",
            Title = "Contact",
            Description = "Send a short contact message.",
            SubmitLabel = "Send",
            CancelLabel = "Cancel",
            Fields =
            [
                Text("name", "Name", required: true, maxLength: 120),
                Text("email", "Email", required: true, maxLength: 254, pattern: @"^[^@\s]+@[^@\s]+\.[^@\s]+$"),
                Text("subject", "Subject", required: true, maxLength: 160),
                Textarea("message", "Message", required: true, maxLength: 1000)
            ]
        };

    public static FormRequestPayload CreateAccessRequestForm() =>
        new()
        {
            FormId = "access-request",
            FormVersion = "1.0",
            Title = "Application Access Request",
            Description = "Provide the requested access details.",
            SubmitLabel = "Submit",
            CancelLabel = "Cancel",
            Fields =
            [
                new FormFieldDefinition
                {
                    Id = "application",
                    Type = FormFieldType.Select,
                    Label = "Application",
                    Required = true,
                    Options =
                    [
                        new FormOption { Value = "finance", Label = "Finance" },
                        new FormOption { Value = "operations", Label = "Operations" },
                        new FormOption { Value = "hr", Label = "HR" }
                    ]
                },
                new FormFieldDefinition
                {
                    Id = "role",
                    Type = FormFieldType.Select,
                    Label = "Role",
                    Required = true,
                    Options =
                    [
                        new FormOption { Value = "viewer", Label = "Viewer" },
                        new FormOption { Value = "editor", Label = "Editor" },
                        new FormOption { Value = "approver", Label = "Approver" }
                    ]
                },
                Textarea("justification", "Business justification", required: true, maxLength: 500),
                new FormFieldDefinition
                {
                    Id = "accessEndDate",
                    Type = FormFieldType.Date,
                    Label = "Access end date",
                    Required = true
                }
            ]
        };

    public static FormRequestPayload CreateConfirmationForm() =>
        new()
        {
            FormId = "confirmation",
            FormVersion = "1.0",
            Title = "Confirm Action",
            Description = "Confirm whether the sample action should continue.",
            SubmitLabel = "Confirm",
            CancelLabel = "Cancel",
            Fields =
            [
                new FormFieldDefinition
                {
                    Id = "confirmed",
                    Type = FormFieldType.Checkbox,
                    Label = "I confirm this sample action.",
                    Required = true
                }
            ]
        };

    public static FormRequestPayload CreateSampleSkillForm() =>
        new()
        {
            FormId = "sample-skill",
            FormVersion = "1.0",
            Title = "Sample Skill",
            Description = "Provide inputs for a sample skill execution.",
            SubmitLabel = "Run skill",
            CancelLabel = "Cancel",
            Fields =
            [
                Text("objective", "Objective", required: true, maxLength: 160),
                new FormFieldDefinition
                {
                    Id = "estimatedHours",
                    Type = FormFieldType.Number,
                    Label = "Estimated hours",
                    Required = true,
                    Min = 1,
                    Max = 40
                },
                new FormFieldDefinition
                {
                    Id = "priority",
                    Type = FormFieldType.Select,
                    Label = "Priority",
                    Required = true,
                    Options =
                    [
                        new FormOption { Value = "low", Label = "Low" },
                        new FormOption { Value = "normal", Label = "Normal" },
                        new FormOption { Value = "high", Label = "High" }
                    ]
                },
                Textarea("notes", "Notes", required: false, maxLength: 500)
            ]
        };

    public static FormRequestPayload? TryCreate(string formId) =>
        formId switch
        {
            "contact" => CreateContactForm(),
            "access-request" => CreateAccessRequestForm(),
            "confirmation" => CreateConfirmationForm(),
            "sample-skill" => CreateSampleSkillForm(),
            _ => null
        };

    private static FormFieldDefinition Text(
        string id,
        string label,
        bool required,
        int maxLength,
        string? pattern = null) =>
        new()
        {
            Id = id,
            Type = FormFieldType.Text,
            Label = label,
            Required = required,
            MaxLength = maxLength,
            Pattern = pattern
        };

    private static FormFieldDefinition Textarea(
        string id,
        string label,
        bool required,
        int maxLength) =>
        new()
        {
            Id = id,
            Type = FormFieldType.Textarea,
            Label = label,
            Required = required,
            MaxLength = maxLength
        };
}
