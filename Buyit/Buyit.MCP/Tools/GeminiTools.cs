using Buyit.Application.Interfaces;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace Buyit.MCP.Tools;

[McpServerToolType]
public class GeminiTools
{
    private readonly IGeminiService _geminiService;

    public GeminiTools(IGeminiService geminiService)
    {
        _geminiService = geminiService;
    }

    [McpServerTool, Description("Generate AI-powered marketing content for a product using Google Gemini.")]
    public async Task<string> generate_product_content(
        [Description("The product name")] string productName,
        [Description("The product category")] string category,
        [Description("Product specifications and features")] string specs)
    {
        var result = await _geminiService.GenerateProductContentAsync(productName, category, specs);
        return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
    }
}