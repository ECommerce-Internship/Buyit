using Buyit.Application.Interfaces;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Buyit.MCP.Tools;

[McpServerToolType]
public class DocumentationTools
{
    private readonly IDocumentationService _documentation;

    public DocumentationTools(IDocumentationService documentation)
    {
        _documentation = documentation;
    }

    // RAG retrieval tool. It does NOT answer — it returns the most relevant passages of Buyit's own
    // feature documentation so the chat model can ground its answer in them. Same shape as
    // search_products: a retriever the model calls, then composes the reply from the result.
    [McpServerTool, Description(
        "Look up Buyit's own feature documentation to answer questions about HOW BUYIT WORKS — e.g. how " +
        "checkout, coupons, returns, semantic search, seller sign-up, or the assistant work. Returns the " +
        "most relevant documentation passages (with their source). Base your answer only on the passages " +
        "returned; if they don't cover the question, say you don't have that information. Use this instead " +
        "of search_products, which is for finding products to buy, not explaining features.")]
    public async Task<string> search_documentation(
        [Description("The user's question about how Buyit or one of its features works.")] string question)
    {
        var results = await _documentation.SearchAsync(question);
        return JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
    }
}
