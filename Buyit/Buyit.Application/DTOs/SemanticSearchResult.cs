namespace Buyit.Application.DTOs;

// TB-156: one semantic-search hit — the product plus how close it was (0 = identical meaning).
public record SemanticSearchResult(ProductResponse Product, double Distance);
