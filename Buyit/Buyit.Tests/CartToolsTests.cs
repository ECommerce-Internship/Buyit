using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Buyit.MCP.Tools;
using FluentAssertions;
using Moq;
using Xunit;

namespace Buyit.Tests;

public class CartToolsTests
{
    private static Mock<ICurrentUserService> User(int? id)
    {
        var u = new Mock<ICurrentUserService>();
        u.Setup(x => x.UserId).Returns(id);
        return u;
    }

    private static CartResponse EmptyCart() =>
        new(1, new List<CartItemResponse>(), 0, null, 0, 0, 0);

    [Fact]
    public async Task add_to_cart_UsesCallerId_AndBuildsRequest()
    {
        var cart = new Mock<ICartService>();
        cart.Setup(c => c.AddItemAsync(It.IsAny<int>(), It.IsAny<AddCartItemRequest>()))
            .ReturnsAsync(EmptyCart());

        var sut = new CartTools(cart.Object, User(42).Object);

        await sut.add_to_cart(productId: 5, quantity: 3);

        // The id came from ICurrentUserService (42), and the DTO carried the tool's args.
        cart.Verify(c => c.AddItemAsync(42,
            It.Is<AddCartItemRequest>(r => r.ProductId == 5 && r.Quantity == 3)), Times.Once);
    }

    [Fact]
    public async Task view_cart_UsesCallerId()
    {
        var cart = new Mock<ICartService>();
        cart.Setup(c => c.GetCartAsync(It.IsAny<int>())).ReturnsAsync(EmptyCart());

        var sut = new CartTools(cart.Object, User(42).Object);

        await sut.view_cart();

        cart.Verify(c => c.GetCartAsync(42), Times.Once);
    }

    [Fact]
    public async Task add_to_cart_NoUser_ThrowsUnauthorized()
    {
        var cart = new Mock<ICartService>();
        var sut = new CartTools(cart.Object, User(null).Object);

        var act = async () => await sut.add_to_cart(productId: 5, quantity: 1);

        // Fail closed: without an identity we refuse rather than touch a default/other cart.
        await act.Should().ThrowAsync<UnauthorizedException>();
        cart.Verify(c => c.AddItemAsync(It.IsAny<int>(), It.IsAny<AddCartItemRequest>()), Times.Never);
    }
}
