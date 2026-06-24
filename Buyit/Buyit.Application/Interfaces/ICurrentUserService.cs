namespace Buyit.Application.Interfaces;

/// <summary>Reads identity facts about the caller of the current request.</summary>
public interface ICurrentUserService
{
    int? UserId { get; }      // null if unauthenticated
    string? Role { get; }     // "Customer" / "Admin" / "Seller"
    bool IsAdmin { get; }
}
