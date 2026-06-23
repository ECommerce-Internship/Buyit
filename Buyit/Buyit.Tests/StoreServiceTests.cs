using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Tests;

public class StoreServiceTests
{
    private static StoreService BuildSut(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        db = new AppDbContext(options);
        return new StoreService(db, new Mock<ILogger<StoreService>>().Object);
    }

    [Fact]
    public async Task CreateStoreForUser_StartsPending_AndGeneratesSlug()
    {
        var sut = BuildSut(out _);

        var store = await sut.CreateStoreForUserAsync(1, "Carl Gadget Hub", "demo");

        store.Status.Should().Be("Pending");
        store.Slug.Should().Be("carl-gadget-hub");
        store.Name.Should().Be("Carl Gadget Hub");
    }

    [Fact]
    public async Task CreateStoreForUser_DuplicateName_GeneratesUniqueSlug()
    {
        var sut = BuildSut(out _);

        var first = await sut.CreateStoreForUserAsync(1, "Carl Shop", null);
        var second = await sut.CreateStoreForUserAsync(1, "Carl Shop", null);

        first.Slug.Should().Be("carl-shop");
        second.Slug.Should().Be("carl-shop-2");   // uniqueness loop kicked in
    }

    [Fact]
    public async Task CreateStoreForUser_EmptyName_ThrowsValidationException()
    {
        var sut = BuildSut(out _);

        Func<Task> act = async () => await sut.CreateStoreForUserAsync(1, "   ", null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Approve_SetsApproved()
    {
        var sut = BuildSut(out var db);
        var created = await sut.CreateStoreForUserAsync(1, "Shop", null);

        var result = await sut.ApproveAsync(created.Id);

        result.Status.Should().Be("Approved");
        (await db.Stores.FindAsync(created.Id))!.Status.Should().Be(StoreStatus.Approved);
    }

    [Fact]
    public async Task SuspendAndReject_SetSuspended()
    {
        var sut = BuildSut(out _);
        var a = await sut.CreateStoreForUserAsync(1, "A", null);
        var b = await sut.CreateStoreForUserAsync(1, "B", null);

        (await sut.SuspendAsync(a.Id)).Status.Should().Be("Suspended");
        (await sut.RejectAsync(b.Id)).Status.Should().Be("Suspended");
    }

    [Fact]
    public async Task GetPendingStores_ReturnsOnlyPending()
    {
        var sut = BuildSut(out _);
        var a = await sut.CreateStoreForUserAsync(1, "A", null);
        await sut.CreateStoreForUserAsync(1, "B", null);
        await sut.ApproveAsync(a.Id);   // A is no longer pending

        var pending = await sut.GetPendingStoresAsync();

        pending.Should().HaveCount(1);
        pending[0].Name.Should().Be("B");
    }

    [Fact]
    public async Task GetBySlug_NotApproved_ThrowsNotFound()
    {
        var sut = BuildSut(out _);
        var s = await sut.CreateStoreForUserAsync(1, "Pending Shop", null);   // stays Pending

        Func<Task> act = async () => await sut.GetBySlugAsync(s.Slug);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetBySlug_Approved_ReturnsStore()
    {
        var sut = BuildSut(out _);
        var s = await sut.CreateStoreForUserAsync(1, "Open Shop", null);
        await sut.ApproveAsync(s.Id);

        var result = await sut.GetBySlugAsync(s.Slug);

        result.Slug.Should().Be(s.Slug);
        result.Status.Should().Be("Approved");
    }
}
