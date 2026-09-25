using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace LifeLink.Middleware
{
    public class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
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
                _logger.LogError(ex, "An unhandled exception occurred during request execution.");
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            int statusCode;
            string message;

            switch (exception)
            {
                case Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException:
                    statusCode = (int)HttpStatusCode.Conflict;
                    message = "This record was changed by someone else at the same time. Please refresh and try again.";
                    break;
                case UnauthorizedAccessException uae:
                    statusCode = (int)HttpStatusCode.Unauthorized;
                    message = uae.Message;
                    break;
                case KeyNotFoundException knfe:
                    statusCode = (int)HttpStatusCode.NotFound;
                    message = knfe.Message;
                    break;
                case LifeLink.Common.ConflictException ce:
                    statusCode = (int)HttpStatusCode.Conflict;
                    message = ce.Message;
                    break;
                case InvalidOperationException ioe:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    message = ioe.Message;
                    break;
                default:
                    statusCode = (int)HttpStatusCode.InternalServerError;
                    message = "An unexpected error occurred. Please try again later.";
                    break;
            }

            context.Response.StatusCode = statusCode;

            var response = new
            {
                status = statusCode,
                message = message,
                errors = (IDictionary<string, string[]>?)null
            };

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return context.Response.WriteAsync(JsonSerializer.Serialize(response, options));
        }
    }
}
