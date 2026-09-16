import { describe, expect, it } from 'vitest';
import { definitionError, initialValues, submittedValues, validateForm } from './formValidation';
import type { FormFieldDefinition, FormRequestPayload } from './types';

const form = (fields: FormFieldDefinition[]): FormRequestPayload => ({ formId: 'test', formVersion: '1.0', title: 'Test', submitLabel: 'Send', fields });
const field = (type: FormFieldDefinition['type'], extra: Partial<FormFieldDefinition> = {}): FormFieldDefinition => ({ id: 'value', label: 'Value', type, required: false, ...extra });

describe('form contract validation', () => {
  it.each(['datetime', 'multiselect', 'hidden', 'display'] as const)('initializes %s and returns documented values', type => {
    const value = type === 'multiselect' ? ['a'] : type === 'datetime' ? '2026-09-16T10:30' : 'supplied';
    const request = form([field(type, { initialValue: value, ...(type === 'multiselect' ? { options: [{ value: 'a', label: 'Alpha' }] } : {}) })]);
    expect(definitionError(request)).toBeNull();
    expect(initialValues(request).value).toEqual(value);
    expect(submittedValues(request, initialValues(request))).toEqual(type === 'display' ? {} : { value });
  });

  it.each([
    field('text', { pattern: '[' }), field('number', { minLength: 1 }),
    field('select'), field('checkbox', { initialValue: 'true' }),
    field('number', { min: 5, max: 1 }), field('date', { initialValue: '2026-02-30' }),
    field('multiselect', { options: [{ value: 'a', label: 'A' }, { value: 'a', label: 'B' }] }),
  ])('rejects malformed definitions: $type', definition => expect(definitionError(form([definition]))).not.toBeNull());

  it('rejects duplicates, null fields, and unknown versions', () => {
    expect(definitionError(form([field('text'), field('number')]))).not.toBeNull();
    expect(definitionError(form([null as unknown as FormFieldDefinition]))).not.toBeNull();
    expect(definitionError({ ...form([]), formVersion: '2.0' })).not.toBeNull();
  });

  it('validates numbers, exact text, dates, and selections', () => {
    expect(validateForm(form([field('number', { integerOnly: true })]), { value: 1.5 }).value).toBeTruthy();
    expect(validateForm(form([field('number')]), { value: Infinity }).value).toBeTruthy();
    expect(validateForm(form([field('text', { pattern: '^a$', maxLength: 1 })]), { value: ' a ' }).value).toBeTruthy();
    expect(validateForm(form([field('date', { minDate: '2026-09-16' })]), { value: '2026-09-15' }).value).toBeTruthy();
    expect(validateForm(form([field('multiselect', { options: [{ value: 'a', label: 'Alpha' }], minSelections: 1 })]), { value: [] }).value).toBeTruthy();
    expect(validateForm(form([field('select', { options: [{ value: 'a', label: 'Alpha' }] })]), { value: 'Alpha' }).value).toBeTruthy();
  });
});
