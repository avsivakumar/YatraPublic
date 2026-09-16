import type { BrowserMessageSubmission, YatraMessage } from './types';

const configuredBaseUrl = import.meta.env.VITE_YATRA_API_BASE_URL as string | undefined;
const apiBaseUrl = configuredBaseUrl?.replace(/\/$/, '') ?? '';

export class SubmissionError extends Error {
  constructor(message: string, public retryable: boolean, public fieldErrors: Record<string, string> = {}) { super(message); }
}

export function conversationMessagesUrl(conversationId: string): string {
  return `${apiBaseUrl}/api/conversations/${conversationId}/messages`;
}

export function conversationEventsUrl(conversationId: string): string {
  return `${apiBaseUrl}/api/conversations/${conversationId}/events`;
}

export async function submitMessage(
  conversationId: string,
  submission: BrowserMessageSubmission,
): Promise<void> {
  const response = await fetch(conversationMessagesUrl(conversationId), {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(submission),
    signal: AbortSignal.timeout(30000),
  });

  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    const fieldErrors = body?.fieldErrors && typeof body.fieldErrors === 'object'
      ? Object.fromEntries(Object.entries(body.fieldErrors).filter((entry): entry is [string, string] => typeof entry[1] === 'string'))
      : {};
    throw new SubmissionError(
      typeof body?.message === 'string' ? body.message : 'Response was not accepted.',
      ![401, 403, 409, 410].includes(response.status) && body?.retryable !== false,
      fieldErrors,
    );
  }
  const acknowledgement = await response.json();
  if (acknowledgement?.messageId !== submission.messageId || acknowledgement?.status !== 'accepted' || typeof acknowledgement?.replayed !== 'boolean') {
    throw new SubmissionError('The server acknowledgement was invalid. Please retry.', true);
  }
}

export function openMessageStream(
  conversationId: string,
  onMessage: (message: YatraMessage) => void,
  onStateChange: (state: 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'error') => void,
): { close: () => void } {
  onStateChange('connecting');
  let source: EventSource;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let stableTimer: ReturnType<typeof setTimeout> | undefined;
  let stopped = false;
  let attempts = 0;
  let cursor = '';
  const seen = new Set<string>();
  const connect = () => {
    if (stopped) return;
    source = new EventSource(conversationEventsUrl(conversationId) + (cursor ? `?lastEventId=${encodeURIComponent(cursor)}` : ''));
    const active = source;
    let failed = false;

    source.onopen = () => {
      if (stopped || failed || active !== source) return;
      stableTimer = setTimeout(() => { attempts = 0; }, 30000);
      onStateChange('connected');
    };
    source.onerror = (event) => {
      if (isMessageEvent(event) || stopped || failed || active !== source) return;
      failed = true;
      clearTimeout(stableTimer);
      source.close();
      if (++attempts > 6) {
        onStateChange('disconnected');
        return;
      }
      onStateChange('reconnecting');
      timer = setTimeout(connect, Math.min(1000 * 2 ** (attempts - 1), 30000));
    };

    for (const type of ['agent.text', 'user.text', 'form.request', 'form.response', 'status', 'error']) {
      source.addEventListener(type, (event) => {
        if (!isMessageEvent(event) || stopped || failed || active !== source) return;
        handleApplicationEvent(event, message => {
          if (seen.has(message.messageId)) return;
          seen.add(message.messageId);
          cursor = event.lastEventId || message.messageId;
          onMessage(message);
        });
      });
    }
  };
  connect();
  return { close: () => { stopped = true; clearTimeout(timer); clearTimeout(stableTimer); source.close(); } };
}

function handleApplicationEvent(
  event: MessageEvent,
  onMessage: (message: YatraMessage) => void,
): void {
  try {
    const message = JSON.parse(event.data) as YatraMessage;
    if (!message || typeof message.messageId !== 'string' || typeof message.type !== 'string') throw new Error('Invalid event');
    onMessage(message);
  } catch {
    console.warn('Ignoring malformed Yatra SSE event.');
  }
}

function isMessageEvent(event: Event): event is MessageEvent {
  return 'data' in event && typeof (event as MessageEvent).data === 'string';
}
