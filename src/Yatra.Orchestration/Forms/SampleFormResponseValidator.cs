using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Yatra.Contracts.Forms;

namespace Yatra.Orchestration.Forms;

public static class SampleFormResponseValidator
{
    public static FormResponseValidationResult Validate(FormResponsePayload? response, FormRequestPayload? definition = null)
    {
        var errors = new List<FormResponseValidationError>();

        if (response is null)
        {
            errors.Add(new("payload", "invalid_form_response", "Form response payload is required."));
            return new FormResponseValidationResult(errors);
        }

        if (!Enum.IsDefined(response.Action))
        {
            errors.Add(new("action", "invalid_action", "Form action is not supported."));
        }

        if (response.Values is null)
        {
            errors.Add(new("values", "required", "Form values are required."));
            return new FormResponseValidationResult(errors);
        }

        var form = definition ?? SampleFormFactory.TryCreate(response.FormId);
        if (form is null)
        {
            errors.Add(new("formId", "unknown_form", "Form ID is not recognized."));
            return new FormResponseValidationResult(errors);
        }

        if (response.FormId != form.FormId)
            errors.Add(new("formId", "unknown_form", "Form ID does not match the requested form."));

        if (response.FormVersion != form.FormVersion)
        {
            errors.Add(new("formVersion", "unsupported_form_version", "Form version is not supported."));
        }

        var fieldsById = form.Fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        foreach (var value in response.Values)
        {
            if (!fieldsById.ContainsKey(value.Key))
            {
                errors.Add(new(value.Key, "unknown_field", "Field is not defined for this form."));
            }
            else if (fieldsById[value.Key].Type == FormFieldType.Display)
            {
                errors.Add(new(value.Key, "display_value", "Display-only fields must not be submitted."));
            }
        }

        foreach (var field in form.Fields.Where(field => !IsSupported(field.Type)))
            errors.Add(new(field.Id, "unsupported_field_type", "Field type is not supported."));

        if (response.Action == FormResponseAction.Cancel)
        {
            if (response.Values.Count != 0)
                errors.Add(new("values", "invalid_cancel_values", "Cancellation must have an empty value set."));
            return errors.Count == 0 ? FormResponseValidationResult.Success : new FormResponseValidationResult(errors);
        }

        foreach (var field in form.Fields)
        {
            if (field.Type == FormFieldType.Display || !IsSupported(field.Type)) continue;
            if (!response.Values.TryGetValue(field.Id, out var value))
            {
                if (field.Required)
                {
                    errors.Add(new(field.Id, "required", "Field is required."));
                }

                continue;
            }

            if (IsMissing(value) && field.Type is not (FormFieldType.Hidden or FormFieldType.Multiselect or FormFieldType.Checkbox))
            {
                if (field.Required) errors.Add(new(field.Id, "required", "Field is required."));
                continue;
            }

            ValidateValue(field, value, errors);
        }

        return errors.Count == 0 ? FormResponseValidationResult.Success : new FormResponseValidationResult(errors);
    }

    private static bool IsSupported(FormFieldType type) => type is
        FormFieldType.Text or FormFieldType.Textarea or FormFieldType.Number or
        FormFieldType.Date or FormFieldType.Datetime or FormFieldType.Select or
        FormFieldType.Multiselect or FormFieldType.Checkbox or FormFieldType.Hidden or FormFieldType.Display;

    private static bool IsMissing(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());

    private static void ValidateValue(
        FormFieldDefinition field,
        JsonElement value,
        ICollection<FormResponseValidationError> errors)
    {
        switch (field.Type)
        {
            case FormFieldType.Text:
            case FormFieldType.Textarea:
            case FormFieldType.Date:
            case FormFieldType.Datetime:
            case FormFieldType.Select:
                ValidateStringField(field, value, errors);
                break;
            case FormFieldType.Multiselect:
                ValidateSelections(field, value, errors);
                break;
            case FormFieldType.Hidden:
                if (!(value.ValueKind is JsonValueKind.String or JsonValueKind.True or JsonValueKind.False
                    || value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var hiddenNumber) && double.IsFinite(hiddenNumber)
                    || value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String)))
                    errors.Add(new(field.Id, "invalid_type", "Hidden value must be a string, finite number, boolean, or string array."));
                else if (field.Required && (IsMissing(value) || value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 0))
                    errors.Add(new(field.Id, "required", "Field is required."));
                break;
            case FormFieldType.Display:
                errors.Add(new(field.Id, "display_value", "Display-only fields must not be submitted."));
                break;
            default:
                errors.Add(new(field.Id, "unsupported_field_type", "Field type is not supported."));
                break;
            case FormFieldType.Number:
                ValidateNumberField(field, value, errors);
                break;
            case FormFieldType.Checkbox:
                if (value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                {
                    errors.Add(new(field.Id, "invalid_type", "Field must be a boolean."));
                }
                else if (field.Required && !value.GetBoolean())
                {
                    errors.Add(new(field.Id, "required", "Field must be checked."));
                }

                break;
        }
    }

    private static void ValidateStringField(
        FormFieldDefinition field,
        JsonElement value,
        ICollection<FormResponseValidationError> errors)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            errors.Add(new(field.Id, "invalid_type", "Field must be a string."));
            return;
        }

        var text = value.GetString() ?? string.Empty;
        if (field.MinLength is not null && text.Length < field.MinLength.Value)
        {
            errors.Add(new(field.Id, "min_length", "Field is shorter than the minimum length."));
        }

        if (field.MaxLength is not null && text.Length > field.MaxLength.Value)
        {
            errors.Add(new(field.Id, "max_length", "Field exceeds the maximum length."));
        }

        if (!string.IsNullOrWhiteSpace(field.Pattern))
        {
            try
            {
                if (!Regex.IsMatch(text, field.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    errors.Add(new(field.Id, "pattern", "Field does not match the required pattern."));
            }
            catch (Exception exception) when (exception is ArgumentException or RegexMatchTimeoutException)
            {
                errors.Add(new(field.Id, "pattern", "Field could not be validated against the required pattern."));
            }
        }

        if (field.Type == FormFieldType.Select
            && field.Options?.Any(option => option.Value == text) != true)
        {
            errors.Add(new(field.Id, "invalid_option", "Selected value is not allowed."));
        }

        if (field.Type is FormFieldType.Date or FormFieldType.Datetime)
        {
            if (!TryParseDate(text, field.Type, out var date))
            {
                errors.Add(new(field.Id, "invalid_date", "Field must be a valid ISO date or local date-time."));
                return;
            }
            if (field.MinDate is not null)
            {
                if (!TryParseDate(field.MinDate, field.Type, out var min)) errors.Add(new(field.Id, "invalid_boundary", "Invalid date boundary."));
                else if (date < min) errors.Add(new(field.Id, "min_date", "Field is before the earliest permitted date."));
            }
            if (field.MaxDate is not null)
            {
                if (!TryParseDate(field.MaxDate, field.Type, out var max)) errors.Add(new(field.Id, "invalid_boundary", "Invalid date boundary."));
                else if (date > max) errors.Add(new(field.Id, "max_date", "Field is after the latest permitted date."));
            }
        }
    }

    private static void ValidateNumberField(
        FormFieldDefinition field,
        JsonElement value,
        ICollection<FormResponseValidationError> errors)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var number))
        {
            errors.Add(new(field.Id, "invalid_type", "Field must be a number."));
            return;
        }

        if (field.Min is not null && number < field.Min.Value)
        {
            errors.Add(new(field.Id, "min", "Field is below the minimum value."));
        }

        if (field.IntegerOnly == true && decimal.Truncate(number) != number)
            errors.Add(new(field.Id, "integer_only", "Field must be a whole number."));

        if (field.Max is not null && number > field.Max.Value)
        {
            errors.Add(new(field.Id, "max", "Field exceeds the maximum value."));
        }
    }

    private static bool TryParseDate(string text, FormFieldType type, out DateTime value) =>
        DateTime.TryParseExact(text,
            type == FormFieldType.Date ? ["yyyy-MM-dd"] : ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
            CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

    private static void ValidateSelections(FormFieldDefinition field, JsonElement value, ICollection<FormResponseValidationError> errors)
    {
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            errors.Add(new(field.Id, "invalid_type", "Field must be an array of option values."));
            return;
        }
        var selections = value.EnumerateArray().Select(item => item.GetString()!).ToArray();
        if (selections.Distinct(StringComparer.Ordinal).Count() != selections.Length)
            errors.Add(new(field.Id, "duplicate_option", "Each option may only be selected once."));
        if (selections.Any(selected => field.Options?.Any(option => option.Value == selected) != true))
            errors.Add(new(field.Id, "invalid_option", "Selected value is not allowed."));
        if (field.Required && selections.Length == 0)
            errors.Add(new(field.Id, "required", "Field is required."));
        if (field.MinSelections is not null && selections.Length < field.MinSelections)
            errors.Add(new(field.Id, "min_selections", "Too few options selected."));
        if (field.MaxSelections is not null && selections.Length > field.MaxSelections)
            errors.Add(new(field.Id, "max_selections", "Too many options selected."));
    }
}
