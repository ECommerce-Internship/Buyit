namespace Buyit.Application.DTOs;

// The AI-generated marketing content. This is BOTH the API's output
// and the shape we deserialize Gemini's inner JSON into.
public record ProductContentResponse
(
    string Description,
    List<string> Features,
    string SeoTitle,
    string MetaDescription
);