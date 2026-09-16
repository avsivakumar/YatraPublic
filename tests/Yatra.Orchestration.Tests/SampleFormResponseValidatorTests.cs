using System.Text.Json;
using Yatra.Contracts.Forms;
using Yatra.Contracts.Messages;
using Yatra.Orchestration.Forms;

namespace Yatra.Orchestration.Tests;

public sealed class SampleFormResponseValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidAccessRequestSubmission()
    {
        var result = SampleFormResponseValidator.Validate(CreateAccessRequestResponse(new Dictionary<string, object?>
        {
            ["application"] = "finance",
            ["role"] = "viewer",
            ["justification"] = "Month-end reporting",
            ["accessEndDate"] = "2026-10-01"
        }));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AcceptsValidSampleSkillSubmission()
    {
        var result = SampleFormResponseValidator.Validate(CreateResponse(
            "sample-skill",
            "1.0",
            FormResponseAction.Submit,
            new Dictionary<string, object?>
            {
                ["objective"] = "Prepare demo notes",
                ["estimatedHours"] = 4,
                ["priority"] = "high",
                ["notes"] = "Use the short path"
            }));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("unknown", "unknown_form")]
    [InlineData("access-request", "unsupported_form_version", "2.0")]
    public void Validate_RejectsUnknownFormOrVersion(
        string formId,
        string expectedCode,
        string formVersion = "1.0")
    {
        var result = SampleFormResponseValidator.Validate(CreateResponse(
            formId,
            formVersion,
            FormResponseAction.Submit,
            new Dictionary<string, object?>()));

        Assert.Contains(result.Errors, error => error.Code == expectedCode);
    }

    [Fact]
    public void Validate_RejectsUnknownField()
    {
        var result = SampleFormResponseValidator.Validate(CreateAccessRequestResponse(new Dictionary<string, object?>
        {
            ["application"] = "finance",
            ["role"] = "viewer",
            ["justification"] = "Month-end reporting",
            ["accessEndDate"] = "2026-10-01",
            ["admin"] = true
        }));

        Assert.Contains(result.Errors, error => error.Field == "admin" && error.Code == "unknown_field");
    }

    [Fact]
    public void Validate_RejectsMissingRequiredFieldsForSubmit()
    {
        var result = SampleFormResponseValidator.Validate(CreateAccessRequestResponse(new Dictionary<string, object?>()));

        Assert.Contains(result.Errors, error => error.Field == "application" && error.Code == "required");
        Assert.Contains(result.Errors, error => error.Field == "role" && error.Code == "required");
    }

    [Fact]
    public void Validate_AllowsCancelWithoutNormalFieldValues()
    {
        var result = SampleFormResponseValidator.Validate(CreateResponse(
            "access-request",
            "1.0",
            FormResponseAction.Cancel,
            new Dictionary<string, object?>()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsInvalidFieldTypesAndConstraints()
    {
        var result = SampleFormResponseValidator.Validate(CreateAccessRequestResponse(new Dictionary<string, object?>
        {
            ["application"] = "not-an-option",
            ["role"] = "viewer",
            ["justification"] = new string('x', 501),
            ["accessEndDate"] = "not-a-date"
        }));

        Assert.Contains(result.Errors, error => error.Field == "application" && error.Code == "invalid_option");
        Assert.Contains(result.Errors, error => error.Field == "justification" && error.Code == "max_length");
        Assert.Contains(result.Errors, error => error.Field == "accessEndDate" && error.Code == "invalid_date");
    }

    [Fact]
    public void Validate_RejectsContactEmailPatternMismatch()
    {
        var result = SampleFormResponseValidator.Validate(CreateResponse(
            "contact",
            "1.0",
            FormResponseAction.Submit,
            new Dictionary<string, object?>
            {
                ["name"] = "Asha",
                ["email"] = "not-email",
                ["subject"] = "Hello",
                ["message"] = "Message"
            }));

        Assert.Contains(result.Errors, error => error.Field == "email" && error.Code == "pattern");
    }

    [Fact]
    public void Validate_RejectsInvalidSampleSkillPriority()
    {
        var result = SampleFormResponseValidator.Validate(CreateResponse(
            "sample-skill",
            "1.0",
            FormResponseAction.Submit,
            new Dictionary<string, object?>
            {
                ["objective"] = "Prepare demo notes",
                ["estimatedHours"] = 4,
                ["priority"] = "urgent"
            }));

        Assert.Contains(result.Errors, error => error.Field == "priority" && error.Code == "invalid_option");
    }

    [Fact]
    public void Validate_RejectsSampleSkillEstimatedHoursOutsideRange()
    {
        var result = SampleFormResponseValidator.Validate(CreateResponse(
            "sample-skill",
            "1.0",
            FormResponseAction.Submit,
            new Dictionary<string, object?>
            {
                ["objective"] = "Prepare demo notes",
                ["estimatedHours"] = 41,
                ["priority"] = "normal"
            }));

        Assert.Contains(result.Errors, error => error.Field == "estimatedHours" && error.Code == "max");
    }

    [Fact]
    public void Validate_RejectsNullValues()
    {
        const string json = """
            {
              "formId": "contact",
              "formVersion": "1.0",
              "action": "submit",
              "values": null
            }
            """;
        var response = JsonSerializer.Deserialize<FormResponsePayload>(json, YatraJson.SerializerOptions);

        var result = SampleFormResponseValidator.Validate(response);

        Assert.Contains(result.Errors, error => error.Field == "values" && error.Code == "required");
    }

    [Fact]
    public void Validate_RejectsUndefinedNumericAction()
    {
        var response = new FormResponsePayload
        {
            FormId = "contact",
            FormVersion = "1.0",
            Action = (FormResponseAction)42,
            Values = new Dictionary<string, JsonElement>()
        };

        var result = SampleFormResponseValidator.Validate(response);

        Assert.Contains(result.Errors, error => error.Field == "action" && error.Code == "invalid_action");
    }

    private static FormResponsePayload CreateAccessRequestResponse(Dictionary<string, object?> values) =>
        CreateResponse("access-request", "1.0", FormResponseAction.Submit, values);

    private static FormResponsePayload CreateResponse(
        string formId,
        string formVersion,
        FormResponseAction action,
        Dictionary<string, object?> values) =>
        new()
        {
            FormId = formId,
            FormVersion = formVersion,
            Action = action,
            Values = values.ToDictionary(
                pair => pair.Key,
                pair => JsonSerializer.SerializeToElement(pair.Value, YatraJson.SerializerOptions))
        };
}
