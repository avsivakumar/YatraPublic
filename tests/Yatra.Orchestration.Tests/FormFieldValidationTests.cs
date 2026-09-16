using System.Text.Json;
using Yatra.Contracts.Forms;
using Yatra.Orchestration.Forms;

namespace Yatra.Orchestration.Tests;

public sealed class FormFieldValidationTests
{
    [Theory]
    [InlineData(FormFieldType.Text, "\"hello\"")]
    [InlineData(FormFieldType.Textarea, "\"hello\"")]
    [InlineData(FormFieldType.Number, "2")]
    [InlineData(FormFieldType.Date, "\"2026-09-16\"")]
    [InlineData(FormFieldType.Datetime, "\"2026-09-16T10:30\"")]
    [InlineData(FormFieldType.Select, "\"a\"")]
    [InlineData(FormFieldType.Multiselect, "[\"a\"]")]
    [InlineData(FormFieldType.Checkbox, "true")]
    [InlineData(FormFieldType.Hidden, "false")]
    public void AcceptsSupportedValues(FormFieldType type, string json) =>
        Assert.True(Validate(Field(type), json).IsValid);

    [Theory]
    [InlineData(FormFieldType.Text)]
    [InlineData(FormFieldType.Textarea)]
    [InlineData(FormFieldType.Number)]
    [InlineData(FormFieldType.Date)]
    [InlineData(FormFieldType.Datetime)]
    [InlineData(FormFieldType.Select)]
    [InlineData(FormFieldType.Multiselect)]
    [InlineData(FormFieldType.Checkbox)]
    [InlineData(FormFieldType.Hidden)]
    public void RejectsWrongTypesAndRequiredOmissions(FormFieldType type)
    {
        Assert.False(Validate(Field(type), "{}").IsValid);
        Assert.False(Validate(Field(type), "null").IsValid);
        Assert.Contains(Validate(Field(type) with { Required = true }, null).Errors, e => e.Code == "required");
    }

    [Fact]
    public void DisplayIsNeverSubmittedAndUnknownTypesFailClosed()
    {
        Assert.True(Validate(Field(FormFieldType.Display) with { Required = true }, null).IsValid);
        Assert.Contains(Validate(Field(FormFieldType.Display), "null").Errors, e => e.Code == "display_value");
        Assert.False(Validate(Field((FormFieldType)999), "1").IsValid);
        Assert.False(Validate(Field((FormFieldType)999), null).IsValid);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("3", true)]
    [InlineData("2.0", true)]
    [InlineData("1.5", false)]
    [InlineData("0", false)]
    [InlineData("4", false)]
    public void EnforcesIntegerAndNumericBounds(string json, bool valid) =>
        Assert.Equal(valid, Validate(Field(FormFieldType.Number) with { IntegerOnly = true, Min = 1, Max = 3 }, json).IsValid);

    [Theory]
    [InlineData(FormFieldType.Date, "2026-09-16", true)]
    [InlineData(FormFieldType.Date, "2026-09-18", true)]
    [InlineData(FormFieldType.Date, "2026-09-15", false)]
    [InlineData(FormFieldType.Date, "2026-09-19", false)]
    [InlineData(FormFieldType.Date, "2026-02-30", false)]
    [InlineData(FormFieldType.Datetime, "2026-09-16T10:30", true)]
    [InlineData(FormFieldType.Datetime, "2026-09-18T10:30", true)]
    [InlineData(FormFieldType.Datetime, "2026-09-16T10:29", false)]
    [InlineData(FormFieldType.Datetime, "2026-09-18T10:31", false)]
    [InlineData(FormFieldType.Datetime, "2026-09-16T25:30", false)]
    [InlineData(FormFieldType.Datetime, "2026-09-16T10:30Z", false)]
    public void EnforcesCalendarAndInclusiveBoundaries(FormFieldType type, string value, bool valid)
    {
        var suffix = type == FormFieldType.Date ? "" : "T10:30";
        Assert.Equal(valid, Validate(Field(type) with { MinDate = "2026-09-16" + suffix, MaxDate = "2026-09-18" + suffix }, JsonSerializer.Serialize(value)).IsValid);
    }

    [Theory]
    [InlineData("[]", false)]
    [InlineData("[\"a\"]", true)]
    [InlineData("[\"a\",\"b\"]", true)]
    [InlineData("[\"a\",\"b\",\"c\"]", false)]
    [InlineData("[\"a\",\"a\"]", false)]
    [InlineData("[\"unknown\"]", false)]
    [InlineData("[1]", false)]
    [InlineData("\"a\"", false)]
    public void EnforcesSelections(string json, bool valid) =>
        Assert.Equal(valid, Validate(Field(FormFieldType.Multiselect) with { MinSelections = 1, MaxSelections = 2 }, json).IsValid);

    [Theory]
    [InlineData("\"token\"", true)]
    [InlineData("42", true)]
    [InlineData("true", true)]
    [InlineData("[\"a\",\"b\"]", true)]
    [InlineData("[]", true)]
    [InlineData("null", false)]
    [InlineData("{}", false)]
    [InlineData("[1]", false)]
    [InlineData("[[\"a\"]]", false)]
    [InlineData("1e400", false)]
    public void EnforcesHiddenJsonTypes(string json, bool valid) =>
        Assert.Equal(valid, Validate(Field(FormFieldType.Hidden), json).IsValid);

    [Fact]
    public void EnforcesRequiredCheckboxSelectionsAndTextConstraints()
    {
        Assert.False(Validate(Field(FormFieldType.Checkbox) with { Required = true }, "false").IsValid);
        Assert.False(Validate(Field(FormFieldType.Multiselect) with { Required = true }, "[]").IsValid);
        foreach (var type in new[] { FormFieldType.Text, FormFieldType.Textarea })
        {
            var field = Field(type) with { MinLength = 2, MaxLength = 3, Pattern = "^a+$" };
            Assert.True(Validate(field, "\"aa\"").IsValid);
            foreach (var value in new[] { "a", "aaaa", "bb" })
                Assert.False(Validate(field, JsonSerializer.Serialize(value)).IsValid);
        }
        Assert.False(Validate(Field(FormFieldType.Select), "\"unknown\"").IsValid);
    }

    private static FormFieldDefinition Field(FormFieldType type) => new()
    {
        Id = "field", Label = "Field", Type = type,
        Options = new[] { "a", "b", "c" }.Select(value => new FormOption { Value = value, Label = value }).ToArray()
    };

    private static FormResponseValidationResult Validate(FormFieldDefinition field, string? json)
    {
        var definition = new FormRequestPayload
        {
            FormId = "test", FormVersion = "1.0", Title = "Test", SubmitLabel = "Submit", Fields = [field]
        };
        var values = new Dictionary<string, JsonElement>();
        if (json is not null) values[field.Id] = JsonSerializer.Deserialize<JsonElement>(json);
        return SampleFormResponseValidator.Validate(new FormResponsePayload
        {
            FormId = "test", FormVersion = "1.0", Action = FormResponseAction.Submit, Values = values
        }, definition);
    }
}
