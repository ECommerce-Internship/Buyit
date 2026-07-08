namespace Buyit.Domain.Entities;

/// <summary>
/// A single line in a cart: links one Product to one Cart with a quantity.
/// This is the "join" entity that turns Cart-to-Product into many-to-many.
/// </summary>
public class CartItem
{
    public int Id { get; set; }

    public int Quantity { get; set; }

    public int CartId { get; set; }
    public Cart Cart { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
}
