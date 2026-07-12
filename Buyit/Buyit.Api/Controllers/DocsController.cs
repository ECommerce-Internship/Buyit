using Asp.Versioning;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class DocsController : ControllerBase
{
    private readonly IDocumentationService _documentation;

    public DocsController(IDocumentationService documentation)
    {
        _documentation = documentation;
    }

    /// <summary>
    /// (Re)build the RAG documentation corpus from the bundled feature Markdown files — reads them,
    /// chunks, embeds, and stores the vectors that the chat assistant retrieves from. Admin only.
    /// Idempotent: it rebuilds the whole corpus each run, so it is safe to re-run (e.g. after the docs
    /// change). If some chunks fail to embed (rate limits), re-run to finish them.
    /// </summary>
    [HttpPost("ingest")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IngestDocsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IngestDocsResponse>> Ingest()
    {
        var result = await _documentation.IngestAsync(HttpContext.RequestAborted);
        return Ok(result);
    }
}
