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
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/problem+json";

            var statusCode = exception switch
            {
                ValidationException => HttpStatusCode.BadRequest,      // this is for 400
                UnauthorizedException => HttpStatusCode.Unauthorized,  // this is for 401
                ForbiddenException => HttpStatusCode.Forbidden,        // this is for 403
                NotFoundException => HttpStatusCode.NotFound,          // this is for 404
                ConflictException => HttpStatusCode.Conflict,          // this is for 409
                SftpConnectionException => HttpStatusCode.BadGateway,  // this is for 502
                SftpFileNotFoundException => HttpStatusCode.NotFound,  // this is for 404
                _ => HttpStatusCode.InternalServerError                // this is for 500
            };

            context.Response.StatusCode = (int)statusCode;

            if (statusCode == HttpStatusCode.InternalServerError)
                _logger.LogError(exception, "Unhandled exception on {Path}", context.Request.Path);
            else
                _logger.LogWarning("Handled {StatusCode} on {Path}: {Message}", (int)statusCode, context.Request.Path, exception.Message);

            var problemDetails = new ProblemDetails
            {
                Status = (int)statusCode,
                Title = statusCode switch
                {
                    HttpStatusCode.BadRequest => "Bad Request",
                    HttpStatusCode.Unauthorized => "Unauthorized",
                    HttpStatusCode.Forbidden => "Forbidden",
                    HttpStatusCode.NotFound => "Not Found",
                    HttpStatusCode.Conflict => "Conflict",
                    _ => "Internal Server Error"
                },
                Detail = statusCode == HttpStatusCode.InternalServerError
                    ? "An unexpected error occurred on the server." 
                    : exception.Message, 
                Instance = context.Request.Path
            };

            if (exception is ValidationException validationException)
            {
                problemDetails.Extensions["errors"] = validationException.Errors;
            }

            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var result = JsonSerializer.Serialize(problemDetails, options);
            await context.Response.WriteAsync(result);
        }
    }
}
