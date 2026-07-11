using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using FluentValidation.Results;
using OfficeOpenXml;
using Buyit.Application.Common;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buyit.Tests;

public class ProductServiceTests
{
    // Runs ONCE before any test in this class. Sets the EPPlus license (the running app
    // does this in Program.cs, but tests never run Program.cs).
    static ProductServiceTests()
    {
        ExcelPackage.License.SetNonCommercialPersonal("Carl Ibrahim");
    }

    // Builds a brand-new ProductService backed by a fresh, isolated in-memory database.
    // Validators are mocked to ALWAYS pass — TB-34 tests ProductService logic, not validation.
    private static ProductService BuildSut(
        out AppDbContext db,
        out Mock<IValidator<CreateProductRequest>> createValidatorMock,
        out Mock<IValidator<UpdateProductRequest>> updateValidatorMock,
        out Mock<IGeminiService> geminiMock,
        out Mock<IEmbeddingService> embeddingMock,
        ICurrentUserService? currentUser = null)   // override to test non-admin ownership paths
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        db = new AppDbContext(options);

        // Every product needs an owning store (StoreId is NOT NULL). Seed one approved
        // store (Id = 1) that tests assign their products to.
        db.Stores.Add(new Store
        {
            Id = 1,
            OwnerUserId = 1,
            Name = "Test Store",
            Slug = "test-store",
            Status = StoreStatus.Approved,
            CommissionRate = 0m
        });
        db.SaveChanges();

        createValidatorMock = new Mock<IValidator<CreateProductRequest>>();
        createValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<CreateProductRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        updateValidatorMock = new Mock<IValidator<UpdateProductRequest>>();
        updateValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<UpdateProductRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        // Cache is mocked: GetAsync returns null by default (a cache MISS), so every test still
        // exercises the real database path. The write/remove calls become harmless no-ops.
        var cacheMock = new Mock<ICacheService>();
        // Blob storage is mocked: these tests exercise DB/cache logic, not Azure. Its
        // methods become harmless no-ops (UploadAsync returns null, DeleteAsync does nothing).
        var blobMock = new Mock<IBlobStorageService>();
        var loggerMock = new Mock<ILogger<ProductService>>();

        // TB-47: the AI generator is mocked so tests NEVER call the real Gemini API.
        // Exposed via the out-parameter so a test can set up its canned reply.
        geminiMock = new Mock<IGeminiService>();

        // TB-47: validator mocked to ALWAYS pass — we test service logic, not validation rules.
        var generateContentValidatorMock = new Mock<IValidator<GenerateContentRequest>>();
        generateContentValidatorMock
            .Setup(v => v.ValidateAsync(It.IsAny<GenerateContentRequest>(), default))
            .ReturnsAsync(new ValidationResult());

        // Current user mocked as an Admin so ownership checks pass — these tests exercise
        // ProductService logic, not authorization (dedicated ownership tests come in TB-131).
        var currentUserMock = new Mock<ICurrentUserService>();
        currentUserMock.Setup(c => c.IsAdmin).Returns(true);
        currentUserMock.Setup(c => c.UserId).Returns(1);

        // TB-156: embedder mocked. Defaults to a valid 768-length vector so create/update (which
        // embed best-effort) succeed silently; tests that care override this via the out param.
        embeddingMock = new Mock<IEmbeddingService>();
        embeddingMock
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        // Semantic relevance cutoff comes from GeminiSettings; the default is fine for these tests
        // (the CosineDistance ranking/threshold runs only in Postgres, not the in-memory provider).
        var geminiSettings = Options.Create(new GeminiSettings());

        return new ProductService(
            db,
            createValidatorMock.Object,
            updateValidatorMock.Object,
            cacheMock.Object,
            blobMock.Object,
            currentUser ?? currentUserMock.Object,
            geminiMock.Object,
            generateContentValidatorMock.Object,
            embeddingMock.Object,
            geminiSettings,
            loggerMock.Object);
    }

    // Builds a real .xlsx in memory in the importer's column order:
    // Name, SKU, Price, CategoryName, Description, InitialStock (headers on row 1, data from row 2).
    private static Stream BuildExcel(params (string Name, string Sku, string Price,
                                             string Category, string Description, string Stock)[] rows)
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Products");

        sheet.Cells[1, 1].Value = "Name";
        sheet.Cells[1, 2].Value = "SKU";
        sheet.Cells[1, 3].Value = "Price";
        sheet.Cells[1, 4].Value = "CategoryName";
        sheet.Cells[1, 5].Value = "Description";
        sheet.Cells[1, 6].Value = "InitialStock";

        int row = 2;
        foreach (var r in rows)
        {
            sheet.Cells[row, 1].Value = r.Name;
            sheet.Cells[row, 2].Value = r.Sku;
            sheet.Cells[row, 3].Value = r.Price;
            sheet.Cells[row, 4].Value = r.Category;
            sheet.Cells[row, 5].Value = r.Description;
            sheet.Cells[row, 6].Value = r.Stock;
            row++;
        }

        var stream = new MemoryStream();
        package.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task CreateProduct_ValidRequest_ReturnsProductResponse()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        await db.SaveChangesAsync();

        var request = new CreateProductRequest
        {
            Name = "Wireless Mouse",
            Description = "Ergonomic 2.4GHz mouse",
            Sku = "MOUSE-001",
            Price = 24.99m,
            CategoryId = 1,
            StoreId = 1,
            InitialStock = 50
        };

        ProductResponse result = await sut.CreateAsync(request);

        result.Should().NotBeNull();
        result.Name.Should().Be("Wireless Mouse");
        result.Price.Should().Be(24.99m);
        result.Sku.Should().Be("MOUSE-001");

        var saved = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Sku == "MOUSE-001");
        saved.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateProduct_DuplicateSKU_ThrowsConflictException()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        db.Products.Add(new Product
        {
            Id = 100,
            Name = "Existing Mouse",
            Description = "Already in catalogue",
            Sku = "MOUSE-001",
            Price = 19.99m,
            CategoryId = 1,
            StoreId = 1
        });
        await db.SaveChangesAsync();

        var request = new CreateProductRequest
        {
            Name = "Another Mouse",
            Description = "Different product, same SKU",
            Sku = "MOUSE-001",
            Price = 29.99m,
            CategoryId = 1,
            StoreId = 1,
            InitialStock = 10
        };

        Func<Task> act = async () => await sut.CreateAsync(request);

        await act.Should().ThrowAsync<ConflictException>()
                 .WithMessage("*already exists*");
    }

    [Fact]
    public async Task GetProductById_ExistingId_ReturnsProductResponse()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Categories.Add(new Category { Id = 7, Name = "Audio" });
        db.Products.Add(new Product
        {
            Id = 42,
            Name = "Studio Headphones",
            Description = "Over-ear, 32 ohm",
            Sku = "HEAD-042",
            Price = 149.50m,
            CategoryId = 7,
            StoreId = 1,
            ImageUrl = "https://example.com/h.png",
            Inventory = new Inventory { QuantityInStock = 8 }
        });
        await db.SaveChangesAsync();

        ProductResponse result = await sut.GetByIdAsync(42);

        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.Name.Should().Be("Studio Headphones");
        result.Description.Should().Be("Over-ear, 32 ohm");
        result.Sku.Should().Be("HEAD-042");
        result.Price.Should().Be(149.50m);
        result.ImageUrl.Should().Be("https://example.com/h.png");
        result.CategoryId.Should().Be(7);
        result.CategoryName.Should().Be("Audio");
        result.QuantityInStock.Should().Be(8);
        result.AverageRating.Should().Be(0);
    }

    [Fact]
    public async Task GetProductById_NotFound_ThrowsNotFoundException()
    {
        var sut = BuildSut(out _, out _, out _, out _, out _);

        Func<Task> act = async () => await sut.GetByIdAsync(999);

        await act.Should().ThrowAsync<NotFoundException>()
                 .WithMessage("*was not found*");
    }

    [Fact]
    public async Task DeleteProduct_ExistingProduct_SetsIsDeletedTrue()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        db.Products.Add(new Product
        {
            Id = 5,
            Name = "Keyboard",
            Description = "Mechanical",
            Sku = "KEY-005",
            Price = 89.99m,
            CategoryId = 1,
            IsDeleted = false
        });
        await db.SaveChangesAsync();

        await sut.DeleteAsync(5);

        var deleted = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == 5);
        deleted.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task ImportExcel_ValidRows_ReturnsCorrectAddedCount()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        await db.SaveChangesAsync();

        var excel = BuildExcel(
            ("Mouse",    "IMP-001", "10.00", "Electronics", "Desc 1", "5"),
            ("Keyboard", "IMP-002", "20.00", "Electronics", "Desc 2", "5"),
            ("Monitor",  "IMP-003", "30.00", "Electronics", "Desc 3", "5"),
            ("Webcam",   "IMP-004", "40.00", "Electronics", "Desc 4", "5"),
            ("Headset",  "IMP-005", "50.00", "Electronics", "Desc 5", "5")
        );

        ImportResultDto result = await sut.ImportAsync(excel);

        result.AddedCount.Should().Be(5);
        result.FailedCount.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportExcel_InvalidPrice_IncludesRowInFailedList()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        await db.SaveChangesAsync();

        var excel = BuildExcel(
            ("Broken", "BAD-001", "abc", "Electronics", "Bad price row", "5")
        );

        ImportResultDto result = await sut.ImportAsync(excel);

        result.AddedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle()
              .Which.Row.Should().Be(2);
        result.Errors[0].Reason.Should().Contain("Price");
    }

    // --- Seller bulk import (store-scoped): every row lands in the given store, ownership is
    // enforced, and SKU uniqueness is PER-STORE. ---

    [Fact]
    public async Task ImportForStore_ValidRows_CreatesProductsInThatStore()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out _);   // default user = Admin, store 1
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        await db.SaveChangesAsync();

        var excel = BuildExcel(
            ("Mouse",    "SLR-001", "10.00", "Electronics", "Desc 1", "5"),
            ("Keyboard", "SLR-002", "20.00", "Electronics", "Desc 2", "3")
        );

        ImportResultDto result = await sut.ImportForStoreAsync(excel, storeId: 1);

        result.AddedCount.Should().Be(2);
        result.FailedCount.Should().Be(0);
        db.Products.Should().HaveCount(2);
        db.Products.Should().OnlyContain(p => p.StoreId == 1);
    }

    [Fact]
    public async Task ImportForStore_CallerDoesNotOwnStore_ThrowsForbiddenException()
    {
        // A non-admin seller (UserId 99) who does NOT own store 1 (its owner is user 1).
        var outsider = new Mock<ICurrentUserService>();
        outsider.Setup(c => c.IsAdmin).Returns(false);
        outsider.Setup(c => c.UserId).Returns(99);

        var sut = BuildSut(out var db, out _, out _, out _, out _, outsider.Object);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        await db.SaveChangesAsync();

        var excel = BuildExcel(("Mouse", "SLR-001", "10.00", "Electronics", "d", "1"));

        Func<Task> act = () => sut.ImportForStoreAsync(excel, storeId: 1);

        await act.Should().ThrowAsync<ForbiddenException>();
        db.Products.Should().BeEmpty();   // nothing imported when the caller is rejected
    }

    [Fact]
    public async Task ImportForStore_SameSkuInAnotherStore_IsAllowed()
    {
        // Per-store uniqueness: a SKU used in store 2 must NOT block the same SKU in store 1.
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Stores.Add(new Store { Id = 2, OwnerUserId = 2, Name = "Other", Slug = "other", Status = StoreStatus.Approved, CommissionRate = 0m });
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        db.Products.Add(new Product { Id = 1, Name = "Existing", Description = "d", Sku = "SHARED-1", Price = 5m, CategoryId = 1, StoreId = 2 });
        await db.SaveChangesAsync();

        var excel = BuildExcel(("Mouse", "SHARED-1", "10.00", "Electronics", "d", "1"));

        ImportResultDto result = await sut.ImportForStoreAsync(excel, storeId: 1);

        result.AddedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
    }

    [Fact]
    public async Task ImportForStore_SkuAlreadyInTargetStore_SkipsRow()
    {
        // A SKU already present in the TARGET store is rejected (per-store uniqueness).
        var sut = BuildSut(out var db, out _, out _, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        db.Products.Add(new Product { Id = 1, Name = "Existing", Description = "d", Sku = "DUP-1", Price = 5m, CategoryId = 1, StoreId = 1 });
        await db.SaveChangesAsync();

        var excel = BuildExcel(("Mouse", "DUP-1", "10.00", "Electronics", "d", "1"));

        ImportResultDto result = await sut.ImportForStoreAsync(excel, storeId: 1);

        result.AddedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors[0].Reason.Should().Contain("already exists");
    }

    // TB-47: happy path — an existing product yields the AI's suggestion, and the
    // product row is NOT modified (the endpoint returns a draft, it never saves).
    [Fact]
    public async Task GenerateContentAsync_ProductExists_ReturnsSuggestionAndDoesNotSave()
    {
        // Arrange — a product in the in-memory DB, plus a fake AI reply from the mock.
        var sut = BuildSut(out var db, out _, out _, out var geminiMock, out _);
        var category = new Category { Name = "Phones" };
        var product = new Product
        {
            Name = "Pixelon X",
            Description = "old text",
            Sku = "PX-1",
            Price = 999m,
            Category = category,
            StoreId = 1
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        var aiReply = new ProductContentResponse(
            "shiny new description",
            new List<string> { "OLED", "5000mAh" },
            "Pixelon X — Buy Now",
            "The Pixelon X meta description.");
        geminiMock
            .Setup(g => g.GenerateProductContentAsync(
                "Pixelon X", "Phones", "6.1in OLED, 5000mAh",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiReply);

        var request = new GenerateContentRequest("6.1in OLED, 5000mAh");

        // Act
        var result = await sut.GenerateContentAsync(product.Id, request);

        // Assert — returns the AI suggestion (check the fields, not just the reference)...
        result.Should().BeEquivalentTo(aiReply);
        result.Description.Should().Be("shiny new description");
        result.Features.Should().HaveCount(2);
        result.SeoTitle.Should().Be("Pixelon X — Buy Now");
        // ...and crucially did NOT overwrite the stored product.
        var reloaded = await db.Products.FindAsync(product.Id);
        reloaded!.Description.Should().Be("old text");
    }

    // TB-47: a missing product id must surface as a NotFoundException (-> 404).
    [Fact]
    public async Task GenerateContentAsync_ProductMissing_ThrowsNotFoundException()
    {
        // Arrange — empty DB, so id 999 cannot exist.
        var sut = BuildSut(out _, out _, out _, out _, out _);
        var request = new GenerateContentRequest("anything");

        // Act
        Func<Task> act = async () => await sut.GenerateContentAsync(999, request);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ===================== TB-156: SEMANTIC SEARCH =====================
    // The real ranking runs in Postgres via the pgvector "<=>" operator, which the in-memory EF
    // provider cannot execute. So we (a) unit-test the pure cosine helper on canned vectors, and
    // (b) test the service's input-validation and Gemini-failure mapping against the mocked client.

    [Fact]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var v = new float[] { 0.2f, 0.5f, 0.9f, 0.1f };
        ProductService.CosineSimilarity(v, v).Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var a = new float[] { 1f, 0f };
        var b = new float[] { 0f, 1f };
        ProductService.CosineSimilarity(a, b).Should().BeApproximately(0.0, 1e-9);
    }

    // "assert ranking order on canned vectors": a vector close in direction to the query must
    // score higher (more similar) than one pointing away from it.
    [Fact]
    public void CosineSimilarity_RanksCloserVectorAboveFartherVector()
    {
        var query = new float[] { 1f, 0f };
        var close = new float[] { 0.9f, 0.1f };   // small angle to the query
        var far   = new float[] { 0.1f, 0.9f };   // large angle to the query

        var simClose = ProductService.CosineSimilarity(query, close);
        var simFar   = ProductService.CosineSimilarity(query, far);

        simClose.Should().BeGreaterThan(simFar);

        // And a full ranking of [far, close] by similarity puts 'close' first.
        var ranked = new[] { far, close }
            .OrderByDescending(v => ProductService.CosineSimilarity(query, v))
            .ToList();
        ranked[0].Should().BeSameAs(close);
        ranked[1].Should().BeSameAs(far);
    }

    [Fact]
    public async Task SearchSemantic_EmptyQuery_ThrowsValidationException()
    {
        var sut = BuildSut(out _, out _, out _, out _, out _);

        Func<Task> act = async () => await sut.SearchSemanticAsync("   ", 10);

        await act.Should().ThrowAsync<Buyit.Domain.Exceptions.ValidationException>();
    }

    [Fact]
    public async Task SearchSemantic_EmbeddingServiceFails_PropagatesExternalServiceException()
    {
        var sut = BuildSut(out _, out _, out _, out _, out var embeddingMock);
        // Override the default: the embedding client is down -> 502-style failure.
        embeddingMock
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("The AI embedding service is unavailable."));

        Func<Task> act = async () => await sut.SearchSemanticAsync("coffee mug", 10);

        await act.Should().ThrowAsync<ExternalServiceException>();
    }

    // Best-effort embedding: a Gemini outage during create must be SWALLOWED so the product is
    // still created (the backfill/lazy path fills the embedding in later).
    [Fact]
    public async Task CreateProduct_EmbeddingServiceFails_StillCreatesProduct()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out var embeddingMock);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        await db.SaveChangesAsync();
        // Override the default: embedding is down.
        embeddingMock
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("The AI embedding service is unavailable."));

        var request = new CreateProductRequest
        {
            Name = "Wireless Mouse",
            Description = "Ergonomic 2.4GHz mouse",
            Sku = "MOUSE-BEE",
            Price = 24.99m,
            CategoryId = 1,
            StoreId = 1,
            InitialStock = 5
        };

        // Act — must NOT throw even though embedding failed.
        var result = await sut.CreateAsync(request);

        // Assert — the product exists and the request succeeded.
        result.Should().NotBeNull();
        var saved = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Sku == "MOUSE-BEE");
        saved.Should().NotBeNull();
    }

    // Backfill embeds every product that has no embedding yet and reports the counts.
    [Fact]
    public async Task BackfillEmbeddings_EmbedsPendingProducts_ReturnsCounts()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out var embeddingMock);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        db.Products.AddRange(
            new Product { Id = 1, Name = "A", Description = "a", Sku = "A-1", Price = 1m, CategoryId = 1, StoreId = 1 },
            new Product { Id = 2, Name = "B", Description = "b", Sku = "B-1", Price = 2m, CategoryId = 1, StoreId = 1 },
            new Product { Id = 3, Name = "C", Description = "c", Sku = "C-1", Price = 3m, CategoryId = 1, StoreId = 1 });
        await db.SaveChangesAsync();
        // Default embeddingMock already returns a valid 768-vector for every call.

        var result = await sut.BackfillEmbeddingsAsync(batchSize: 100);

        result.Embedded.Should().Be(3);
        result.Failed.Should().Be(0);
        result.Remaining.Should().Be(0);
        embeddingMock.Verify(
            e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    // TB-156 BUG: bulk-imported products must ALSO get a semantic-search embedding, exactly like
    // products created one-by-one via CreateAsync. Before the fix the import path inserted rows
    // without ever calling the embedder, so every imported product had a null Embedding and was
    // invisible to semantic search (the reported "search doesn't find newly-added products" bug).
    [Fact]
    public async Task ImportExcel_ValidRows_GeneratesEmbeddingForEachProduct()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out var embeddingMock);
        db.Categories.Add(new Category { Id = 1, Name = "Clothing" });
        await db.SaveChangesAsync();

        var excel = BuildExcel(
            ("Running Socks", "EMB-001", "12.99", "Clothing", "Breathable cushioned running socks", "5"),
            ("Wool Beanie",   "EMB-002", "9.99",  "Clothing", "Warm winter beanie", "5")
        );

        var result = await sut.ImportAsync(excel);

        result.AddedCount.Should().Be(2);
        // Every imported product must carry an embedding so semantic search can rank it.
        var saved = await db.Products.IgnoreQueryFilters().ToListAsync();
        saved.Should().HaveCount(2);
        saved.Should().OnlyContain(p => p.Embedding != null);
        // The embedder is called once per imported product.
        embeddingMock.Verify(
            e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ImportForStore_ValidRows_GeneratesEmbeddingForEachProduct()
    {
        var sut = BuildSut(out var db, out _, out _, out _, out var embeddingMock);
        db.Categories.Add(new Category { Id = 1, Name = "Clothing" });
        await db.SaveChangesAsync();

        var excel = BuildExcel(
            ("Running Socks", "SEMB-001", "12.99", "Clothing", "Breathable cushioned running socks", "5")
        );

        var result = await sut.ImportForStoreAsync(excel, storeId: 1);

        result.AddedCount.Should().Be(1);
        var saved = await db.Products.IgnoreQueryFilters().SingleAsync();
        saved.Embedding.Should().NotBeNull();
        embeddingMock.Verify(
            e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
