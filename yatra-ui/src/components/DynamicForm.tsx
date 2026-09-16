import type { ReactNode } from 'react';
import { cloneElement, useId, useMemo, useState, useRef } from 'react';
import type { ReactElement } from 'react';
import { Ban, Check } from 'lucide-react';
import { definitionError, initialValues, submittedValues, validateForm, type FormErrors, type FormValues } from '../formValidation';
import { SubmissionError } from '../api';
import { readSaved, saveState } from '../persistence';
import { useEffect } from 'react';
import type {
  FormFieldDefinition,
  FormValue,
  FormOutcome,
  FormRequestPayload,
  FormResponsePayload,
  FormSubmissionReceipt,
  YatraMessage,
} from '../types';

interface DynamicFormProps {
  message: YatraMessage<FormRequestPayload>;
  canSubmit: boolean;
  outcome?: FormOutcome;
  onSubmit: (
    message: YatraMessage<FormRequestPayload>,
    response: FormResponsePayload,
  ) => Promise<FormSubmissionReceipt>;
}

export function DynamicForm({ message, canSubmit, outcome, onSubmit }: DynamicFormProps) {
  const form = message.payload;
  const storageKey = `form:${message.conversationId}:${message.messageId}`;
  const saved = useMemo(() => readSaved<{ values?: FormValues; errors?: FormErrors; state?: string; pendingResponseId?: string | null }>(storageKey, {}), [storageKey]);
  const submitting = useRef(false);
  const latestOutcome = useRef(outcome);
  latestOutcome.current = outcome;
  const outcomeAtStart = useRef(outcome);
  const formRef = useRef<HTMLFormElement>(null);
  const [values, setValues] = useState<FormValues>(() => saved?.values ?? initialValues(form));
  const [errors, setErrors] = useState<FormErrors>(saved?.errors ?? {});
  const [state, setState] = useState<'open' | 'submitting' | 'submitted' | 'completed' | 'cancelled' | 'failed-terminal'>(() =>
    saved?.state === 'submitted' || saved?.state === 'completed' || saved?.state === 'cancelled' || saved?.state === 'failed-terminal' ? saved.state : 'open');
  const [pendingResponseId, setPendingResponseId] = useState<string | null>(saved?.pendingResponseId ?? null);
  const disabled = !canSubmit || state !== 'open';
  const invalidDefinition = useMemo(() => definitionError(form), [form]);

  useEffect(() => {
    if (!saveState(storageKey, { values, errors, state, pendingResponseId }) && errors.$form !== 'Draft storage is unavailable. Keep this page open until submission succeeds.') {
      setErrors(current => ({ ...current, $form: 'Draft storage is unavailable. Keep this page open until submission succeeds.' }));
    }
  }, [storageKey, values, errors, state, pendingResponseId]);

  useEffect(() => {
    if (invalidDefinition) console.warn('Yatra form unavailable', { messageId: message.messageId, formVersion: form?.formVersion, fieldTypes: form?.fields?.map?.(field => field?.type) });
  }, [invalidDefinition, message.messageId, form]);

  useEffect(() => {
    if (!outcome || submitting.current && outcome === outcomeAtStart.current) {
      return;
    }

    if (outcome.responseMessageId && pendingResponseId && outcome.responseMessageId !== pendingResponseId) {
      return;
    }

    applyOutcome(outcome);
  }, [outcome, pendingResponseId]);

  function applyOutcome(result: FormOutcome) {
    if (result.status === 'submitted' || result.status === 'completed' || result.status === 'cancelled') {
      setErrors({});
      setState(result.status);
      return;
    }

    if (result.status === 'failed-retryable') {
      setErrors({ $form: result.message ?? 'Response was not accepted.' });
      setState('open');
      return;
    }

    setErrors({ $form: result.message ?? 'The response could not be processed.' });
    setState('failed-terminal');
  }

  if (invalidDefinition) {
    return <div className="form-card form-card-error" role="alert">{invalidDefinition}</div>;
  }

  async function send(action: 'submit' | 'cancel') {
    if (!canSubmit || state !== 'open' || submitting.current) {
      return;
    }

    const nextErrors = action === 'submit' ? validateForm(form, values) : {};
    setErrors(nextErrors);
    if (Object.keys(nextErrors).length > 0) {
      const first = formRef.current?.querySelector<HTMLElement>(`[data-field="${CSS.escape(Object.keys(nextErrors)[0])}"] input, [data-field="${CSS.escape(Object.keys(nextErrors)[0])}"] textarea, [data-field="${CSS.escape(Object.keys(nextErrors)[0])}"] select`);
      first?.focus();
      return;
    }

    submitting.current = true;
    outcomeAtStart.current = latestOutcome.current;
    setPendingResponseId(null);
    setState('submitting');

    try {
      const receipt = await onSubmit(message, {
        formId: form.formId,
        formVersion: form.formVersion,
        action,
        values: action === 'cancel' ? {} : submittedValues(form, values),
      });
      setPendingResponseId(receipt.responseMessageId);
      const result = latestOutcome.current;
      if (result && result !== outcomeAtStart.current && (!result.responseMessageId || result.responseMessageId === receipt.responseMessageId)) {
        applyOutcome(result);
      } else {
        setState('submitted');
      }
    } catch (error) {
      const result = latestOutcome.current;
      if (result && result !== outcomeAtStart.current) {
        applyOutcome(result);
      } else {
        setErrors({ ...(error instanceof SubmissionError ? error.fieldErrors : {}), $form: error instanceof SubmissionError ? error.message : 'Response was not accepted.' });
        setState(error instanceof SubmissionError && !error.retryable ? 'failed-terminal' : 'open');
      }
    } finally {
      submitting.current = false;
    }
  }

  return (
    <form ref={formRef} className="form-card" noValidate onSubmit={event => { event.preventDefault(); void send('submit'); }}>
      <header>
        <h2>{form.title}</h2>
        {form.description && <p>{form.description}</p>}
      </header>

      <div className="form-fields">
        {form.fields.map((field) => (
          <Field
            key={field.id}
            field={field}
            value={values[field.id]}
            error={errors[field.id]}
            disabled={disabled}
            onChange={(value) => setValues((current) => ({ ...current, [field.id]: value }))}
          />
        ))}
      </div>

      {errors.$form && <div className="field-error" role="alert">{errors.$form}</div>}
      {state !== 'open' && <div className="form-state">{formatState(state)}</div>}

      <footer>
        {form.cancelLabel && (
          <button type="button" className="secondary-button" disabled={disabled} onClick={() => void send('cancel')}>
            <Ban size={16} />
            <span>{form.cancelLabel}</span>
          </button>
        )}
        <button type="submit" className="primary-button" disabled={disabled}>
          <Check size={16} />
          <span>{form.submitLabel}</span>
        </button>
      </footer>
    </form>
  );
}

function formatState(state: string): string {
  return state.replace('-', ' ');
}

function Field({
  field,
  value,
  error,
  disabled,
  onChange,
}: {
  field: FormFieldDefinition;
  value: FormValue;
  error?: string;
  disabled: boolean;
  onChange: (value: FormValue) => void;
}) {
  const renderer = fieldRenderers[field.type];
  const inputId = useId();
  if (field.type === 'hidden') return null;
  if (field.type === 'display') return <p>{String(value || field.label)}</p>;

  return (
    <label data-field={field.id} className={`field field-${field.type}`}>
      <span>{field.label}</span>
      {cloneElement(renderer(field, value, disabled || !!field.disabled || !!field.readOnly, onChange) as ReactElement<Record<string, unknown>>, {
        'aria-label': field.label, 'aria-required': field.required, 'aria-invalid': !!error,
        'aria-describedby': [field.helpText && `${inputId}-help`, error && `${inputId}-error`].filter(Boolean).join(' ') || undefined,
      })}
      {field.helpText && <small id={`${inputId}-help`}>{field.helpText}</small>}
      {error && <em id={`${inputId}-error`} role="alert">{error}</em>}
    </label>
  );
}

const fieldRenderers: Record<string, (
  field: FormFieldDefinition,
  value: FormValue,
  disabled: boolean,
  onChange: (value: FormValue) => void,
) => ReactNode> = {
  datetime: (field, value, disabled, onChange) => (
    <input type="datetime-local" step="any" value={String(value ?? '')} min={field.minDate ?? undefined} max={field.maxDate ?? undefined} disabled={disabled} onChange={event => onChange(event.target.value)} />
  ),
  multiselect: (field, value, disabled, onChange) => (
    <select multiple value={Array.isArray(value) ? value : []} disabled={disabled} onChange={event => onChange(Array.from(event.target.selectedOptions, option => option.value))}>
      {field.options?.map(option => <option key={option.value} value={option.value}>{option.label}</option>)}
    </select>
  ),
  text: (field, value, disabled, onChange) => (
    <input
      value={String(value ?? '')}
      disabled={disabled}
      placeholder={field.placeholder ?? undefined}
      maxLength={field.maxLength ?? undefined}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
  textarea: (field, value, disabled, onChange) => (
    <textarea
      value={String(value ?? '')}
      disabled={disabled}
      placeholder={field.placeholder ?? undefined}
      maxLength={field.maxLength ?? undefined}
      rows={4}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
  number: (field, value, disabled, onChange) => (
    <input
      type="number"
      value={typeof value === 'number' ? value : ''}
      disabled={disabled}
      min={field.min ?? undefined}
      max={field.max ?? undefined}
      onChange={(event) => onChange(event.target.value === '' ? '' : Number(event.target.value))}
    />
  ),
  date: (_field, value, disabled, onChange) => (
    <input
      type="date"
      value={String(value ?? '')}
      disabled={disabled}
      onChange={(event) => onChange(event.target.value)}
    />
  ),
  select: (field, value, disabled, onChange) => (
    <select value={String(value ?? '')} disabled={disabled} onChange={(event) => onChange(event.target.value)}>
      <option value=""></option>
      {(field.options ?? []).map((option) => (
        <option key={option.value} value={option.value}>{option.label}</option>
      ))}
    </select>
  ),
  checkbox: (field, value, disabled, onChange) => (
    <input
      type="checkbox"
      aria-label={field.label}
      checked={value === true}
      disabled={disabled}
      onChange={(event) => onChange(event.target.checked)}
    />
  ),
};
