const alphabet = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
let lastTimestamp = 0;

function encodeTime(time: number): string {
  let value = Math.max(0, Math.min(time, 0xffffffffffff));
  let output = '';

  for (let index = 0; index < 10; index += 1) {
    output = alphabet[value % 32] + output;
    value = Math.floor(value / 32);
  }

  return output;
}

function encodeRandom(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  let output = '';

  for (let index = 0; index < 16; index += 1) {
    output += alphabet[bytes[index] & 31];
  }

  return output;
}

export function newUlid(): string {
  const now = Date.now();
  lastTimestamp = now <= lastTimestamp ? lastTimestamp + 1 : now;
  return `${encodeTime(lastTimestamp)}${encodeRandom()}`;
}

export function getConversationId(): string {
  const key = 'yatra.conversationId';
  const existing = localStorage.getItem(key);
  if (existing) {
    return existing;
  }

  const sessionConversationId = sessionStorage.getItem(key);
  if (sessionConversationId) {
    localStorage.setItem(key, sessionConversationId);
    return sessionConversationId;
  }

  const created = newUlid();
  localStorage.setItem(key, created);
  return created;
}
