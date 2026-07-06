namespace Buyit.Application.DTOs;

// What the caller POSTs to the chatbot.
// - message: the user's plain-English question (required).
// - conversationId: identifies an ongoing conversation. Optional — omit it on the
//   first message; the service will create a fresh id and return it.
public record ChatRequest
(
    string message,
    string? conversationId
);