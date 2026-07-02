using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// Contract for the AI chatbot engine (TB-97).
// Runs the Gemini <-> Buyit.MCP function-calling loop and returns a final answer.
public interface IChatService
{
    // Sends the user's message through the Gemini + MCP loop and returns the final reply.
    // Throws ValidationException (400) if the request is invalid, or
    // ExternalServiceException (502) if Gemini or the MCP client fails.
    Task<ChatResponse> SendMessageAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}