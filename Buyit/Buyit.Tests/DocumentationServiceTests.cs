using Xunit;
using Moq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Buyit.Application.Common;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Exceptions;
using Buyit.Infrastructure.Data;
using Buyit.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Buyit.Tests;

public class DocumentationServiceTests
{
    // Builds a DocumentationService over a fresh in-memory DB and a mocked embedder. The embedder
    // returns a valid 768-length vector by default so ingest succeeds silently; tests that care
    // override it via the out param. NOTE: SearchAsync ranks with pgvector's CosineDistance, which the
    // in-memory provider cannot execute — so, exactly like ProductServiceTests, we cover the guard and
    // failure paths here and leave the real ranking to manual Postgres verification.
    private static DocumentationService BuildSut(
        out AppDbContext db,
        out Mock<IEmbeddingService> embeddingMock,
        string? docsPath = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        db = new AppDbContext(options);

        embeddingMock = new Mock<IEmbeddingService>();
        embeddingMock
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<EmbeddingTaskType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[768]);

        var settings = Options.Create(new DocumentationSettings { DocsPath = docsPath ?? "Documentation" });
        var loggerMock = new Mock<ILogger<DocumentationService>>();

        return new DocumentationService(db, embeddingMock.Object, settings, loggerMock.Object);
    }

    // Creates a throwaway docs folder with the given files and returns its path.
    private static string CreateDocsFolder(params (string Name, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), "buyit-docs-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        foreach (var (name, content) in files)
            File.WriteAllText(Path.Combine(dir, name), content);
        return dir;
    }

    [Fact]
    public async Task IngestAsync_ReadsFilesAndEmbedsChunks_ReturnsCounts()
    {
        // Arrange — one file with an intro plus two "##" sections => three chunks.
        var dir = CreateDocsFolder(("orders.md",
            "# Orders\nIntro paragraph.\n\n## Checkout\nHow checkout works.\n\n## Tracking\nHow tracking works."));
        var sut = BuildSut(out var db, out var embeddingMock, dir);

        // Act
        var result = await sut.IngestAsync();

        // Assert — counts reported and chunks persisted with embeddings, each embedded as a DOCUMENT.
        result.Files.Should().Be(1);
        result.Chunks.Should().Be(3);
        result.Embedded.Should().Be(3);

        var stored = await db.DocChunks.ToListAsync();
        stored.Should().HaveCount(3);
        stored.Should().OnlyContain(c => c.Source == "orders.md" && c.Embedding != null);
        embeddingMock.Verify(
            e => e.EmbedAsync(It.IsAny<string>(), EmbeddingTaskType.RetrievalDocument, It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task IngestAsync_RerunRebuildsCorpus_NoDuplicateChunks()
    {
        // Arrange
        var dir = CreateDocsFolder(("cart.md", "# Cart\n## Adding\nAdd items to the cart."));
        var sut = BuildSut(out var db, out _, dir);

        // Act — ingest twice; the second run must replace, not append.
        await sut.IngestAsync();
        var second = await sut.IngestAsync();

        // Assert — still exactly the corpus on disk, no duplication.
        second.Chunks.Should().Be(1);
        (await db.DocChunks.CountAsync()).Should().Be(1);

        Directory.Delete(dir, true);
    }

    [Fact]
    public async Task IngestAsync_MissingFolder_ThrowsNotFoundException()
    {
        var missing = Path.Combine(Path.GetTempPath(), "buyit-docs-tests", Guid.NewGuid().ToString());
        var sut = BuildSut(out _, out _, missing);

        Func<Task> act = async () => await sut.IngestAsync();

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ThrowsValidationException()
    {
        var sut = BuildSut(out _, out _);

        Func<Task> act = async () => await sut.SearchAsync("   ");

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task SearchAsync_EmbeddingServiceFails_PropagatesExternalServiceException()
    {
        var sut = BuildSut(out _, out var embeddingMock);
        embeddingMock
            .Setup(e => e.EmbedAsync(It.IsAny<string>(), It.IsAny<EmbeddingTaskType>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ExternalServiceException("boom"));

        Func<Task> act = async () => await sut.SearchAsync("how does checkout work");

        await act.Should().ThrowAsync<ExternalServiceException>();
    }
}
