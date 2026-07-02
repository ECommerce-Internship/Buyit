using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Buyit.Application.DTOs;
using Buyit.Application.Interfaces;

namespace Buyit.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize]                    // authenticated users only — the MCP tools expose store/order data
[EnableRateLimiting("chat")]   // throttle the expensive per-message MCP + Gemini round-trips
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatController(IChatService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Ask the Buyit AI assistant a question. It uses live store data via the MCP tools.
    /// </summary>
    // POST api/v1/chat
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request)
    {
        var result = await _chatService.SendMessageAsync(request, HttpContext.RequestAborted);

        return Ok(result);
    }
}