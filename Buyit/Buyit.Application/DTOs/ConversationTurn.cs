namespace Buyit.Application.DTOs;

// One line of persisted dialogue in a conversation.
// Role is "user" or "model" (the two roles Gemini understands); Text is the message/reply.
public record ConversationTurn(string Role, string Text);
