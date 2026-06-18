using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using FluentValidation;
using FluentValidation.Results;
using OfficeOpenXml;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using Microsoft.Extensions.Logging;

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
        out Mock<IValidator<UpdateProductRequest>> updateValidatorMock)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        db = new AppDbContext(options);

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

        return new ProductService(
            db,
            createValidatorMock.Object,
            updateValidatorMock.Object,
            cacheMock.Object,
            blobMock.Object,
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
        var sut = BuildSut(out var db, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        await db.SaveChangesAsync();

        var request = new CreateProductRequest
        {
            Name = "Wireless Mouse",
            Description = "Ergonomic 2.4GHz mouse",
            Sku = "MOUSE-001",
            Price = 24.99m,
            CategoryId = 1,
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
        var sut = BuildSut(out var db, out _, out _);
        db.Categories.Add(new Category { Id = 1, Name = "Electronics" });
        db.Products.Add(new Product
        {
            Id = 100,
            Name = "Existing Mouse",
            Description = "Already in catalogue",
            Sku = "MOUSE-001",
            Price = 19.99m,
            CategoryId = 1
        });
        await db.SaveChangesAsync();

        var request = new CreateProductRequest
        {
            Name = "Another Mouse",
            Description = "Different product, same SKU",
            Sku = "MOUSE-001",
            Price = 29.99m,
            CategoryId = 1,
            InitialStock = 10
        };

        Func<Task> act = async () => await sut.CreateAsync(request);

        await act.Should().ThrowAsync<ConflictException>()
                 .WithMessage("*already exists*");
    }

    [Fact]
    public async Task GetProductById_ExistingId_ReturnsProductResponse()
    {
        var sut = BuildSut(out var db, out _, out _);
        db.Categories.Add(new Category { Id = 7, Name = "Audio" });
        db.Products.Add(new Product
        {
            Id = 42,
            Name = "Studio Headphones",
            Description = "Over-ear, 32 ohm",
            Sku = "HEAD-042",
            Price = 149.50m,
            CategoryId = 7,
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
        var sut = BuildSut(out _, out _, out _);

        Func<Task> act = async () => await sut.GetByIdAsync(999);

        await act.Should().ThrowAsync<NotFoundException>()
                 .WithMessage("*was not found*");
    }

    [Fact]
    public async Task DeleteProduct_ExistingProduct_SetsIsDeletedTrue()
    {
        var sut = BuildSut(out var db, out _, out _);
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
        var sut = BuildSut(out var db, out _, out _);
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
        var sut = BuildSut(out var db, out _, out _);
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
}
