using System;
using System.Text;
using LifeLink.Data;
using LifeLink.Middleware;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using LifeLink.Services.Inventory;
using LifeLink.Services.Emergency;
using LifeLink.Services.Transfer;
using LifeLink.Services.Hospitals;
using LifeLink.Services.Doctors;
using LifeLink.Services.Verification;
using LifeLink.Services.Matching;
using LifeLink.Services.Notification;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Admin;
using LifeLink.Services.Complaints;
using LifeLink.Services.HospitalActivity;
using LifeLink.Services.Appeals;
using LifeLink.Services.Planning;
using LifeLink.Services.Assistant;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure PostgreSQL / Neon Database Context
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Development-only: log which config source supplied DefaultConnection (password masked as length + hash prefix)
if (builder.Environment.IsDevelopment())
{
    var winningProvider = ((IConfigurationRoot)builder.Configuration).Providers.Reverse()
        .FirstOrDefault(p => p.TryGet("ConnectionStrings:DefaultConnection", out _));
    var connectionSource = winningProvider is Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider json
        ? json.Source.FileProvider?.GetFileInfo(json.Source.Path!).PhysicalPath
        : winningProvider?.ToString();
    var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
    var passwordHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(csb.Password ?? ""))).ToLowerInvariant()[..8];
    Console.WriteLine($"[DB] Environment={builder.Environment.EnvironmentName}; ContentRoot={builder.Environment.ContentRootPath}");
    Console.WriteLine($"[DB] DefaultConnection from: {connectionSource ?? "(not set)"}");
    Console.WriteLine($"[DB] Host={csb.Host}; Database={csb.Database}; Username={csb.Username}; PasswordLength={csb.Password?.Length ?? 0}; PasswordSha256={passwordHash}");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }));

// 2. Configure HttpContextAccessor & Services Injection
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<IEmailService, MailKitEmailService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<RoleAttentionService>();

// Idle timeout: server-side sessions (Session:IdleTimeoutMinutes, Session:WarningMinutes)
builder.Services.AddSingleton(SessionSettings.FromConfiguration(builder.Configuration));
builder.Services.AddScoped<ISessionService, SessionService>();

// Student 3 Services Injection
builder.Services.AddScoped<IBloodInventoryService, BloodInventoryService>();
builder.Services.AddScoped<IEmergencyRequestService, EmergencyRequestService>();
builder.Services.AddScoped<ITransferRequestService, TransferRequestService>();
builder.Services.AddScoped<InventoryMonitor>();
builder.Services.AddScoped<InventoryAnalysisService>();

// Student 2 Services Injection
builder.Services.AddHttpClient<INotificationAgentService, NotificationAgentService>();
builder.Services.AddScoped<IHospitalService, HospitalService>();
builder.Services.AddScoped<IDoctorService, DoctorService>();
builder.Services.AddScoped<INotificationAgentService, NotificationAgentService>();
builder.Services.AddScoped<IVerificationService, VerificationService>();
builder.Services.AddScoped<IMatchingService, MatchingService>();

// Student 1 Services Injection
builder.Services.AddScoped<IBloodCompatibilityService, BloodCompatibilityService>();
builder.Services.AddScoped<IBloodRequestService, BloodRequestService>();
builder.Services.AddHttpClient<IScreeningAgentClient, ScreeningAgentClient>(client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<IAcceptanceService, AcceptanceService>();
builder.Services.AddScoped<IRequestExpiryService, RequestExpiryService>();
builder.Services.AddHostedService<RequestExpiryBackgroundService>();

// Student 4 Services Injection (Admin Governance, Compliance & Planning Agent)
builder.Services.AddHttpClient<IPlanningAgentService, PlanningAgentService>();
builder.Services.AddScoped<IAdminNotificationService, AdminNotificationService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IAdminOversightActionsService, AdminOversightActionsService>();
builder.Services.AddScoped<IComplaintService, ComplaintService>();
builder.Services.AddScoped<IHospitalActivityService, HospitalActivityService>();
builder.Services.AddScoped<IAppealService, AppealService>();

// Universal assistant (Supervisor agent): relays chat with a role-scoped snapshot; limited per user
builder.Services.AddHttpClient<IAssistantService, AssistantService>(client => client.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("assistant", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("otp-verification", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// 3. Configure JWT Authentication & Authorization
var jwtSettings = builder.Configuration.GetSection("Jwt");
var keyString = jwtSettings["Key"] ?? throw new InvalidOperationException("Jwt:Key is missing from configuration.");
var key = Encoding.UTF8.GetBytes(keyString);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // set to true in production
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "LifeLinkAPI",
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"] ?? "LifeLinkApp",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
    options.Events = new JwtBearerEvents
    {
        // End sessions that no longer match the account: a former Admin after an ownership transfer,
        // or a permanently blocked / deleted account. (The frontend signs out on 401.)
        OnTokenValidated = async ctx =>
        {
            var sub = ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? ctx.Principal?.FindFirst("sub")?.Value;
            if (!Guid.TryParse(sub, out var userId)) return;

            var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var account = await db.Users.AsNoTracking()
                .Where(u => u.UserId == userId)
                .Select(u => new { u.AccountStatus, IsAdmin = u.UserRoles.Any(ur => ur.Role.Name == "Admin") })
                .FirstOrDefaultAsync();

            if (account != null && (account.AccountStatus == LifeLink.Entities.AccountStatus.Blocked || account.AccountStatus == LifeLink.Entities.AccountStatus.Deleted))
            {
                ctx.Fail("This account is no longer active.");
                return;
            }
            if (ctx.Principal!.IsInRole("Admin") && account?.IsAdmin != true)
            {
                ctx.Fail("Admin ownership has changed. Please sign in again.");
                return;
            }

            // Idle timeout and sign-out: the token's session must still be open and active within the idle window.
            // Only requests the app marks as user activity (never background polling) move the session on; the
            // heartbeat endpoint always does. Tokens without a session (issued before sessions existed) are refused.
            var sessions = ctx.HttpContext.RequestServices.GetRequiredService<ISessionService>();
            if (!Guid.TryParse(ctx.Principal.FindFirst(SessionSettings.SessionClaim)?.Value, out var sessionId))
            {
                ctx.HttpContext.Response.Headers[SessionSettings.EndedHeader] = "signed-out";
                ctx.Fail("Please sign in again.");
                return;
            }
            var request = ctx.HttpContext.Request;
            var recordActivity = request.Headers[SessionSettings.ActivityHeader] == "1" ||
                                 (HttpMethods.IsPost(request.Method) && request.Path.Equals("/api/Auth/activity", StringComparison.OrdinalIgnoreCase));
            var state = await sessions.CheckAsync(sessionId, userId, recordActivity);
            if (state != SessionState.Active)
            {
                ctx.HttpContext.Response.Headers[SessionSettings.EndedHeader] = state == SessionState.Idle ? "idle" : "signed-out";
                ctx.Fail(state == SessionState.Idle ? "You were signed out after a period of inactivity." : "This session has ended. Please sign in again.");
            }
        }
    };
});

builder.Services.AddAuthorization();

// 4. Configure Controllers & CORS
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        // Allowed origins from config (Cors:AllowedOrigins, e.g. env Cors__AllowedOrigins__0); local dev origins otherwise
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        if (allowedOrigins == null || allowedOrigins.Length == 0)
        {
            allowedOrigins = new[] { "http://localhost:5173", "http://localhost:3000", "http://127.0.0.1:5173" };
        }
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// 5. Configure Swagger / OpenAPI with JWT Authorization Support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "LifeLink Core Authentication API",
        Version = "v1",
        Description = "Core Authentication and Authorization Foundation API for LifeLink Platform."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. \r\n\r\n Enter 'Bearer' [space] and then your token in the text input below.\r\n\r\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...\""
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Automatically apply any pending EF Core database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();

    // Development-only demo stock: dotnet run -- --seed-demo-data
    if (args.Contains("--seed-demo-data"))
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException("--seed-demo-data is only allowed in the Development environment.");
        }
        var seeded = await DevelopmentDataSeeder.SeedDemoStockAsync(dbContext);
        Console.WriteLine($"Seeded {seeded} demo blood packets.");
        return;
    }
}

// 6. Request Pipeline Configuration
app.UseMiddleware<GlobalExceptionMiddleware>();

// Swagger: always in Development; elsewhere when Swagger:Enabled is true (env Swagger__Enabled)
if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LifeLink Core Auth API v1");
    });
}

app.UseHttpsRedirection();

app.UseCors("AllowFrontend");

app.UseMiddleware<InternalServiceAuthMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<RestrictedGovernanceModeMiddleware>();
app.UseRateLimiter();

app.MapControllers();

// Health check for the hosting platform: 200 when the database is reachable, 503 otherwise (no login needed)
app.MapGet("/health", async (AppDbContext db) =>
        await db.Database.CanConnectAsync()
            ? Results.Ok(new { status = "Healthy" })
            : Results.Json(new { status = "Unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();

app.Run();
