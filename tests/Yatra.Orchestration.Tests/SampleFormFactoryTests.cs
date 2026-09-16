using Yatra.Contracts.Forms;
using Yatra.Orchestration.Forms;

namespace Yatra.Orchestration.Tests;

public sealed class SampleFormFactoryTests
{
    [Fact]
    public void ContactForm_ContainsExpectedFields()
    {
        var form = SampleFormFactory.CreateContactForm();

        Assert.Equal("contact", form.FormId);
        Assert.Equal(["name", "email", "subject", "message"], form.Fields.Select(field => field.Id));
    }

    [Fact]
    public void AccessRequestForm_ContainsExpectedFieldsAndSelectOptions()
    {
        var form = SampleFormFactory.CreateAccessRequestForm();

        Assert.Equal("access-request", form.FormId);
        Assert.Equal(["application", "role", "justification", "accessEndDate"], form.Fields.Select(field => field.Id));
        Assert.Equal(FormFieldType.Select, form.Fields[0].Type);
        Assert.NotEmpty(form.Fields[0].Options!);
    }

    [Fact]
    public void ConfirmationForm_UsesConfirmAndCancelActions()
    {
        var form = SampleFormFactory.CreateConfirmationForm();

        Assert.Equal("confirmation", form.FormId);
        Assert.Equal("Confirm", form.SubmitLabel);
        Assert.Equal("Cancel", form.CancelLabel);
        Assert.Equal(FormFieldType.Checkbox, form.Fields[0].Type);
    }

    [Fact]
    public void SampleSkillForm_ContainsExpectedFields()
    {
        var form = SampleFormFactory.CreateSampleSkillForm();

        Assert.Equal("sample-skill", form.FormId);
        Assert.Equal("Run skill", form.SubmitLabel);
        Assert.Equal(["objective", "estimatedHours", "priority", "notes"], form.Fields.Select(field => field.Id));
        Assert.Equal(FormFieldType.Number, form.Fields[1].Type);
        Assert.Equal(1, form.Fields[1].Min);
        Assert.Equal(40, form.Fields[1].Max);
        Assert.Equal(FormFieldType.Select, form.Fields[2].Type);
        Assert.NotEmpty(form.Fields[2].Options!);
    }
}
