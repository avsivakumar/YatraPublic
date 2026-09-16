import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from './App';

class MockEventSource extends EventTarget {
  static instances: MockEventSource[] = [];
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSED = 2;

  readonly url: string;
  readyState = MockEventSource.CONNECTING;
  onopen: ((event: Event) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;

  constructor(url: string) {
    super();
    this.url = url;
    MockEventSource.instances.push(this);
  }

  open() {
    this.readyState = MockEventSource.OPEN;
    this.onopen?.(new Event('open'));
  }

  emit(type: string, data: unknown) {
    const event = new MessageEvent(type, { data: typeof data === 'string' ? data : JSON.stringify(data) });
    this.dispatchEvent(event);
    if (type === 'error') {
      this.onerror?.(event);
    }
  }

  close() {
    this.readyState = MockEventSource.CLOSED;
  }
}

describe('App', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    MockEventSource.instances = [];
    vi.stubGlobal('EventSource', MockEventSource);
    vi.stubGlobal('fetch', vi.fn().mockImplementation(async (_url, init) => new Response(JSON.stringify({ messageId: JSON.parse(init.body).messageId, status: 'accepted', replayed: false }), { status: 202 })));
    vi.stubGlobal('crypto', {
      getRandomValues: (bytes: Uint8Array) => {
        bytes.fill(1);
        return bytes;
      },
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('opens an SSE stream and renders server errors safely', async () => {
    render(<App />);
    const stream = MockEventSource.instances[0];

    stream.open();
    stream.emit('error', {
      messageId: '01K00000000000000000000020',
      conversationId: '01K00000000000000000000000',
      type: 'error',
      source: 'engine:routing',
      destination: 'ui:current-conversation',
      createdAtUtc: '2026-09-10T00:00:00Z',
      expectsReply: false,
      payload: {
        code: 'duplicate_response',
        message: '<strong>Completed</strong>',
        retryable: false,
      },
    });

    expect(await screen.findByText('connected')).toBeInTheDocument();
    expect(screen.getByText('duplicate_response')).toBeInTheDocument();
    expect(screen.getByText('<strong>Completed</strong>')).toBeInTheDocument();
    expect(screen.queryByText('error')).not.toBeInTheDocument();
    expect(screen.getAllByText('duplicate_response')).toHaveLength(1);
  });

  it('keeps send disabled until the SSE stream opens', async () => {
    const user = userEvent.setup();
    render(<App />);

    await user.type(screen.getByLabelText('Message'), 'show contact form');
    expect(screen.getByRole('button', { name: /send message/i })).toBeDisabled();

    MockEventSource.instances[0].open();

    expect(await screen.findByRole('button', { name: /send message/i })).toBeEnabled();
  });

  it('clears messages while disconnected and keeps them cleared across reload and replay', async () => {
    const user = userEvent.setup();
    const first = render(<App />);
    expect(screen.getByRole('button', { name: 'Clear all messages' })).toBeDisabled();
    const message = contactFormMessage();
    MockEventSource.instances[0].emit('form.request', message);
    await screen.findByText('Contact');
    await user.click(screen.getByRole('button', { name: 'Clear all messages' }));
    expect(screen.getByText('No messages yet')).toBeInTheDocument();
    first.unmount();
    render(<App />);
    const stream = MockEventSource.instances[1];
    stream.emit('form.request', message);
    expect(screen.queryByText('Contact')).not.toBeInTheDocument();
    stream.emit('form.request', { ...message, messageId: '01K00000000000000000000099' });
    expect(await screen.findByText('Contact')).toBeInTheDocument();
  });

  it('posts user text through the browser DTO', async () => {
    const user = userEvent.setup();
    render(<App />);

    MockEventSource.instances[0].open();
    await user.type(screen.getByLabelText('Message'), 'show contact form');
    await user.click(screen.getByRole('button', { name: /send message/i }));

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1));
    const [, init] = vi.mocked(fetch).mock.calls[0];
    const body = JSON.parse(String(init?.body));

    expect(body).toMatchObject({
      type: 'user.text',
      payload: { text: 'show contact form' },
    });
    expect(body.source).toBeUndefined();
    expect(body.destination).toBeUndefined();
    expect(body.createdAtUtc).toBeUndefined();
  });

  it('scrolls the conversation to reveal server messages', async () => {
    const scrollTo = vi.fn();
    Object.defineProperty(HTMLElement.prototype, 'scrollTo', {
      configurable: true,
      value: scrollTo,
    });
    render(<App />);

    MockEventSource.instances[0].open();
    MockEventSource.instances[0].emit('agent.text', {
      messageId: '01K00000000000000000000021',
      conversationId: '01K00000000000000000000000',
      type: 'agent.text',
      source: 'orchestrator:sample',
      destination: 'ui:current-conversation',
      createdAtUtc: '2026-09-10T00:00:00Z',
      expectsReply: false,
      payload: { text: 'server answer' },
    });

    expect(await screen.findByText('server answer')).toBeInTheDocument();
    await waitFor(() => expect(scrollTo).toHaveBeenCalledWith(expect.objectContaining({ behavior: 'smooth' })));
  });

  it('ignores malformed SSE JSON', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    render(<App />);

    MockEventSource.instances[0].open();
    MockEventSource.instances[0].emit('agent.text', '{not-json');

    expect(screen.getByText('No messages yet')).toBeInTheDocument();
    expect(warn).toHaveBeenCalled();
    warn.mockRestore();
  });

  it('locks a submitted form after a valid HTTP acknowledgement', async () => {
    const user = userEvent.setup();
    render(<App />);

    const stream = MockEventSource.instances[0];
    stream.open();
    await screen.findByText('connected');
    stream.emit('form.request', contactFormMessage());
    await screen.findByText('Contact');

    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /^send$/i }));

    expect(await screen.findByText('submitted')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^send$/i })).toBeDisabled();
  });

  it('completes a form when agent confirmation references the response message', async () => {
    const user = userEvent.setup();
    render(<App />);

    const stream = MockEventSource.instances[0];
    stream.open();
    await screen.findByText('connected');
    stream.emit('form.request', contactFormMessage());
    await screen.findByText('Contact');

    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /^send$/i }));
    const firstBody = postedBody(0);

    stream.emit('agent.text', {
      messageId: '01K00000000000000000000040',
      conversationId: '01K00000000000000000000000',
      type: 'agent.text',
      source: 'orchestrator:sample',
      destination: 'ui:current-conversation',
      createdAtUtc: '2026-09-10T00:00:00Z',
      inReplyTo: firstBody.messageId,
      expectsReply: false,
      payload: { text: 'Your contact response was received.' },
    });

    expect(await screen.findByText('completed')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^send$/i })).toBeDisabled();
    stream.emit('form.response', {
      ...contactFormMessage(), messageId: firstBody.messageId,
      type: 'form.response', inReplyTo: firstBody.inReplyTo, payload: firstBody.payload,
    });
    expect(await screen.findByText('completed')).toBeInTheDocument();
    expect(screen.queryByText('submitted')).not.toBeInTheDocument();
  });

  it('reopens after retryable SSE error and retries with a new response id', async () => {
    const user = userEvent.setup();
    render(<App />);

    const stream = MockEventSource.instances[0];
    stream.open();
    await screen.findByText('connected');
    stream.emit('form.request', contactFormMessage());
    await screen.findByText('Contact');

    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /^send$/i }));
    const firstBody = postedBody(0);

    stream.emit('error', {
      messageId: '01K00000000000000000000041',
      conversationId: '01K00000000000000000000000',
      type: 'error',
      source: 'engine:routing',
      destination: 'ui:current-conversation',
      createdAtUtc: '2026-09-10T00:00:00Z',
      inReplyTo: firstBody.messageId,
      expectsReply: false,
      payload: {
        code: 'requester_unavailable',
        message: 'The response could not be delivered. Please try again.',
        retryable: true,
        relatedMessageId: firstBody.messageId,
      },
    });

    expect(await screen.findByText('The response could not be delivered. Please try again.')).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('Siva');
    await waitFor(() => expect(screen.getByRole('button', { name: /^send$/i })).toBeEnabled());

    await user.click(screen.getByRole('button', { name: /^send$/i }));
    const secondBody = postedBody(1);

    expect(secondBody.messageId).not.toEqual(firstBody.messageId);
    expect(secondBody.inReplyTo).toEqual(firstBody.inReplyTo);
  });

  it('reuses response identity and content after a lost acknowledgement', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockRejectedValueOnce(new TypeError('network'));
    render(<App />);
    const stream = MockEventSource.instances[0];
    stream.open(); stream.emit('form.request', contactFormMessage());
    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /^send$/i }));
    expect(await screen.findByText('Response was not accepted.')).toBeInTheDocument();
    expect(screen.getByLabelText('Name')).toHaveValue('Siva');
    await user.click(screen.getByRole('button', { name: /^send$/i }));
    expect(postedBody(1)).toEqual(postedBody(0));
    expect(await screen.findByText('submitted')).toBeInTheDocument();
    stream.emit('form.request', contactFormMessage());
    expect(screen.getAllByText('Contact')).toHaveLength(1);
    expect(screen.getByRole('button', { name: /^send$/i })).toBeDisabled();
  });

  it('restores response identity across a reload after a lost acknowledgement', async () => {
    const user = userEvent.setup();
    vi.mocked(fetch).mockRejectedValueOnce(new TypeError('network'));
    const first = render(<App />);
    MockEventSource.instances[0].open();
    MockEventSource.instances[0].emit('form.request', contactFormMessage());
    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /^send$/i }));
    expect(await screen.findByText('Response was not accepted.')).toBeInTheDocument();
    const original = postedBody(0);
    first.unmount();
    render(<App />);
    MockEventSource.instances[1].open();
    expect(screen.getByLabelText('Name')).toHaveValue('Siva');
    await user.click(screen.getByRole('button', { name: /^send$/i }));
    expect(postedBody(1)).toEqual(original);
    expect(await screen.findByText('submitted')).toBeInTheDocument();
  });

  it('keeps a form closed after terminal SSE error', async () => {
    const user = userEvent.setup();
    render(<App />);

    const stream = MockEventSource.instances[0];
    stream.open();
    await screen.findByText('connected');
    stream.emit('form.request', contactFormMessage());
    await screen.findByText('Contact');

    await fillContactForm(user);
    await user.click(screen.getByRole('button', { name: /^send$/i }));
    const firstBody = postedBody(0);

    stream.emit('error', {
      messageId: '01K00000000000000000000042',
      conversationId: '01K00000000000000000000000',
      type: 'error',
      source: 'engine:routing',
      destination: 'ui:current-conversation',
      createdAtUtc: '2026-09-10T00:00:00Z',
      inReplyTo: firstBody.inReplyTo,
      expectsReply: false,
      payload: {
        code: 'duplicate_response',
        message: 'This interaction has already been completed.',
        retryable: false,
        relatedMessageId: firstBody.inReplyTo,
      },
    });

    expect(await screen.findByText('This interaction has already been completed.')).toBeInTheDocument();
    expect(await screen.findByText('failed terminal')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^send$/i })).toBeDisabled();
  });
});

async function fillContactForm(user: ReturnType<typeof userEvent.setup>) {
  await user.type(await screen.findByLabelText('Name'), 'Siva');
  await user.type(screen.getByLabelText('Email'), 'siva@example.com');
  await user.type(screen.getByLabelText('Subject'), 'Hello');
  await user.type(screen.getAllByLabelText('Message')[0], 'Need help');
}

function postedBody(index: number) {
  const [, init] = vi.mocked(fetch).mock.calls[index];
  return JSON.parse(String(init?.body));
}

function contactFormMessage() {
  return {
    messageId: '01K00000000000000000000030',
    conversationId: '01K00000000000000000000000',
    type: 'form.request',
    source: 'orchestrator:sample',
    destination: 'ui:current-conversation',
    createdAtUtc: '2026-09-10T00:00:00Z',
    inReplyTo: '01K00000000000000000000029',
    expectsReply: true,
    payload: {
      formId: 'contact',
      formVersion: '1.0',
      title: 'Contact',
      submitLabel: 'Send',
      cancelLabel: 'Cancel',
      fields: [
        { id: 'name', type: 'text', label: 'Name', required: true, maxLength: 120 },
        { id: 'email', type: 'text', label: 'Email', required: true, maxLength: 254 },
        { id: 'subject', type: 'text', label: 'Subject', required: true, maxLength: 160 },
        { id: 'message', type: 'textarea', label: 'Message', required: true, maxLength: 1000 },
      ],
    },
  };
}
