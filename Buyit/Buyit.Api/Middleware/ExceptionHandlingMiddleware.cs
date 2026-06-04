using System.Net;
using System.Text.Json;
using Buyit.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Buyit.Api.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex);
            }
        }

        private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            var statusCode = exception switch
            {
                ValidationException => HttpStatusCode.BadRequest,      // this is for 400
                UnauthorizedException => HttpStatusCode.Unauthorized,  // this is for 401
                ForbiddenException => HttpStatusCode.Forbidden,        // this is for 403
                NotFoundException => HttpStatusCode.NotFound,          // this is for 404
                ConflictException => HttpStatusCode.Conflict,          // this is for 409
                _ => HttpStatusCode.InternalServerError                // this is for 500
            };

            context.Response.StatusCode = (int)statusCode;

            var problemDetails = new ProblemDetails
            {
                Status = (int)statusCode,
                Title = statusCode.ToString(),
                Detail = statusCode == HttpStatusCode.InternalServerError
                    ? "An unexpected error occurred on the server." 
                    : exception.Message, 
                Instance = context.Request.Path
            };

            if (exception is ValidationException validationException)
            {
                problemDetails.Extensions.Add("errors", validationException.Errors);
            }

            var result = JsonSerializer.Serialize(problemDetails);
            await context.Response.WriteAsync(result);
        }
    }
}
