using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.Profiles;
using LifeLink.Entities;
using LifeLink.Middleware;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LifeLink.Tests;

public class SecurityPrivacyCorrectionsTests
{
    private sealed class Viewer : ICurrentUserService
    {
        public Guid? UserId { get; init; }
        public string? Email { get; init; }
        public IEnumerable<string> Roles { get; init; } = Array.Empty<string>();
        public bool IsAuthenticated => UserId.HasValue;
    }

    private static AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static ProfilesController Profiles(AppDbContext db, Guid viewerId, params string[] roles) =>
        new(db, new Viewer { UserId = viewerId, Roles = roles }, null!);

    [Fact]
    public async Task Contact_Privacy_Is_Enforced_Server_Side_While_Owner_And_Admin_See_Values()
    {
        await using var db = NewContext();
        var owner = new User { UserId = Guid.NewGuid(), FirstName = "Private", LastName = "User", Email = "private@example.test", PhoneNumber = "0771234567", Address = "Private address", IsEmailPublic = true };
        db.Users.Add(owner);
        db.UserRoles.Add(new UserRole { UserId = owner.UserId, RoleId = 1 });
        await db.SaveChangesAsync();

        var stranger = Assert.IsType<UserProfileDto>(Assert.IsType<OkObjectResult>(await Profiles(db, Guid.NewGuid(), "User").GetUserProfile(owner.UserId)).Value);
        Assert.Equal(owner.Email, stranger.Email);
        Assert.Null(stranger.PhoneNumber);
        Assert.Null(stranger.Address);
        Assert.Equal(owner.FirstName, stranger.FirstName);

        var own = Assert.IsType<UserProfileDto>(Assert.IsType<OkObjectResult>(await Profiles(db, owner.UserId, "User").GetUserProfile(owner.UserId)).Value);
        Assert.Equal(owner.PhoneNumber, own.PhoneNumber);
        Assert.Equal(owner.Address, own.Address);

        var admin = Assert.IsType<UserProfileDto>(Assert.IsType<OkObjectResult>(await Profiles(db, Guid.NewGuid(), "Admin").GetUserProfile(owner.UserId)).Value);
        Assert.Equal(owner.PhoneNumber, admin.PhoneNumber);
        Assert.Equal(owner.Address, admin.Address);
    }

    [Fact]
    public async Task Owner_Can_Update_Independent_Visibility_But_Other_User_Cannot()
    {
        await using var db = NewContext();
        var owner = new User { UserId = Guid.NewGuid(), FirstName = "Owner", LastName = "User", Email = "owner@example.test", PhoneNumber = "0771234567", Address = "Address" };
        db.Users.Add(owner);
        db.UserRoles.Add(new UserRole { UserId = owner.UserId, RoleId = 1 });
        await db.SaveChangesAsync();
        var dto = new UpdateUserProfileDto { FirstName = owner.FirstName, LastName = owner.LastName, PhoneNumber = owner.PhoneNumber, Address = owner.Address, IsPhonePublic = true };

        Assert.IsType<OkObjectResult>(await Profiles(db, owner.UserId, "User").UpdateUserProfile(owner.UserId, dto));
        Assert.True((await db.Users.FindAsync(owner.UserId))!.IsPhonePublic);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await Profiles(db, Guid.NewGuid(), "User").UpdateUserProfile(owner.UserId, dto)).StatusCode);
    }

    [Fact]
    public async Task Otp_Is_Invalidated_After_Five_Wrong_Attempts()
    {
        await using var db = NewContext();
        var user = new User { UserId = Guid.NewGuid(), Email = "otp@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var service = new PasswordResetService(db);
        var correct = await service.CreatePasswordResetOtpAsync(user);
        for (var i = 0; i < 5; i++) Assert.Null(await service.VerifyOtpAsync(user, "000000"));
        var token = await db.PasswordResetTokens.SingleAsync();
        Assert.Equal(5, token.FailedAttempts);
        Assert.NotNull(token.UsedAt);
        Assert.Null(await service.VerifyOtpAsync(user, correct));
    }

    [Theory]
    [InlineData(null, "presented")]
    [InlineData("", "presented")]
    [InlineData("LifeLink-Internal-Agent-Key-2026", "LifeLink-Internal-Agent-Key-2026")]
    [InlineData("configured-key", "incorrect-key")]
    public async Task Invalid_Internal_Key_Never_Grants_InternalAgent(string? configured, string presented)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalService:ApiKey"] = configured }).Build();
        var context = new DefaultHttpContext();
        context.Request.Headers[InternalServiceAuthMiddleware.HeaderName] = presented;
        var middleware = new InternalServiceAuthMiddleware(_ => Task.CompletedTask, configuration, NullLogger<InternalServiceAuthMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.False(context.User.IsInRole(InternalServiceAuthMiddleware.RoleName));
    }

    [Fact]
    public async Task Explicit_Internal_Key_Grants_Only_InternalAgent()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["InternalService:ApiKey"] = "test-only-internal-key" }).Build();
        var context = new DefaultHttpContext();
        context.Request.Headers[InternalServiceAuthMiddleware.HeaderName] = "test-only-internal-key";
        var middleware = new InternalServiceAuthMiddleware(_ => Task.CompletedTask, configuration, NullLogger<InternalServiceAuthMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.True(context.User.IsInRole(InternalServiceAuthMiddleware.RoleName));
        Assert.False(context.User.IsInRole("Admin"));
    }

    [Fact]
    public void Matching_Http_Surface_Is_Admin_Read_Only()
    {
        var authorize = Assert.Single(typeof(MatchingController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal("Admin", authorize.Roles);
        Assert.DoesNotContain(typeof(MatchingController).GetMethods(), method =>
            method.GetCustomAttributes(typeof(HttpPostAttribute), true).Any());
    }

    [Fact]
    public void Active_Donor_Database_Invariant_Includes_All_And_Only_Active_Donor_States()
    {
        using var db = NewContext();
        var index = db.Model.FindEntityType(typeof(Acceptance))!.GetIndexes()
            .Single(i => i.GetDatabaseName() == "IX_Acceptances_OneActiveDonorProcess");
        Assert.True(index.IsUnique);
        var filter = index.GetFilter();
        Assert.Contains("Accepted", filter);
        Assert.Contains("ScreeningPending", filter);
        Assert.Contains("ScreeningCompleted", filter);
        Assert.Contains("Verified", filter);
        Assert.Contains("DonorHospitalId", filter);
        Assert.DoesNotContain("Matched", filter);
        Assert.DoesNotContain("Rejected", filter);
        Assert.DoesNotContain("Cancelled", filter);
    }
}
