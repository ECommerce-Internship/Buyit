namespace Buyit.Application.DTOs;

// TB-47: the body the Admin POSTs to /api/v1/products/{id}/generate-content.
// Only the specs are supplied here. The product's Name and Category are read
// from the database using the {id} in the URL — NOT sent by the caller.
public record GenerateContentRequest
(
    string Specs
);