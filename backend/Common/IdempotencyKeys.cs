using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeLink.Common
{
    public static class IdempotencyKeys
    {
        public const string HeaderName = "Idempotency-Key";
        public const int MaxClientKeyLength = 100;
        public static readonly TimeSpan RetentionPeriod = TimeSpan.FromHours(24);

        /// <summary>Removes keys older than the cutoff (a repeat that late is a new submission).</summary>
        public static async Task DeleteOlderThanAsync(AppDbContext context, DateTime cutoff)
        {
            if (context.Database.IsRelational())
            {
                await context.IdempotencyKeys.Where(k => k.CreatedAt < cutoff).ExecuteDeleteAsync();
                return;
            }
            context.IdempotencyKeys.RemoveRange(await context.IdempotencyKeys.Where(k => k.CreatedAt < cutoff).ToListAsync());
            await context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Create actions that have no uniqueness rule of their own (adding packets, transfers, emergencies, complaints).
    /// When the client sends an Idempotency-Key header (one key per form submission, reused on a double click or retry),
    /// the key is added to the request's DbContext and saved in the same save as the records the action creates.
    /// The same key again gets 409 and nothing is created twice: a key already stored is refused here, and one saved
    /// by a parallel request at the same moment fails on the primary key (GlobalExceptionMiddleware maps it to 409).
    /// If the action fails, nothing is saved, so the same key can be used again. Without the header nothing changes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var clientKey = context.HttpContext.Request.Headers[IdempotencyKeys.HeaderName].ToString().Trim();
            if (clientKey.Length == 0)
            {
                await next();
                return;
            }

            if (clientKey.Length > IdempotencyKeys.MaxClientKeyLength)
            {
                context.Result = new BadRequestObjectResult(new { message = "The Idempotency-Key header is too long." });
                return;
            }

            // Keys are per account, so two users can never collide
            var userId = Guid.TryParse(context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
            var key = $"{userId?.ToString() ?? "anonymous"}:{clientKey}";

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            if (await db.IdempotencyKeys.AnyAsync(k => k.Key == key))
            {
                context.Result = new ObjectResult(new { message = DatabaseConflicts.AlreadySubmittedMessage }) { StatusCode = 409 };
                return;
            }

            var endpoint = $"{context.HttpContext.Request.Method} {context.ActionDescriptor.AttributeRouteInfo?.Template}";
            db.IdempotencyKeys.Add(new IdempotencyKey
            {
                Key = key,
                UserId = userId,
                Endpoint = endpoint.Length > 100 ? endpoint[..100] : endpoint,
                CreatedAt = DateTime.UtcNow
            });

            await next();
        }
    }
}
