using Asp.Versioning;
using Buyit.Application.Interfaces;
using Buyit.Domain.Entities;
using Buyit.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Buyit.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class TokenTestController : ControllerBase
    {
        private readonly IJwtTokenService _jwtTokenService;

        public TokenTestController(IJwtTokenService jwtTokenService)
        {
            _jwtTokenService = jwtTokenService;
        }

        // TEMPORARY (TB-26): generates a token to verify. REMOVE before merging.
        // Pass ?role=Admin (default) or ?role=Customer to mint different tokens.
        [HttpGet("generate")]
        public IActionResult Generate([FromQuery] UserRole role = UserRole.Admin)
        {
            var fakeUser = new User
            {
                Id = 1,
                Email = "test@buyit.com",
                Role = role
            };

            var token = _jwtTokenService.GenerateAccessToken(fakeUser);
            return Ok(new { token });
        }

        // TEMPORARY (TB-26 verification): proves [Authorize(Roles=...)] reads the role claim.
        // REMOVE before merging.
        [Authorize(Roles = "Admin")]
        [HttpGet("admin-only")]
        public IActionResult AdminOnly() => Ok("admin reached");
    }
}