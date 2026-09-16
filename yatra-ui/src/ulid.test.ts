import { beforeEach, describe, expect, it, vi } from 'vitest';
import { getConversationId } from './ulid';

describe('getConversationId', () => {
  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    vi.stubGlobal('crypto', {
      getRandomValues: (bytes: Uint8Array) => {
        bytes.fill(1);
        return bytes;
      },
    });
  });

  it('persists the conversation id in local storage for later sessions', () => {
    const first = getConversationId();
    sessionStorage.clear();

    const second = getConversationId();

    expect(second).toEqual(first);
    expect(localStorage.getItem('yatra.conversationId')).toEqual(first);
  });

  it('migrates an existing session conversation id to local storage', () => {
    sessionStorage.setItem('yatra.conversationId', '01K00000000000000000000000');

    const conversationId = getConversationId();

    expect(conversationId).toEqual('01K00000000000000000000000');
    expect(localStorage.getItem('yatra.conversationId')).toEqual('01K00000000000000000000000');
  });
});
