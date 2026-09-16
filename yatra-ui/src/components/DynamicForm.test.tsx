import { act, fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { DynamicForm } from './DynamicForm';
import type { FormOutcome, FormRequestPayload, FormResponsePayload, YatraMessage } from '../types';

describe('DynamicForm', () => {
  beforeEach(() => localStorage.clear());

  it('restores a draft and acknowledged state after unmounting', async () => {
    const user = userEvent.setup();
    const message = formMessage(contactForm());
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: 'persisted-response' });
    const first = render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);
    await fillContactForm(user);
    first.unmount();
    const second = render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);
    expect(screen.getByLabelText('Name')).toHaveValue('Siva');
    await user.click(screen.getByRole('button', { name: /send/i }));
    second.unmount();
    render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);
    expect(screen.getByLabelText('Name')).toHaveValue('Siva');
    expect(screen.getByText('submitted')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send/i })).toBeDisabled();
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });
  it('guards simultaneous submit events and focuses the first invalid field', async () => {
    const onSubmit = vi.fn().mockReturnValue(new Promise(() => {}));
    const { container } = render(<DynamicForm message={formMessage(contactForm())} canSubmit={true} onSubmit={onSubmit} />);
    fireEvent.submit(container.querySelector('form')!);
    expect(screen.getByLabelText('Name')).toHaveFocus();
    expect(screen.getByLabelText('Name')).toHaveAttribute('aria-invalid', 'true');
    await fillContactForm(userEvent.setup());
    fireEvent.submit(container.querySelector('form')!);
    fireEvent.submit(container.querySelector('form')!);
    fireEvent.click(screen.getByRole('button', { name: /send/i }));
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });

  it('renders added field types and preserves disabled values in submission', async () => {
    const request: FormRequestPayload = { ...contactForm(), fields: [
      { id: 'when', type: 'datetime', label: 'When', required: true, initialValue: '2026-09-16T10:30' },
      { id: 'choices', type: 'multiselect', label: 'Choices', required: true, initialValue: ['a'], options: [{ value: 'a', label: 'Alpha' }] },
      { id: 'token', type: 'hidden', label: 'Token', required: false, initialValue: 'correlation' },
      { id: 'note', type: 'display', label: 'Note', required: false, initialValue: 'Information' },
      { id: 'locked', type: 'text', label: 'Locked', required: false, initialValue: 'preserved', disabled: true },
    ] };
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: 'response' });
    render(<DynamicForm message={formMessage(request)} canSubmit={true} onSubmit={onSubmit} />);
    expect(screen.getByLabelText('When')).toHaveValue('2026-09-16T10:30');
    expect(screen.getByLabelText('Choices')).toHaveValue(['a']);
    expect(screen.getByLabelText('Locked')).toBeDisabled();
    expect(screen.queryByLabelText('Token')).not.toBeInTheDocument();
    expect(screen.getByText('Information')).toBeInTheDocument();
    await userEvent.setup().click(screen.getByRole('button', { name: /send/i }));
    expect(onSubmit.mock.calls[0][1].values).toEqual({ when: '2026-09-16T10:30', choices: ['a'], token: 'correlation', locked: 'preserved' });
  });
  it('renders every supported initial field type', () => {
    render(<DynamicForm message={formMessage(allFieldsForm())} canSubmit={true} onSubmit={vi.fn()} />);

    expect(screen.getByLabelText('Name')).toBeInTheDocument();
    expect(screen.getByLabelText('Description')).toBeInTheDocument();
    expect(screen.getByLabelText('Amount')).toBeInTheDocument();
    expect(screen.getByLabelText('Access end date')).toBeInTheDocument();
    expect(screen.getByLabelText('Application')).toBeInTheDocument();
    expect(screen.getByLabelText('Confirmed')).toBeInTheDocument();
  });

  it('enforces required-field validation', async () => {
    const user = userEvent.setup();
    render(<DynamicForm message={formMessage(contactForm())} canSubmit={true} onSubmit={vi.fn()} />);

    await user.click(screen.getByRole('button', { name: /send/i }));

    expect(await screen.findAllByText('Required')).toHaveLength(4);
  });

  it('submits a form response with the original message as inReplyTo', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: '01K00000000000000000000020' });
    const message = formMessage(contactForm());

    render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText('Name'), 'Siva');
    await user.type(screen.getByLabelText('Email'), 'siva@example.com');
    await user.type(screen.getByLabelText('Subject'), 'Hello');
    await user.type(screen.getByLabelText('Message'), 'Need help');
    await user.click(screen.getByRole('button', { name: /send/i }));

    expect(onSubmit).toHaveBeenCalledWith(message, {
      formId: 'contact',
      formVersion: '1.0',
      action: 'submit',
      values: {
        name: 'Siva',
        email: 'siva@example.com',
        subject: 'Hello',
        message: 'Need help',
      },
    } satisfies FormResponsePayload);
  });

  it('locks the form after HTTP acceptance', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: '01K00000000000000000000020' });

    render(<DynamicForm message={formMessage(contactForm())} canSubmit={true} onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText('Name'), 'Siva');
    await user.type(screen.getByLabelText('Email'), 'siva@example.com');
    await user.type(screen.getByLabelText('Subject'), 'Hello');
    await user.type(screen.getByLabelText('Message'), 'Need help');
    await user.click(screen.getByRole('button', { name: /send/i }));
    await user.click(screen.getByRole('button', { name: /send/i }));

    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(screen.getByText('submitted')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send/i })).toBeDisabled();
  });

  it('leaves the form editable after a rejected response', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockRejectedValue(new Error('bad'));

    render(<DynamicForm message={formMessage(contactForm())} canSubmit={true} onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText('Name'), 'Siva');
    await user.type(screen.getByLabelText('Email'), 'siva@example.com');
    await user.type(screen.getByLabelText('Subject'), 'Hello');
    await user.type(screen.getByLabelText('Message'), 'Need help');
    await user.click(screen.getByRole('button', { name: /send/i }));

    expect(await screen.findByText('Response was not accepted.')).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toBeEnabled();
  });

  it('cancels with an empty value set', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: '01K00000000000000000000021' });
    const message = formMessage(contactForm());

    render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);

    await user.click(screen.getByRole('button', { name: /cancel/i }));

    expect(onSubmit).toHaveBeenCalledWith(message, {
      formId: 'contact',
      formVersion: '1.0',
      action: 'cancel',
      values: {},
    });
    expect(screen.getByText('submitted')).toBeInTheDocument();
  });

  it.each(['failed-retryable', 'failed-terminal', 'completed', 'cancelled'] as const)(
    'preserves early SSE %s across a late HTTP acknowledgement', async status => {
      let acknowledge!: (value: { responseMessageId: string }) => void;
      const onSubmit = vi.fn(() => new Promise<{ responseMessageId: string }>(resolve => { acknowledge = resolve; }));
      const message = formMessage(contactForm());
      const { rerender } = render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);
      await fillContactForm(userEvent.setup());
      await userEvent.setup().click(screen.getByRole('button', { name: /send/i }));
      const outcome: FormOutcome = { status, responseMessageId: 'response', message: 'Agent result' };
      rerender(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} outcome={outcome} />);
      await act(async () => { acknowledge({ responseMessageId: 'response' }); });
      expect(screen.queryByText('submitted')).not.toBeInTheDocument();
      if (status === 'failed-retryable') {
        expect(screen.getByRole('button', { name: /send/i })).toBeEnabled();
        expect(screen.getByText('Agent result')).toBeInTheDocument();
      } else {
        expect(screen.getByText(status.replace('-', ' '))).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /send/i })).toBeDisabled();
      }
    });

  it('completes after an agent confirmation references the response message', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: '01K00000000000000000000022' });
    const message = formMessage(contactForm());
    const { rerender } = render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);

    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /send/i }));

    rerender(
      <DynamicForm
        message={message}
        canSubmit={true}
        onSubmit={onSubmit}
        outcome={{ status: 'completed', responseMessageId: '01K00000000000000000000022' }}
      />,
    );

    expect(screen.getByText('completed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send/i })).toBeDisabled();
  });

  it('reopens with values preserved after a retryable SSE error', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: '01K00000000000000000000023' });
    const message = formMessage(contactForm());
    const { rerender } = render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);

    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /send/i }));

    rerender(
      <DynamicForm
        message={message}
        canSubmit={true}
        onSubmit={onSubmit}
        outcome={{
          status: 'failed-retryable',
          message: 'Please try again.',
          responseMessageId: '01K00000000000000000000023',
        }}
      />,
    );

    expect(screen.getByText('Please try again.')).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('Siva');
    expect(screen.getByRole('button', { name: /send/i })).toBeEnabled();
  });

  it('closes after a terminal SSE error', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn().mockResolvedValue({ responseMessageId: '01K00000000000000000000024' });
    const message = formMessage(contactForm());
    const { rerender } = render(<DynamicForm message={message} canSubmit={true} onSubmit={onSubmit} />);

    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /send/i }));

    rerender(
      <DynamicForm
        message={message}
        canSubmit={true}
        onSubmit={onSubmit}
        outcome={{
          status: 'failed-terminal',
          message: 'This interaction is no longer available.',
          responseMessageId: '01K00000000000000000000024',
        }}
      />,
    );

    expect(screen.getByText('This interaction is no longer available.')).toBeInTheDocument();
    expect(screen.getByText('failed terminal')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /send/i })).toBeDisabled();
  });

  it('disables form actions when the stream is not connected', () => {
    render(<DynamicForm message={formMessage(contactForm())} canSubmit={false} onSubmit={vi.fn()} />);

    expect(screen.getByRole('button', { name: /send/i })).toBeDisabled();
    expect(screen.getByRole('button', { name: /cancel/i })).toBeDisabled();
  });

  it('rejects malformed and inherited field definitions safely', () => {
    const malformed = {
      ...contactForm(),
      fields: [{ id: 'bad', type: 'toString', label: 'Bad', required: false }],
    } as unknown as FormRequestPayload;

    render(<DynamicForm message={formMessage(malformed)} canSubmit={true} onSubmit={vi.fn()} />);

    expect(screen.getByText('Unsupported form field type: toString')).toBeInTheDocument();
  });
});

function formMessage(payload: FormRequestPayload): YatraMessage<FormRequestPayload> {
  return {
    messageId: '01K00000000000000000000010',
    conversationId: '01K00000000000000000000000',
    type: 'form.request',
    source: 'orchestrator:sample',
    destination: 'ui:current-conversation',
    createdAtUtc: '2026-09-10T00:00:00Z',
    expectsReply: true,
    payload,
  };
}

function contactForm(): FormRequestPayload {
  return {
    formId: 'contact',
    formVersion: '1.0',
    title: 'Contact',
    submitLabel: 'Send',
    cancelLabel: 'Cancel',
    fields: [
      { id: 'name', type: 'text', label: 'Name', required: true, maxLength: 120 },
      { id: 'email', type: 'text', label: 'Email', required: true, maxLength: 254, pattern: '^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$' },
      { id: 'subject', type: 'text', label: 'Subject', required: true, maxLength: 160 },
      { id: 'message', type: 'textarea', label: 'Message', required: true, maxLength: 1000 },
    ],
  };
}

async function fillContactForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Name'), 'Siva');
  await user.type(screen.getByLabelText('Email'), 'siva@example.com');
  await user.type(screen.getByLabelText('Subject'), 'Hello');
  await user.type(screen.getByLabelText('Message'), 'Need help');
}

function allFieldsForm(): FormRequestPayload {
  return {
    formId: 'all-fields',
    formVersion: '1.0',
    title: 'All fields',
    submitLabel: 'Submit',
    cancelLabel: 'Cancel',
    fields: [
      { id: 'name', type: 'text', label: 'Name', required: false },
      { id: 'description', type: 'textarea', label: 'Description', required: false },
      { id: 'amount', type: 'number', label: 'Amount', required: false, min: 1, max: 10 },
      { id: 'accessEndDate', type: 'date', label: 'Access end date', required: false },
      {
        id: 'application',
        type: 'select',
        label: 'Application',
        required: false,
        options: [{ value: 'finance', label: 'Finance' }],
      },
      { id: 'confirmed', type: 'checkbox', label: 'Confirmed', required: false },
    ],
  };
}
