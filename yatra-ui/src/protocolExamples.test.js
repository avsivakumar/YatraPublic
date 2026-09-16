// @vitest-environment node
import { readFileSync } from 'node:fs';
import { expect, it } from 'vitest';
import { definitionError } from './formValidation';

it('accepts published request examples', () => {
  const protocol = readFileSync('../docs/Message-Protocol.md', 'utf8');
  const examples = [...protocol.matchAll(/```json\s*([\s\S]*?)```/g)].map(match => JSON.parse(match[1]));
  const requests = examples.filter(example => Array.isArray(example.fields));
  expect(requests.length).toBeGreaterThan(0);
  for (const request of requests) expect(definitionError(request)).toBeNull();
});
