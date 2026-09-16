import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { MessageList } from './MessageList';
import type { FormRequestPayload, YatraMessage } from '../types';

describe('MessageList', () => {
  it('renders supported text, status, error, and form messages', () => {
    render(
      <MessageList
        messages={[
          message('01K00000000000000000000001', 'user.text', { text: 'hello' }),
          message('01K00000000000000000000002', 'agent.text', { text: 'answer' }),
          message('01K00000000000000000000003', 'status', { text: 'working' }),
          message('01K00000000000000000000004', 'error', {
            code: 'invalid_form_response',
            message: 'Nope',
            retryable: false,
          }),
          message('01K00000000000000000000005', 'form.request', formPayload()),
        ]}
        renderForm={(formMessage) => <div>{formMessage.payload.title}</div>}
      />,
    );

    expect(screen.getByText('hello')).toBeInTheDocument();
    expect(screen.getByText('answer')).toBeInTheDocument();
    expect(screen.getByText('working')).toBeInTheDocument();
    expect(screen.getByText('invalid_form_response')).toBeInTheDocument();
    expect(screen.getByText('Contact')).toBeInTheDocument();
  });

  it('renders unsupported messages safely', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);

    render(
      <MessageList
        messages={[message('01K00000000000000000000006', 'unsafe.html', { html: '<b>no</b>' })]}
        renderForm={() => null}
      />,
    );

    expect(screen.getByText('Unsupported message')).toBeInTheDocument();
    expect(screen.queryByText('<b>no</b>')).not.toBeInTheDocument();
    warn.mockRestore();
  });
});

function message(messageId: string, type: string, payload: unknown): YatraMessage {
  return {
    messageId,
    conversationId: '01K00000000000000000000000',
    type,
    source: 'orchestrator:sample',
    destination: 'ui:current-conversation',
    createdAtUtc: '2026-09-10T00:00:00Z',
    expectsReply: false,
    payload,
  };
}

function formPayload(): FormRequestPayload {
  return {
    formId: 'contact',
    formVersion: '1.0',
    title: 'Contact',
    submitLabel: 'Send',
    cancelLabel: 'Cancel',
    fields: [],
  };
}
