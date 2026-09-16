import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { openMessageStream, submitMessage } from './api';

class Stream extends EventTarget {
  static instances: Stream[] = [];
  onopen: (() => void) | null = null;
  onerror: ((event: Event) => void) | null = null;
  close = vi.fn();
  constructor(public url: string) { super(); Stream.instances.push(this); }
}

describe('transport reliability', () => {
  beforeEach(() => { vi.useFakeTimers(); Stream.instances = []; vi.stubGlobal('EventSource', Stream); });
  afterEach(() => { vi.useRealTimers(); vi.unstubAllGlobals(); });

  it('closes old streams, resumes by cursor, deduplicates, and stops after exhaustion', () => {
    const receive = vi.fn(); const status = vi.fn();
    const connection = openMessageStream('conversation', receive, status);
    const first = Stream.instances[0]; first.onopen?.();
    const event = () => new MessageEvent('agent.text', { lastEventId: 'event1', data: JSON.stringify({ messageId: 'event1', type: 'agent.text' }) });
    first.dispatchEvent(event());
    first.onerror?.(new Event('error'));
    expect(first.close).toHaveBeenCalledTimes(1);
    expect(status).toHaveBeenLastCalledWith('reconnecting');
    vi.advanceTimersByTime(1000);
    expect(Stream.instances[1].url).toContain('lastEventId=event1');
    Stream.instances[1].dispatchEvent(event());
    expect(receive).toHaveBeenCalledTimes(1);
    for (let attempt = 0; attempt < 6; attempt++) {
      Stream.instances.at(-1)!.onerror?.(new Event('error'));
      vi.advanceTimersByTime(30000);
    }
    expect(status).toHaveBeenLastCalledWith('disconnected');
    expect(Stream.instances).toHaveLength(7);
    connection.close();
    vi.runAllTimers();
    expect(Stream.instances).toHaveLength(7);
  });

  it('does not interpret application error events as disconnects', () => {
    const status = vi.fn(); const receive = vi.fn();
    const connection = openMessageStream('conversation', receive, status);
    const event = new MessageEvent('error', { data: JSON.stringify({ messageId: 'error1', type: 'error' }) });
    Stream.instances[0].dispatchEvent(event);
    Stream.instances[0].onerror?.(event);
    expect(receive).toHaveBeenCalledTimes(1);
    expect(status).toHaveBeenCalledTimes(1);
    connection.close();
  });

  it('validates acknowledgement identity and classifies conflicts', async () => {
    const submission = { messageId: 'response1', type: 'form.response' as const, payload: {} };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValueOnce(new Response(JSON.stringify({ messageId: 'wrong', status: 'accepted', replayed: false })))
      .mockResolvedValueOnce(new Response(JSON.stringify({ code: 'response_conflict', message: 'Conflict', retryable: false }), { status: 409 })));
    await expect(submitMessage('conversation', submission)).rejects.toThrow('acknowledgement');
    await expect(submitMessage('conversation', submission)).rejects.toMatchObject({ retryable: false });
  });
});
