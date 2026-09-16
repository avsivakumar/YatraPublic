import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { Send, Trash2 } from 'lucide-react';
import { openMessageStream, submitMessage } from './api';
import { DynamicForm } from './components/DynamicForm';
import { MessageList } from './components/MessageList';
import { newUlid, getConversationId } from './ulid';
import { readSaved, saveState } from './persistence';
import type {
  FormOutcome,
  FormResponseAction,
  FormResponsePayload,
  FormSubmissionReceipt,
  TextPayload,
  YatraErrorPayload,
  YatraMessage,
} from './types';

type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'error';
type FormSubmission = {
  formMessageId: string;
  responseMessageId: string;
  action: FormResponseAction;
};

export function App() {
  const conversationId = useMemo(() => getConversationId(), []);
  const [messages, setMessages] = useState<YatraMessage[]>(() => readSaved(`messages:${conversationId}`, []));
  const [composerText, setComposerText] = useState(() => readSaved(`composer:${conversationId}`, ''));
  const [connectionState, setConnectionState] = useState<ConnectionState>('connecting');
  const [isSending, setIsSending] = useState(false);
  const [streamAttempt, setStreamAttempt] = useState(0);
  const responseDrafts = useRef<Record<string, { id: string; response: FormResponsePayload; rejected?: boolean }>>(readSaved(`responses:${conversationId}`, {}));
  const [localError, setLocalError] = useState<string | null>(null);
  const [formOutcomes, setFormOutcomes] = useState<Record<string, FormOutcome>>(() => readSaved(`outcomes:${conversationId}`, {}));
  const messagesRef = useRef<YatraMessage[]>(messages);
  const clearedIds = useRef(new Set(readSaved<string[]>(`cleared:${conversationId}`, [])));
  const submissionsRef = useRef<Record<string, FormSubmission>>(readSaved(`submissions:${conversationId}`, {}));
  const conversationRef = useRef<HTMLElement | null>(null);
  const conversationEndRef = useRef<HTMLDivElement | null>(null);
  const isConnected = connectionState === 'connected';

  useEffect(() => { saveState(`messages:${conversationId}`, messages); }, [conversationId, messages]);
  useEffect(() => { saveState(`composer:${conversationId}`, composerText); }, [conversationId, composerText]);
  useEffect(() => { saveState(`outcomes:${conversationId}`, formOutcomes); }, [conversationId, formOutcomes]);

  const rememberSubmission = useCallback((submission: FormSubmission) => {
    submissionsRef.current = {
      ...submissionsRef.current,
      [submission.responseMessageId]: submission,
    };
    saveState(`submissions:${conversationId}`, submissionsRef.current);
  }, [conversationId]);

  const forgetSubmission = useCallback((responseMessageId: string) => {
    const { [responseMessageId]: _removed, ...remaining } = submissionsRef.current;
    submissionsRef.current = remaining;
    saveState(`submissions:${conversationId}`, remaining);
  }, [conversationId]);

  const handleStreamMessage = useCallback((message: YatraMessage) => {
    if (message.type === 'form.response' && message.inReplyTo) {
      const latest = responseDrafts.current[message.inReplyTo];
      if (latest && latest.id !== message.messageId) return;
      const response = message.payload as FormResponsePayload;
      rememberSubmission({ formMessageId: message.inReplyTo, responseMessageId: message.messageId, action: response.action });
      setFormOutcomes(current => {
        const existing = current[message.inReplyTo!];
        if (existing?.responseMessageId === message.messageId) return current;
        return { ...current, [message.inReplyTo!]: {
          status: 'submitted', responseMessageId: message.messageId,
        } };
      });
      return;
    }
    if (message.type === 'agent.text' && message.inReplyTo) {
      const submission = submissionsRef.current[message.inReplyTo];
      if (!submission) {
        return;
      }

      const latest = responseDrafts.current[submission.formMessageId];
      if (latest && latest.id !== submission.responseMessageId) return;

      forgetSubmission(submission.responseMessageId);
      setFormOutcomes((current) => ({
        ...current,
        [submission.formMessageId]: {
          status: submission.action === 'cancel' ? 'cancelled' : 'completed',
          responseMessageId: submission.responseMessageId,
        },
      }));
      return;
    }

    if (message.type !== 'error') {
      return;
    }

    const error = message.payload as Partial<YatraErrorPayload>;
    const referencedIds = [message.inReplyTo, error.relatedMessageId].filter(Boolean) as string[];
    const submission = referencedIds
      .map((id) => submissionsRef.current[id])
      .find((candidate): candidate is FormSubmission => Boolean(candidate));
    const formMessageId = submission?.formMessageId ?? referencedIds.find((id) =>
      messagesRef.current.some((candidate) => candidate.messageId === id && candidate.type === 'form.request'));

    if (!formMessageId) {
      return;
    }

    if (submission) {
      const draft = responseDrafts.current[submission.formMessageId];
      if (draft && draft.id !== submission.responseMessageId) return;
      if (draft) draft.rejected = true;
      saveState(`responses:${conversationId}`, responseDrafts.current);
      forgetSubmission(submission.responseMessageId);
    }

    setFormOutcomes((current) => ({
      ...current,
      [formMessageId]: {
        status: error.retryable === true ? 'failed-retryable' : 'failed-terminal',
        message: error.message ?? 'The response could not be processed.',
        responseMessageId: submission?.responseMessageId,
      },
    }));
  }, [forgetSubmission, rememberSubmission, conversationId]);

  useEffect(() => {
    const stream = openMessageStream(conversationId, (message) => {
      if (clearedIds.current.has(message.messageId)) return;
      if (messagesRef.current.some(current => current.messageId === message.messageId)) return;
      messagesRef.current = appendUnique(messagesRef.current, message);
      setMessages(current => appendUnique(current, message));
      handleStreamMessage(message);
    }, setConnectionState);

    return () => stream.close();
  }, [conversationId, handleStreamMessage, streamAttempt]);

  function clearMessages() {
    const next = new Set([...clearedIds.current, ...messages.map(message => message.messageId)]);
    if (!saveState(`cleared:${conversationId}`, [...next]) || !saveState(`messages:${conversationId}`, [])) {
      setLocalError('Messages could not be cleared from browser storage. Please try again.');
      return;
    }
    clearedIds.current = next;
    messagesRef.current = [];
    setMessages([]);
    setLocalError(null);
  }

  useLayoutEffect(() => {
    if (messages.length === 0) {
      return;
    }

    const animationFrame = window.requestAnimationFrame(() => {
      const conversationEnd = conversationEndRef.current;
      if (!conversationEnd) {
        return;
      }

      const conversation = conversationRef.current;
      if (conversation) {
        if (typeof conversation.scrollTo === 'function') {
          conversation.scrollTo({ top: conversation.scrollHeight, behavior: 'smooth' });
        } else {
          conversation.scrollTop = conversation.scrollHeight;
        }

        return;
      }

      if (typeof conversationEnd.scrollIntoView === 'function') {
        conversationEnd.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        return;
      }
    });

    return () => window.cancelAnimationFrame(animationFrame);
  }, [messages.length]);

  const sendUserText = useCallback(async (text: string) => {
    const trimmed = text.trim();
    if (!isConnected || !trimmed) {
      return;
    }

    const messageId = newUlid();
    const optimisticMessage: YatraMessage<TextPayload> = {
      messageId,
      conversationId,
      type: 'user.text',
      source: 'ui:current-conversation',
      destination: 'engine:inbound',
      createdAtUtc: new Date().toISOString(),
      expectsReply: false,
      payload: { text: trimmed },
    };

    setIsSending(true);
    setLocalError(null);
    setMessages((current) => appendUnique(current, optimisticMessage));

    try {
      await submitMessage(conversationId, {
        messageId,
        type: 'user.text',
        payload: { text: trimmed },
      });
      setComposerText('');
    } catch {
      setLocalError('Message was not accepted.');
    } finally {
      setIsSending(false);
    }
  }, [conversationId, isConnected]);

  const sendFormResponse = useCallback(async (
    formMessage: YatraMessage,
    response: FormResponsePayload,
  ): Promise<FormSubmissionReceipt> => {
    const previous = responseDrafts.current[formMessage.messageId];
    const comparable = (value: FormResponsePayload) => JSON.stringify({ ...value, replacesResponseId: undefined });
    const reuse = previous && !previous.rejected && comparable(previous.response) === comparable(response);
    const responseMessageId = reuse ? previous.id : newUlid();
    response = reuse ? previous.response : { ...response, ...(previous ? { replacesResponseId: previous.id } : {}) };
    responseDrafts.current[formMessage.messageId] = { id: responseMessageId, response };
    if (!saveState(`responses:${conversationId}`, responseDrafts.current)) {
      throw new Error('The response identity could not be saved.');
    }
    const submission: FormSubmission = {
      formMessageId: formMessage.messageId,
      responseMessageId,
      action: response.action,
    };

    rememberSubmission(submission);

    await submitMessage(conversationId, {
      messageId: responseMessageId,
      type: 'form.response',
      inReplyTo: formMessage.messageId,
      payload: response,
    });

    return { responseMessageId };
  }, [conversationId, forgetSubmission, rememberSubmission]);

  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <h1>Yatra</h1>
          <p>{conversationId}</p>
        </div>
        <div className="connection-actions">
          <span className={`connection connection-${connectionState}`}>{connectionState}</span>
          <button type="button" className="clear-messages" aria-label="Clear all messages"
            title="Clear all messages" disabled={messages.length === 0} onClick={clearMessages}>
            <Trash2 size={18} />
          </button>
        </div>
      </header>

      {connectionState === 'disconnected' && <div role="alert">Connection lost. <button type="button" onClick={() => setStreamAttempt(value => value + 1)}>Retry connection</button></div>}

      <section className="conversation" aria-label="Conversation" ref={conversationRef}>
        <MessageList
          messages={messages}
          renderForm={(message) => (
            <DynamicForm
              message={message}
              canSubmit={true}
              outcome={formOutcomes[message.messageId]}
              onSubmit={sendFormResponse}
            />
          )}
        />
        <div className="conversation-end" ref={conversationEndRef} aria-hidden="true" />
      </section>

      {localError && <div className="local-error" role="alert">{localError}</div>}

      <form
        className="composer"
        onSubmit={(event) => {
          event.preventDefault();
          void sendUserText(composerText);
        }}
      >
        <input
          aria-label="Message"
          value={composerText}
          onChange={(event) => setComposerText(event.target.value)}
          placeholder="Message Yatra"
          maxLength={8000}
        />
        <button
          type="submit"
          disabled={!isConnected || isSending || composerText.trim().length === 0}
          aria-label="Send message"
        >
          <Send size={18} />
        </button>
      </form>
    </main>
  );
}

function appendUnique(messages: YatraMessage[], next: YatraMessage): YatraMessage[] {
  if (messages.some((message) => message.messageId === next.messageId)) {
    return messages;
  }

  return [...messages, next];
}
