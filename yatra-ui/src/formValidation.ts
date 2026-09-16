import type { FormFieldDefinition, FormRequestPayload, FormValue } from './types';

export type FormValues = Record<string, FormValue>;
export type FormErrors = Record<string, string>;
const types = ['text', 'textarea', 'number', 'date', 'datetime', 'select', 'multiselect', 'checkbox', 'display', 'hidden'];
const present = (value: unknown) => value !== undefined && value !== null;

export function definitionError(form: FormRequestPayload): string | null {
  if (!form || typeof form.formVersion !== 'string' || !/^1(?:\.\d+)*$/.test(form.formVersion) ||
      [form.formId, form.title, form.submitLabel].some(v => typeof v !== 'string' || !v.trim()) ||
      [form.description, form.cancelLabel].some(v => present(v) && typeof v !== 'string') || !Array.isArray(form.fields)) return 'Unsupported form';
  const ids = new Set<string>();
  for (const field of form.fields) {
    if (!field || [field.id, field.label].some(v => typeof v !== 'string' || !v.trim()) || ids.has(field.id)) return 'Invalid form definition';
    if ([field.required, field.disabled, field.readOnly, field.integerOnly].some(v => present(v) && typeof v !== 'boolean') ||
        [field.helpText, field.placeholder, field.pattern, field.minDate, field.maxDate].some(v => present(v) && typeof v !== 'string')) return 'Invalid field metadata';
    ids.add(field.id);
    if (!types.includes(field.type)) return `Unsupported form field type: ${String(field.type)}`;
    const text = ['text', 'textarea'].includes(field.type);
    const choice = ['select', 'multiselect'].includes(field.type);
    const date = ['date', 'datetime'].includes(field.type);
    for (const [keys, applicable] of [
      [['minLength', 'maxLength', 'pattern'], text],
      [['min', 'max', 'integerOnly'], field.type === 'number'],
      [['minSelections', 'maxSelections'], field.type === 'multiselect'],
      [['minDate', 'maxDate'], date], [['options'], choice],
    ] as [Array<keyof FormFieldDefinition>, boolean][]) {
      if (!applicable && keys.some(key => present(field[key]))) return 'Invalid validation metadata';
    }
    for (const key of ['minLength', 'maxLength', 'minSelections', 'maxSelections'] as const) {
      if (present(field[key]) && (!Number.isInteger(field[key]) || field[key]! < 0)) return 'Invalid validation metadata';
    }
    for (const key of ['min', 'max'] as const) if (present(field[key]) && !Number.isFinite(field[key])) return 'Invalid validation metadata';
    for (const [min, max] of [[field.minLength, field.maxLength], [field.min, field.max], [field.minSelections, field.maxSelections]]) {
      if (present(min) && present(max) && min! > max!) return 'Invalid validation metadata';
    }
    if (present(field.pattern)) { try { new RegExp(field.pattern!); } catch { return 'Invalid validation pattern'; } }
    if (choice && (!Array.isArray(field.options) || !field.options.length || field.options.some(o => !o || typeof o.value !== 'string' || !o.value || typeof o.label !== 'string') || new Set(field.options.map(o => o.value)).size !== field.options.length)) return 'Invalid choice options';
    if (date && ([field.minDate, field.maxDate].some(v => present(v) && !validDate(v!, field.type)) || (field.minDate && field.maxDate && Date.parse(field.minDate) > Date.parse(field.maxDate)))) return 'Invalid date boundaries';
    if (present(field.initialValue) && valueError({ ...field, required: false }, field.initialValue!)) return 'Invalid initial value';
  }
  return null;
}

export function initialValues(form: FormRequestPayload): FormValues {
  if (definitionError(form)) return {};
  return Object.fromEntries(form.fields.map(field => [field.id, field.initialValue ?? (field.type === 'checkbox' ? false : field.type === 'multiselect' ? [] : '')]));
}

export function submittedValues(form: FormRequestPayload, values: FormValues): FormValues {
  return Object.fromEntries(form.fields.filter(f => f.type !== 'display').map(f => [f.id, values[f.id]]));
}

export function validateForm(form: FormRequestPayload, values: FormValues): FormErrors {
  const invalid = definitionError(form);
  if (invalid) return { $form: invalid };
  return Object.fromEntries(form.fields.filter(f => f.type !== 'display').flatMap(f => {
    const error = valueError(f, values[f.id]);
    return error ? [[f.id, error]] : [];
  }));
}

function validDate(value: string, type: string): boolean {
  if (type === 'date') return /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?$/.test(value) && validDate(value.slice(0, 10), 'date') && Number.isFinite(Date.parse(value));
}

function valueError(field: FormFieldDefinition, value: FormValue | undefined): string | null {
  const empty = value === undefined || value === '' || (typeof value === 'string' && !value.trim()) || (Array.isArray(value) && !value.length);
  if (field.required && (empty || (field.type === 'checkbox' && value !== true))) return 'Required';
  if (empty && field.type !== 'multiselect') return null;
  if (field.type === 'checkbox') return typeof value === 'boolean' ? null : 'Enter a boolean value';
  if (field.type === 'number') {
    if (typeof value !== 'number' || !Number.isFinite(value)) return 'Enter a valid number';
    if (field.integerOnly && !Number.isInteger(value)) return 'Enter a whole number';
    if (present(field.min) && value < field.min!) return `Minimum ${field.min}`;
    if (present(field.max) && value > field.max!) return `Maximum ${field.max}`;
    return null;
  }
  if (field.type === 'hidden') return typeof value === 'string' || typeof value === 'boolean' || (typeof value === 'number' && Number.isFinite(value)) || (Array.isArray(value) && value.every(v => typeof v === 'string')) ? null : 'Invalid value';
  if (field.type === 'multiselect') {
    if (!Array.isArray(value) || new Set(value).size !== value.length || value.some(v => !field.options?.some(o => o.value === v))) return 'Choose listed options';
    if (present(field.minSelections) && value.length < field.minSelections!) return `Select at least ${field.minSelections}`;
    if (present(field.maxSelections) && value.length > field.maxSelections!) return `Select at most ${field.maxSelections}`;
    return null;
  }
  if (typeof value !== 'string') return 'Enter text';
  if (field.type === 'select' && !field.options?.some(o => o.value === value)) return 'Choose a listed option';
  if (['date', 'datetime'].includes(field.type)) {
    if (!validDate(value, field.type)) return 'Enter a valid date';
    if (field.minDate && Date.parse(value) < Date.parse(field.minDate)) return `On or after ${field.minDate}`;
    if (field.maxDate && Date.parse(value) > Date.parse(field.maxDate)) return `On or before ${field.maxDate}`;
  }
  if (present(field.minLength) && value.length < field.minLength!) return `Minimum ${field.minLength} characters`;
  if (present(field.maxLength) && value.length > field.maxLength!) return `Maximum ${field.maxLength} characters`;
  if (field.pattern && !new RegExp(field.pattern).test(value)) return 'Invalid format';
  return null;
}
