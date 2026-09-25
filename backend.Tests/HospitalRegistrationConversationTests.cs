using System.ComponentModel.DataAnnotations;
using System.Reflection;
using LifeLink.Common;
using LifeLink.Controllers;
using LifeLink.Data;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.Appeals;
using LifeLink.DTOs.Complaints;
using LifeLink.DTOs.Hospitals;
using LifeLink.DTOs.Profiles;
using LifeLink.Entities;
using LifeLink.Services.Admin;
using LifeLink.Services.Appeals;
using LifeLink.Services.Auth;
using LifeLink.Services.Common;
using LifeLink.Services.Complaints;
using LifeLink.Services.Hospitals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>
    /// Hospital registration as one continuous conversation (no Resubmitted status, no versions), the empty-file rule,
    /// admin identity removed from users' copies, and the sign-in requirements on hospital and appeal endpoints.
    /// </summary>
    public class HospitalRegistrationConversationTests
    {
        private const int UserRoleId = 1, AdminRoleId = 4; // seeded by AppDbContext.HasData
        private const string Pdf = "data:application/pdf;base64,JVBERi0xLjQ=";
        private const string EmptyPdf = "data:application/pdf;base64,";

        private sealed class Seed
        {
            public AppDbContext Db = null!;
            public AdminNotificationService Notify = null!;
            public AdminService Admin = null!;
            public HospitalService Hospitals = null!;
            public Guid AdminId;
        }

        private static async Task<Seed> SeedAsync()
        {
            var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            db.Database.EnsureCreated();
            var adminId = Guid.NewGuid();
            await db.Users.AddAsync(new User { UserId = adminId, FirstName = "A", LastName = "D", Email = "admin@lifelink.org" });
            await db.UserRoles.AddAsync(new UserRole { UserId = adminId, RoleId = AdminRoleId });
            await db.SaveChangesAsync();
            var notify = new AdminNotificationService(db, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance);
            return new Seed { Db = db, Notify = notify, Admin = new AdminService(db, notify), Hospitals = new HospitalService(db, null, notify), AdminId = adminId };
        }

        private static CreateHospitalDto Registration(string email = "city@h.org") => new()
        {
            Name = "City Hospital", Email = email, LicenseNumber = $"LIC-{email}", Address = "1 Main Street",
            ContactNumber = "0112345678", ContactPersonName = "Dr. Silva", ContactPersonPhone = "0771234567"
        };

        private static async Task<Guid> RejectedHospitalAsync(Seed s, string email = "city@h.org")
        {
            var created = await s.Hospitals.CreateHospitalAsync(Registration(email));
            await s.Admin.RejectHospitalAsync(created.HospitalId, s.AdminId, new RejectHospitalDto { Reason = "License expired." });
            return created.HospitalId;
        }

        private static HospitalRegistrationReplyDto Reply(string message = "Updated as requested.") => new() { Message = message };

        // ---------- Workflow rules ----------

        [Fact]
        public async Task Registration_Starts_The_Conversation_And_Reject_Is_Only_Possible_While_Pending()
        {
            var s = await SeedAsync();
            var created = await s.Hospitals.CreateHospitalAsync(Registration());
            Assert.Equal("Submitted", created.ApprovalHistory.Single().Type);

            await s.Admin.RejectHospitalAsync(created.HospitalId, s.AdminId, new RejectHospitalDto { Reason = "License expired." });
            var again = await Assert.ThrowsAsync<ConflictException>(() =>
                s.Admin.RejectHospitalAsync(created.HospitalId, s.AdminId, new RejectHospitalDto { Reason = "Still expired." }));
            Assert.Contains("already rejected", again.Message);

            await s.Admin.ApproveHospitalAsync(created.HospitalId, s.AdminId);
            await Assert.ThrowsAsync<ConflictException>(() =>
                s.Admin.RejectHospitalAsync(created.HospitalId, s.AdminId, new RejectHospitalDto { Reason = "Too late." }));
        }

        [Fact]
        public async Task Comments_Are_Only_For_Rejected_Registrations_And_Keep_The_Status()
        {
            var s = await SeedAsync();
            var pending = await s.Hospitals.CreateHospitalAsync(Registration("pending@h.org"));
            await Assert.ThrowsAsync<ConflictException>(() =>
                s.Admin.CommentOnHospitalRegistrationAsync(pending.HospitalId, s.AdminId, new HospitalRegistrationCommentDto { Message = "Question?" }));

            var id = await RejectedHospitalAsync(s);
            var result = await s.Admin.CommentOnHospitalRegistrationAsync(id, s.AdminId,
                new HospitalRegistrationCommentDto { Message = "Please upload the 2026 license.", AttachmentUrl = Pdf, AttachmentName = "checklist.pdf" });

            Assert.Equal("Rejected", result.ApprovalStatus);
            var comment = result.ApprovalHistory.Last();
            Assert.Equal("AdminComment", comment.Type);
            Assert.True(comment.FromAdmin);
            Assert.Equal("checklist.pdf", comment.AttachmentName);
            Assert.Equal("admin@lifelink.org", comment.AdminName); // admins see who acted
            Assert.Equal(1, await s.Db.Notifications.CountAsync(n => n.HospitalId == id && n.NotificationType == "HospitalRegistrationComment"));
        }

        [Fact]
        public async Task Hospital_Replies_Only_While_Rejected_Record_Corrections_And_Notify_Admins()
        {
            var s = await SeedAsync();
            var pending = await s.Hospitals.CreateHospitalAsync(Registration("pending@h.org"));
            await Assert.ThrowsAsync<ConflictException>(() => s.Hospitals.ReplyToRegistrationAsync(pending.HospitalId, Reply()));

            var id = await RejectedHospitalAsync(s);
            var result = await s.Hospitals.ReplyToRegistrationAsync(id, new HospitalRegistrationReplyDto
            {
                Message = "Renewed license attached.",
                ContactNumber = "0119876543",
                LicenseDocumentUrl = Pdf, LicenseDocumentName = "license-2026.pdf",
                AttachmentUrl = Pdf, AttachmentName = "cover-letter.pdf"
            });

            Assert.Equal("Rejected", result.ApprovalStatus);
            Assert.True(result.AwaitingAdminReview);
            Assert.Equal("0119876543", result.ContactNumber);
            Assert.Equal("license-2026.pdf", result.LicenseDocumentName);
            var entry = result.ApprovalHistory.Last();
            Assert.Equal("HospitalReply", entry.Type);
            Assert.False(entry.FromAdmin);
            Assert.Equal("cover-letter.pdf", entry.AttachmentName);
            Assert.Equal(new[] { "Hospital Contact Number: 0112345678 -> 0119876543", "License Document: no file -> license-2026.pdf" }, entry.ChangedFields!.Split('\n'));
            Assert.Equal(new[] { "Submitted", "Rejected", "HospitalReply" }, result.ApprovalHistory.Select(e => e.Type).ToArray());
            Assert.True(await s.Db.Notifications.AnyAsync(n => n.UserId == s.AdminId && n.Title == "Hospital Registration Reply"));

            // Several replies in a row are allowed, each as one conversation entry
            var second = await s.Hospitals.ReplyToRegistrationAsync(id, Reply("One more document is on the way."));
            Assert.Equal(4, second.ApprovalHistory.Count);
            Assert.Null(second.ApprovalHistory.Last().ChangedFields);

            await s.Admin.ApproveHospitalAsync(id, s.AdminId);
            await Assert.ThrowsAsync<ConflictException>(() => s.Hospitals.ReplyToRegistrationAsync(id, Reply()));
        }

        [Fact]
        public async Task A_Reply_Must_Leave_A_Valid_Authorized_Person()
        {
            var s = await SeedAsync();
            var legacy = new Hospital { HospitalId = Guid.NewGuid(), Name = "Old Hospital", Email = "old@h.org", ContactNumber = "0112345678", ApprovalStatus = ApprovalStatus.Rejected };
            await s.Db.Hospitals.AddAsync(legacy);
            await s.Db.SaveChangesAsync();

            var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => s.Hospitals.ReplyToRegistrationAsync(legacy.HospitalId, Reply()));
            Assert.Equal("Authorized person name is required.", missing.Message);

            var fixedUp = await s.Hospitals.ReplyToRegistrationAsync(legacy.HospitalId, new HospitalRegistrationReplyDto
            {
                Message = "Added our authorized person.", ContactPersonName = "Dr. Fernando", ContactPersonPhone = "0712345678"
            });
            Assert.Equal("Dr. Fernando", fixedUp.ContactPersonName);
        }

        [Fact]
        public async Task Approval_And_Comments_Are_Refused_When_The_Hospital_Replied_After_The_Admin_Looked()
        {
            var s = await SeedAsync();
            var id = await RejectedHospitalAsync(s);
            var seen = (await s.Admin.GetAllHospitalsAsync()).Single().ApprovalHistory.Last().Id; // the rejection

            await s.Hospitals.ReplyToRegistrationAsync(id, Reply("New certificate uploaded."));

            await Assert.ThrowsAsync<ConflictException>(() => s.Admin.ApproveHospitalAsync(id, s.AdminId, seen));
            await Assert.ThrowsAsync<ConflictException>(() =>
                s.Admin.CommentOnHospitalRegistrationAsync(id, s.AdminId, new HospitalRegistrationCommentDto { Message = "Thanks", LastSeenEntryId = seen }));

            var latest = (await s.Admin.GetAllHospitalsAsync()).Single().ApprovalHistory.Last().Id;
            Assert.Equal("Approved", (await s.Admin.ApproveHospitalAsync(id, s.AdminId, latest)).ApprovalStatus);
            await Assert.ThrowsAsync<ConflictException>(() => s.Admin.ApproveHospitalAsync(id, s.AdminId)); // approved is read-only
        }

        [Fact]
        public async Task Queue_Lists_Approved_Hospitals_And_Pending_Holds_Only_Work_For_The_Admin()
        {
            var s = await SeedAsync();
            var approvedId = (await s.Hospitals.CreateHospitalAsync(Registration("approved@h.org"))).HospitalId;
            await s.Admin.ApproveHospitalAsync(approvedId, s.AdminId);
            await RejectedHospitalAsync(s, "waiting@h.org"); // waiting for the hospital
            var awaitingAdmin = await RejectedHospitalAsync(s, "replied@h.org");
            await s.Hospitals.ReplyToRegistrationAsync(awaitingAdmin, Reply());
            var pendingId = (await s.Hospitals.CreateHospitalAsync(Registration("new@h.org"))).HospitalId;

            var approved = (await s.Admin.GetAllHospitalsAsync()).Single(h => h.HospitalId == approvedId);
            Assert.Equal("Approved", approved.ApprovalStatus);
            Assert.Equal(new[] { "Submitted", "Approved" }, approved.ApprovalHistory.Select(e => e.Type).ToArray());
            Assert.Equal("Dr. Silva", approved.ContactPersonName); // registration details stay visible

            var queue = (await s.Admin.GetPendingHospitalsAsync()).Select(h => h.HospitalId).ToHashSet();
            Assert.Equal(new HashSet<Guid> { awaitingAdmin, pendingId }, queue);
            Assert.Equal(2, (await s.Admin.GetDashboardStatsAsync()).PendingHospitalApprovals);
        }

        [Fact]
        public async Task Hospital_Copy_Of_The_Conversation_Hides_Which_Admin_Acted()
        {
            var s = await SeedAsync();
            var id = await RejectedHospitalAsync(s);

            var rejection = (await s.Hospitals.GetHospitalByIdAsync(id))!.ApprovalHistory.Last();
            Assert.True(rejection.FromAdmin);
            Assert.Null(rejection.AdminId);
            Assert.Null(rejection.AdminName);

            var adminView = await s.Hospitals.GetHospitalByIdAsync(id, includeAdminIdentity: true);
            Assert.Equal("admin@lifelink.org", adminView!.ApprovalHistory.Last().AdminName);
        }

        // ---------- Empty files ----------

        [Fact]
        public async Task Empty_Files_Are_Refused_Throughout_The_Registration_Conversation()
        {
            var s = await SeedAsync();
            var bad = Registration("empty@h.org");
            bad.LicenseDocumentUrl = EmptyPdf;
            bad.LicenseDocumentName = "license.pdf";
            Assert.Equal(AttachmentRules.EmptyFileMessage, (await Assert.ThrowsAsync<InvalidOperationException>(() => s.Hospitals.CreateHospitalAsync(bad))).Message);

            var pending = await s.Hospitals.CreateHospitalAsync(Registration("ok@h.org"));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                s.Admin.RejectHospitalAsync(pending.HospitalId, s.AdminId, new RejectHospitalDto { Reason = "Expired", ReportDocumentUrl = EmptyPdf }));

            var id = await RejectedHospitalAsync(s, "rejected@h.org");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                s.Admin.CommentOnHospitalRegistrationAsync(id, s.AdminId, new HospitalRegistrationCommentDto { Message = "See file", AttachmentUrl = EmptyPdf }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                s.Hospitals.ReplyToRegistrationAsync(id, new HospitalRegistrationReplyDto { Message = "See file", AttachmentUrl = EmptyPdf }));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                s.Hospitals.ReplyToRegistrationAsync(id, new HospitalRegistrationReplyDto { Message = "New license", LicenseDocumentUrl = EmptyPdf }));
        }

        [Fact]
        public async Task Complaint_And_Appeal_Messages_Refuse_Empty_Files()
        {
            var s = await SeedAsync();
            var creator = new User { UserId = Guid.NewGuid(), FirstName = "C", LastName = "R", Email = "creator@t.org" };
            var suspended = new User { UserId = Guid.NewGuid(), FirstName = "S", LastName = "U", Email = "suspended@t.org", IsSuspended = true };
            await s.Db.Users.AddRangeAsync(creator, suspended);
            await s.Db.UserRoles.AddRangeAsync(new UserRole { UserId = creator.UserId, RoleId = UserRoleId }, new UserRole { UserId = suspended.UserId, RoleId = UserRoleId });
            await s.Db.SaveChangesAsync();

            var complaints = new ComplaintService(s.Db, s.Notify);
            var complaint = await complaints.CreateComplaintAsync(creator.UserId, null,
                new CreateComplaintDto { ComplaintType = "Donation Process", Subject = "Late response", Description = "The hospital responded very late." });
            var complaintError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                complaints.AdminReplyAsync(complaint.ComplaintId, s.AdminId, new ReviewComplaintDto { Notes = "See the file", AttachmentUrl = EmptyPdf, AttachmentName = "x.pdf" }));
            Assert.Equal(AttachmentRules.EmptyFileMessage, complaintError.Message);

            var appeals = new AppealService(s.Db, s.Notify);
            var appealError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                appeals.SubmitAppealAsync(new CreateAppealDto { Reason = "Please review my suspension.", AttachmentUrl = EmptyPdf, AttachmentName = "proof.pdf" }, suspended.UserId));
            Assert.Equal(AttachmentRules.EmptyFileMessage, appealError.Message);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("data:application/pdf;base64,", false)]
        [InlineData("data:application/pdf;base64,   ", false)]
        [InlineData("data:application/pdf;base64", false)]
        [InlineData("data:application/pdf;base64,JVBERi0=", true)]
        [InlineData("https://files.example/report.pdf", true)]
        public void Attachment_Content_Detection(string? url, bool expected) => Assert.Equal(expected, AttachmentRules.HasContent(url));

        [Fact]
        public void Zero_Byte_Files_Stored_Earlier_Are_Not_Offered_For_Download()
        {
            var hospital = new Hospital
            {
                ApprovalHistories = { new HospitalApprovalHistory { Status = RegistrationEntryType.Rejected, AdminId = Guid.NewGuid(), ReportDocumentName = "empty.pdf", ReportDocumentUrl = EmptyPdf } }
            };
            var entry = RegistrationThread.ToDtos(hospital, includeAdminIdentity: true).Single();
            Assert.Null(entry.AttachmentUrl);
            Assert.Null(entry.AttachmentName);
        }

        // ---------- Admin identity, old data, validation ----------

        [Fact]
        public void Complainant_And_Appellant_Copies_Drop_Admin_Identity_But_Keep_Who_Wrote()
        {
            var complaint = new ComplaintResponseDto
            {
                AssignedAdminId = Guid.NewGuid(),
                AssignedAdminEmail = "admin@lifelink.org",
                AuditLogs = { new ComplaintAuditLogDto { AdminId = Guid.NewGuid(), AdminEmail = "admin@lifelink.org", FromAdmin = true, Notes = "Hello" } }
            };
            var forComplainant = AdminIdentityRedaction.ForComplainant(complaint);
            Assert.Null(forComplainant.AssignedAdminId);
            Assert.Null(forComplainant.AssignedAdminEmail);
            Assert.Null(forComplainant.AuditLogs[0].AdminId);
            Assert.Null(forComplainant.AuditLogs[0].AdminEmail);
            Assert.True(forComplainant.AuditLogs[0].FromAdmin);

            var appeal = new AppealResponseDto
            {
                ReviewedByAdminId = Guid.NewGuid(),
                Messages = { new AppealMessageDto { FromAdmin = true, AdminEmail = "admin@lifelink.org", Message = "Reviewed" } }
            };
            var forAppellant = AdminIdentityRedaction.ForAppellant(appeal);
            Assert.Null(forAppellant.ReviewedByAdminId);
            Assert.Null(forAppellant.Messages[0].AdminEmail);
            Assert.True(forAppellant.Messages[0].FromAdmin);
        }

        [Fact]
        public void Values_Written_By_The_Old_Resubmission_Workflow_Are_Read_Under_The_New_One()
        {
            Assert.False(Enum.IsDefined(typeof(ApprovalStatus), "Resubmitted"));
            Assert.Equal(ApprovalStatus.Rejected, RegistrationStatusConversions.ParseApprovalStatus("Resubmitted"));
            Assert.Equal(ApprovalStatus.Approved, RegistrationStatusConversions.ParseApprovalStatus("Approved"));
            Assert.Equal(RegistrationEntryType.Submitted, RegistrationStatusConversions.ParseEntryType("Pending"));
            Assert.Equal(RegistrationEntryType.HospitalReply, RegistrationStatusConversions.ParseEntryType("Resubmitted"));
            Assert.Equal(RegistrationEntryType.AdminComment, RegistrationStatusConversions.ParseEntryType("AdminComment"));
        }

        private static List<string> Errors(object dto)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(dto, new ValidationContext(dto), results, validateAllProperties: true);
            return results.Select(r => r.ErrorMessage!).ToList();
        }

        [Fact]
        public void Registration_Requires_The_Authorized_Person_And_Ten_Digit_Phone_Numbers()
        {
            Assert.Empty(Errors(Registration()));

            var missing = Registration();
            missing.ContactPersonName = null;
            missing.ContactPersonPhone = "";
            Assert.Contains("Authorized person name is required.", Errors(missing));
            Assert.Contains("Authorized person phone number is required.", Errors(missing));

            var wrongLength = Registration();
            wrongLength.ContactNumber = "011234567";
            wrongLength.ContactPersonPhone = "07712345678";
            Assert.Contains("Hospital contact number must be exactly 10 digits.", Errors(wrongLength));
            Assert.Contains("Authorized person phone number must be exactly 10 digits.", Errors(wrongLength));
        }

        [Fact]
        public void Profile_Edit_And_Replies_Apply_The_Same_Rules()
        {
            var profile = new UpdateHospitalProfileDto { Name = "City", Address = "1 Main", ContactNumber = "0112345678" };
            Assert.Contains("Authorized person name is required.", Errors(profile));
            Assert.Contains("Authorized person phone number is required.", Errors(profile));

            Assert.Empty(Errors(new HospitalRegistrationReplyDto { Message = "Only a message." }));
            Assert.Contains("Authorized person phone number must be exactly 10 digits.",
                Errors(new HospitalRegistrationReplyDto { Message = "Fix", ContactPersonPhone = "12345" }));
        }

        // ---------- Sign-in requirements (Phase S) ----------

        private static bool IsPublic(Type controller, MethodInfo action) =>
            action.GetCustomAttribute<AllowAnonymousAttribute>() != null ||
            (action.GetCustomAttribute<AuthorizeAttribute>() == null && controller.GetCustomAttribute<AuthorizeAttribute>() == null);

        [Fact]
        public void Only_Intended_Endpoints_Are_Reachable_Without_Signing_In()
        {
            var publicEndpoints = typeof(HospitalsController).Assembly.GetTypes()
                .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute))
                    .Where(m => IsPublic(t, m))
                    .Select(m => $"{t.Name}.{m.Name}"))
                .OrderBy(n => n)
                .ToList();

            Assert.Equal(new[]
            {
                "AuthController.ForgotPassword", "AuthController.Login", "AuthController.Logout", "AuthController.Register",
                "AuthController.ResendOtp", "AuthController.ResetPassword", "AuthController.VerifyOtp",
                "BloodRequestsController.GetPublicRequests",
                "HospitalsController.CreateHospital",
                "ProfilesController.GetHospitalProfile"
            }, publicEndpoints);
        }

        [Fact]
        public void Hospital_Endpoints_Are_Limited_To_The_Right_Roles()
        {
            AuthorizeAttribute Role(string action) => typeof(HospitalsController).GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>()!;
            Assert.Equal("Admin", Role(nameof(HospitalsController.VerifyHospital)).Roles);
            Assert.Equal("HospitalStaff", Role(nameof(HospitalsController.ReplyToRegistration)).Roles);
            Assert.Equal("HospitalStaff", Role(nameof(HospitalsController.GetMyHospital)).Roles);
            Assert.DoesNotContain(typeof(HospitalsController).GetMethods(), m => m.Name.Contains("Resubmit"));
            Assert.NotNull(typeof(AppealsController).GetMethod(nameof(AppealsController.SubmitAppeal))!.GetCustomAttribute<AuthorizeAttribute>());
        }

        [Fact]
        public async Task Public_Hospital_Profile_Hides_The_Authorized_Persons_Direct_Contact_From_Signed_Out_Visitors()
        {
            var s = await SeedAsync();
            var created = await s.Hospitals.CreateHospitalAsync(Registration());

            ProfilesController Controller(bool signedIn)
            {
                var user = new Mock<ICurrentUserService>();
                user.SetupGet(u => u.IsAuthenticated).Returns(signedIn);
                user.SetupGet(u => u.Roles).Returns(signedIn ? new[] { "User" } : Array.Empty<string>());
                return new ProfilesController(s.Db, user.Object, s.Hospitals);
            }

            var anonymous = (HospitalProfileDto)((OkObjectResult)await Controller(false).GetHospitalProfile(created.HospitalId)).Value!;
            Assert.Equal("Dr. Silva", anonymous.ContactPersonName);
            Assert.Null(anonymous.ContactPersonPhone);

            var signedIn = (HospitalProfileDto)((OkObjectResult)await Controller(true).GetHospitalProfile(created.HospitalId)).Value!;
            Assert.Equal("0771234567", signedIn.ContactPersonPhone);
        }
    }
}
