using Buyit.Application.DTOs;

namespace Buyit.Application.Interfaces;

// Contract for the AI product-content generator (TB-46).
public interface IGeminiService
{
    // Generates marketing content for a product by calling the Gemini API.
    // Throws ValidationException (400) if the caller's input is invalid,
    // and ExternalServiceException (502) if Gemini fails or returns bad data.
    Task<ProductContentResponse> GenerateProductContentAsync(
        string productName,
        string category,
        string specs,
        CancellationToken cancellationToken = default);
}