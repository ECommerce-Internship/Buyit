namespace Buyit.Application.DTOs;

// What the caller POSTs to ask for AI-generated content.
public record GenerateProductContentRequest
(
    string ProductName,
    string Category,
    string Specs
);