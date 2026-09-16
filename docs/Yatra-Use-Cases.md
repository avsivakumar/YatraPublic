# Yatra Use Cases

Yatra supports situations in which an agent needs a person to read, decide, select, correct, confirm, or supply structured information during a conversation.

## Common uses

- **Missing information:** collect only the fields the agent still needs.
- **Clarification:** present constrained choices instead of asking the agent to infer an important detail.
- **Review and correction:** initialize a form with extracted or generated values so the user can verify and edit them.
- **Confirmation:** capture explicit confirmation before the application performs an action.
- **Approval or rejection:** present an approval decision while the application owns identity, authority, evidence, and execution.
- **Exception resolution:** explain an exceptional condition and offer permitted resolutions.
- **Guided data collection:** request different structured information as the conversation evolves.
- **Tool parameters:** gather complete, typed values before an agent invokes a tool.
- **Alternative selection:** let a user choose among agent-generated options using stable submitted identifiers.
- **Cancellation:** distinguish an intentional cancel action from an incomplete or invalid response.

## Human-in-the-loop pattern

1. The agent runtime determines that human participation is needed.
2. The agent sends a Yatra form request.
3. Yatra presents the interaction and collects the response.
4. The response returns with stable correlation.
5. The agent runtime validates it and decides how execution continues.

Yatra enables this interaction but does not implement agent suspension, approval policy, authorization, escalation, or workflow continuation.

## Responsibility boundary

| Responsibility | Owner |
| --- | --- |
| Determine that human interaction is needed | Agent or application |
| Describe fields and choices | Agent or application |
| Render the interaction | Yatra UI |
| Capture, persist, and transport the response | Yatra UI and engine |
| Apply business validation | Agent or application |
| Determine authority and consequences | Business application or workflow |
| Continue, stop, or escalate processing | Agent runtime or workflow |

This boundary allows Yatra to serve many applications without becoming coupled to their business processes.
