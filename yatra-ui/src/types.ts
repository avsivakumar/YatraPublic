export type YatraMessageType =
  | 'user.text'
  | 'agent.text'
  | 'form.request'
  | 'form.response'
  | 'status'
  | 'error';

export type FormFieldType = 'text' | 'textarea' | 'number' | 'date' | 'datetime' | 'select' | 'multiselect' | 'checkbox' | 'display' | 'hidden';
export type FormValue = string | number | boolean | string[];

export type FormResponseAction = 'submit' | 'cancel';

export interface YatraMessage<TPayload = unknown> {
  messageId: string;
  conversationId: string;
  type: YatraMessageType | string;
  source: string;
  destination: string;
  createdAtUtc: string;
  inReplyTo?: string | null;
  expectsReply: boolean;
  payload: TPayload;
}

export interface BrowserMessageSubmission<TPayload = unknown> {
  messageId: string;
  type: 'user.text' | 'form.response';
  inReplyTo?: string | null;
  payload: TPayload;
}

export interface TextPayload {
  text: string;
}

export interface YatraErrorPayload {
  code: string;
  message: string;
  retryable: boolean;
  relatedMessageId?: string | null;
}

export interface FormOption {
  value: string;
  label: string;
}

export interface FormFieldDefinition {
  id: string;
  type: FormFieldType;
  label: string;
  required: boolean;
  initialValue?: FormValue | null;
  readOnly?: boolean;
  disabled?: boolean;
  helpText?: string | null;
  integerOnly?: boolean;
  minSelections?: number | null;
  maxSelections?: number | null;
  minDate?: string | null;
  maxDate?: string | null;
  minLength?: number | null;
  maxLength?: number | null;
  min?: number | null;
  max?: number | null;
  pattern?: string | null;
  placeholder?: string | null;
  options?: FormOption[] | null;
}

export interface FormRequestPayload {
  formId: string;
  formVersion: string;
  title: string;
  description?: string | null;
  submitLabel: string;
  cancelLabel?: string | null;
  fields: FormFieldDefinition[];
}

export interface FormResponsePayload {
  formId: string;
  formVersion: string;
  action: FormResponseAction;
  values: Record<string, FormValue>;
  replacesResponseId?: string;
}

export interface FormSubmissionReceipt {
  responseMessageId: string;
}

export type FormOutcomeStatus = 'submitted' | 'completed' | 'cancelled' | 'failed-retryable' | 'failed-terminal';

export interface FormOutcome {
  status: FormOutcomeStatus;
  message?: string;
  responseMessageId?: string;
}
