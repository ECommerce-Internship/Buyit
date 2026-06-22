using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using ValidationException = Buyit.Domain.Exceptions.ValidationException;

namespace Buyit.Tests;

public class ReviewServiceTests
{
    // Builds a ReviewService backed by a FRESH in-memory database + passing validator + no-op logger.
    private static ReviewService BuildSut(out AppDbContext db)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        db = new AppDbContext(options);

        // Validator: always passes (validation rules are tested elsewhere, not here).
        var validatorMock = new Mock<IValidator<SubmitReviewRequest>>();
        validatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<SubmitReviewRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        // Cache is fire-and-forget here; a no-op mock satisfies the dependency.
        var cacheMock = new Mock<ICacheService>();

        var loggerMock = new Mock<ILogger<ReviewService>>();

        return new ReviewService(db, validatorMock.Object, cacheMock.Object, loggerMock.Object);
    }

    // Seeds a user + product. If 'delivered' is true, also seeds a Delivered order for that product.
    private static async Task<(int userId, int productId)> SeedAsync(AppDbContext db, bool delivered)
    {
        var user = new User
        {
            FirstName = "Rev",
            LastName = "User",
            Email = $"rev-{Guid.NewGuid()}@buyit.com",
            PasswordHash = "irrelevant",
            Role = UserRole.Customer
        };
        db.Users.Add(user);

        var category = new Category { Name = "Electronics" };
        var product = new Product
        {
            Name = "Headphones",
            Description = "Noise-cancelling",
            Sku = $"HP-{Guid.NewGuid()}",
            Price = 199.99m,
            Category = category
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        if (delivered)
        {
            var order = new Order
            {
                UserId = user.Id,
                Status = OrderStatus.Delivered,
                TotalAmount = 199.99m,
                ShippingLine1 = "1 Test St",
                ShippingCity = "Testville",
                ShippingPostalCode = "0000",
                ShippingCountry = "Testland",
                OrderItems = new List<OrderItem>
                {
                    new() { ProductId = product.Id, Quantity = 1, UnitPrice = 199.99m }
                }
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        return (user.Id, product.Id);
    }

    // ---- THE TICKET'S REQUIRED TEST ----
    [Fact]
    public async Task SubmitReview_WithoutDeliveredOrder_ThrowsForbiddenException()
    {
        // Arrange: user + product, but NO delivered order.
        var sut = BuildSut(out var db);
        var (userId, productId) = await SeedAsync(db, delivered: false);
        var request = new SubmitReviewRequest(5, "Great!");

        // Act
        Func<Task> act = () => sut.SubmitReviewAsync(userId, productId, request);

        // Assert: 403 with the exact business message.
        await act.Should().ThrowAsync<ForbiddenException>()
            .WithMessage("You can only review products you have received");
    }

    [Fact]
    public async Task SubmitReview_WithDeliveredOrder_CreatesReview()
    {
        // Arrange: user + product + a Delivered order for that product.
        var sut = BuildSut(out var db);
        var (userId, productId) = await SeedAsync(db, delivered: true);
        var request = new SubmitReviewRequest(4, "Solid");

        // Act
        var result = await sut.SubmitReviewAsync(userId, productId, request);

        // Assert: returned DTO is correct AND the row is persisted.
        result.Rating.Should().Be(4);
        result.ReviewerName.Should().Be("Rev User");
        (await db.Reviews.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SubmitReview_Twice_ThrowsConflictException()
    {
        // Arrange: a delivered order, and one review already submitted.
        var sut = BuildSut(out var db);
        var (userId, productId) = await SeedAsync(db, delivered: true);
        await sut.SubmitReviewAsync(userId, productId, new SubmitReviewRequest(5, "First"));

        // Act: try to review the same product again.
        Func<Task> act = () => sut.SubmitReviewAsync(userId, productId, new SubmitReviewRequest(3, "Again"));

        // Assert
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task UpdateReview_OlderThan48Hours_ThrowsValidationException()
    {
        // Arrange: a delivered order + a review created 49 hours ago.
        var sut = BuildSut(out var db);
        var (userId, productId) = await SeedAsync(db, delivered: true);
        var oldReview = new Review
        {
            UserId = userId,
            ProductId = productId,
            Rating = 5,
            Comment = "Old",
            CreatedAt = DateTime.UtcNow.AddHours(-49)
        };
        db.Reviews.Add(oldReview);
        await db.SaveChangesAsync();

        // Act
        Func<Task> act = () => sut.UpdateReviewAsync(userId, oldReview.Id, new SubmitReviewRequest(1, "Changed"));

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task UpdateReview_NotOwner_ThrowsForbiddenException()
    {
        // Arrange: a review owned by user A; user B (id 99999) tries to edit it.
        var sut = BuildSut(out var db);
        var (ownerId, productId) = await SeedAsync(db, delivered: true);
        var review = new Review
        {
            UserId = ownerId,
            ProductId = productId,
            Rating = 5,
            CreatedAt = DateTime.UtcNow
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        // Act
        Func<Task> act = () => sut.UpdateReviewAsync(99999, review.Id, new SubmitReviewRequest(1, "Hack"));

        // Assert
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task DeleteAsAdmin_AnyReview_RemovesIt()
    {
        // Arrange: a review owned by some user.
        var sut = BuildSut(out var db);
        var (ownerId, productId) = await SeedAsync(db, delivered: true);
        var review = new Review
        {
            UserId = ownerId,
            ProductId = productId,
            Rating = 5,
            CreatedAt = DateTime.UtcNow
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        // Act: admin deletes it (no ownership check).
        await sut.DeleteAsAdminAsync(review.Id);

        // Assert
        (await db.Reviews.CountAsync()).Should().Be(0);
    }

    // ---- GetByProductIdAsync ----

    [Fact]
    public async Task GetByProductId_NoReviews_ReturnsZeroAverageAndEmptyPage()
    {
        // Arrange: a product with no reviews at all.
        var sut = BuildSut(out var db);
        var (_, productId) = await SeedAsync(db, delivered: false);

        // Act
        var result = await sut.GetByProductIdAsync(productId, page: 1, pageSize: 10);

        // Assert: the empty-set guard returns 0, not a thrown AverageAsync.
        result.AverageRating.Should().Be(0);
        result.TotalCount.Should().Be(0);
        result.Reviews.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetByProductId_WithReviews_ComputesAverageAndPaginates()
    {
        // Arrange: 3 reviews (ratings 2,4,5 → average 3.6667) on one product.
        var sut = BuildSut(out var db);
        var (ownerId, productId) = await SeedAsync(db, delivered: true);
        foreach (var rating in new[] { 2, 4, 5 })
        {
            // Distinct users so each review is allowed (one-per-user-per-product).
            var u = new User
            {
                FirstName = "R", LastName = rating.ToString(),
                Email = $"u-{Guid.NewGuid()}@buyit.com",
                PasswordHash = "x", Role = UserRole.Customer
            };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            db.Reviews.Add(new Review { UserId = u.Id, ProductId = productId, Rating = rating, CreatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();

        // Act: ask for page 1 with a page size of 2.
        var result = await sut.GetByProductIdAsync(productId, page: 1, pageSize: 2);

        // Assert: totals/average across ALL reviews; only one page's worth of items.
        result.TotalCount.Should().Be(3);
        result.AverageRating.Should().BeApproximately(3.6667, 0.001);
        result.Reviews.Items.Should().HaveCount(2);
        result.Reviews.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetByProductId_MissingProduct_ThrowsNotFoundException()
    {
        var sut = BuildSut(out _);

        Func<Task> act = () => sut.GetByProductIdAsync(999999, page: 1, pageSize: 10);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- SubmitReviewAsync (extra paths) ----

    [Fact]
    public async Task SubmitReview_MissingProduct_ThrowsNotFoundException()
    {
        var sut = BuildSut(out _);

        Func<Task> act = () => sut.SubmitReviewAsync(1, 999999, new SubmitReviewRequest(5, "x"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- UpdateReviewAsync happy path ----

    [Fact]
    public async Task UpdateReview_WithinWindow_UpdatesRatingAndComment()
    {
        // Arrange: a fresh review owned by the user.
        var sut = BuildSut(out var db);
        var (userId, productId) = await SeedAsync(db, delivered: true);
        var review = new Review
        {
            UserId = userId, ProductId = productId, Rating = 3, Comment = "ok", CreatedAt = DateTime.UtcNow
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        // Act
        var result = await sut.UpdateReviewAsync(userId, review.Id, new SubmitReviewRequest(5, "Excellent"));

        // Assert: returned DTO AND the persisted row both reflect the change.
        result.Rating.Should().Be(5);
        result.Comment.Should().Be("Excellent");
        var persisted = await db.Reviews.FindAsync(review.Id);
        persisted!.Rating.Should().Be(5);
        persisted.Comment.Should().Be("Excellent");
    }

    [Fact]
    public async Task UpdateReview_MissingReview_ThrowsNotFoundException()
    {
        var sut = BuildSut(out _);

        Func<Task> act = () => sut.UpdateReviewAsync(1, 999999, new SubmitReviewRequest(5, "x"));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---- DeleteReviewAsync (customer) ----

    [Fact]
    public async Task DeleteReview_Owner_RemovesIt()
    {
        // Arrange: a review owned by the user.
        var sut = BuildSut(out var db);
        var (userId, productId) = await SeedAsync(db, delivered: true);
        var review = new Review
        {
            UserId = userId, ProductId = productId, Rating = 5, CreatedAt = DateTime.UtcNow
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        // Act
        await sut.DeleteReviewAsync(userId, review.Id);

        // Assert
        (await db.Reviews.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task DeleteReview_NotOwner_ThrowsForbiddenException()
    {
        // Arrange: a review owned by user A; user B (99999) tries to delete it.
        var sut = BuildSut(out var db);
        var (ownerId, productId) = await SeedAsync(db, delivered: true);
        var review = new Review
        {
            UserId = ownerId, ProductId = productId, Rating = 5, CreatedAt = DateTime.UtcNow
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync();

        // Act
        Func<Task> act = () => sut.DeleteReviewAsync(99999, review.Id);

        // Assert: not removed, and 403.
        await act.Should().ThrowAsync<ForbiddenException>();
        (await db.Reviews.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteReview_MissingReview_ThrowsNotFoundException()
    {
        var sut = BuildSut(out _);

        Func<Task> act = () => sut.DeleteReviewAsync(1, 999999);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}