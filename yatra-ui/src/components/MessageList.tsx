import type { ReactNode } from 'react';
import type { FormRequestPayload, TextPayload, YatraErrorPayload, YatraMessage } from '../types';

interface MessageListProps {
  messages: YatraMessage[];
  renderForm: (message: YatraMessage<FormRequestPayload>) => ReactNode;
}

export function MessageList({ messages, renderForm }: MessageListProps) {
  if (messages.length === 0) {
    return <div className="empty-state">No messages yet</div>;
  }

  return (
    <ol className="message-list">
      {messages.filter(message => message.type !== 'form.response').map((message) => (
        <li key={message.messageId} className={`message-row message-row-${message.type.replace('.', '-')}`}>
          {renderMessage(message, renderForm)}
        </li>
      ))}
    </ol>
  );
}

function renderMessage(
  message: YatraMessage,
  renderForm: (message: YatraMessage<FormRequestPayload>) => ReactNode,
) {
  switch (message.type) {
    case 'user.text':
      return <TextBubble align="right" label="You" text={(message.payload as TextPayload).text} />;
    case 'agent.text':
      return <TextBubble align="left" label="Yatra" text={(message.payload as TextPayload).text} />;
    case 'form.request':
      return renderForm(message as YatraMessage<FormRequestPayload>);
    case 'status':
      return <div className="status-message">{(message.payload as { text?: string }).text ?? 'Status update'}</div>;
    case 'error': {
      const error = message.payload as YatraErrorPayload;
      return (
        <div className="error-message" role="alert">
          <strong>{error.code}</strong>
          <span>{error.message}</span>
        </div>
      );
    }
    default:
      console.warn('Unsupported Yatra message type', message.type);
      return <div className="unsupported-message">Unsupported message</div>;
  }
}

function TextBubble({ align, label, text }: { align: 'left' | 'right'; label: string; text: string }) {
  return (
    <article className={`message-bubble message-bubble-${align}`}>
      <span>{label}</span>
      <p>{text}</p>
    </article>
  );
}
