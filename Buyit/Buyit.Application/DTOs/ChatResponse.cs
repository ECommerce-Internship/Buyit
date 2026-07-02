namespace Buyit.Application.DTOs;

// What the chatbot returns to the caller.
// - reply: the final natural-language answer from Gemini.
// - conversationId: the id of this conversation (newly created if the caller omitted one).
public record ChatResponse
(
    string reply,
    string conversationId
);