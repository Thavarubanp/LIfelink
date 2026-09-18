using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Middleware
{
    public class RestrictedGovernanceModeMiddleware
    {
        private readonly RequestDelegate _next;

        public RestrictedGovernanceModeMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, AppDbContext dbContext)
        {
            // 1. If not authenticated, proceed (public endpoints or anonymous authentication flows)
            if (context.User?.Identity?.IsAuthenticated != true)
            {
                await _next(context);
                return;
            }

            // 2. Admins are exempt from restricted governance mode
            if (context.User.IsInRole("Admin"))
            {
                await _next(context);
                return;
            }

            // 3. Inspect endpoint metadata for [AllowSuspendedAccess] attribute (no hardcoded route strings)
            var endpoint = context.GetEndpoint();
            var allowSuspended = endpoint?.Metadata.GetMetadata<AllowSuspendedAccessAttribute>() != null;

            if (allowSuspended)
            {
                await _next(context);
                return;
            }

            // 4. Retrieve authenticated UserId from claims
            var subClaim = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value;

            if (Guid.TryParse(subClaim, out var userId))
            {
                var user = await dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
                if (user != null && user.IsSuspended)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";

                    var response = ApiResponse<object>.Fail("Account is suspended. Only governance and appeal actions are allowed in Restricted Governance Mode.");
                    await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));
                    return;
                }

                // Check if user is a doctor or staff affiliated with a suspended hospital
                var doctor = await dbContext.Doctors.Include(d => d.Hospital).AsNoTracking().FirstOrDefaultAsync(d => d.UserId == userId);
                if (doctor?.Hospital != null && doctor.Hospital.IsSuspended)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";

                    var response = ApiResponse<object>.Fail("Account is suspended. Only governance and appeal actions are allowed in Restricted Governance Mode.");
                    await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }));
                    return;
                }

                // Check if user email corresponds to a suspended hospital
                if (user != null && !string.IsNullOrEmpty(user.Email))
                {
                    var hospital = await dbContext.Hospitals.AsNoTracking().FirstOrDefaultAsync(h => h.Email.ToLower() == user.Email.ToLower());
                    if (hospital != null && hospital.IsSuspended)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "application/json";

                        var response = ApiResponse<object>.Fail("Account is suspended. Only governance and appeal actions are allowed in Restricted Governance Mode.");
                        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                        }));
                        return;
                    }
                }
            }

            await _next(context);
        }
    }
}
