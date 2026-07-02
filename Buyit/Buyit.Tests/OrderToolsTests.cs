using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Exceptions;
using Buyit.MCP.Tools;
using FluentAssertions;
using Moq;
using Xunit;

namespace Buyit.Tests;

public class OrderToolsTests
{
    private static Mock<ICurrentUserService> User(int? id)
    {
        var u = new Mock<ICurrentUserService>();
        u.Setup(x => x.UserId).Returns(id);
        return u;
    }

    [Fact]
    public async Task get_my_orders_SelfScopesToCallerId_NotAModelParameter()
    {
        var orders = new Mock<IOrderService>();
        orders.Setup(o => o.GetMyOrdersAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
              .ReturnsAsync(new PaginatedResult<OrderSummaryResponse>());

        var sut = new OrderTools(orders.Object, User(42).Object);

        await sut.get_my_orders(page: 2, pageSize: 5);

        // The id came from the JWT identity (42), never from a tool parameter — the whole
        // point of get_my_orders. page/pageSize are passed straight through.
        orders.Verify(o => o.GetMyOrdersAsync(42, 2, 5), Times.Once);
    }

    [Fact]
    public async Task get_my_orders_NoUser_ThrowsUnauthorized()
    {
        var orders = new Mock<IOrderService>();
        var sut = new OrderTools(orders.Object, User(null).Object);

        var act = async () => await sut.get_my_orders();

        await act.Should().ThrowAsync<UnauthorizedException>();
        orders.Verify(o => o.GetMyOrdersAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }
}
