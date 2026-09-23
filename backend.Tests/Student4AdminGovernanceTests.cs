using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Admin;
using LifeLink.DTOs.Appeals;
using LifeLink.DTOs.Auth;
using LifeLink.DTOs.Complaints;
using LifeLink.DTOs.HospitalActivity;
using LifeLink.Entities;
using LifeLink.Middleware;
using LifeLink.Services.Admin;
using LifeLink.Services.Appeals;
using LifeLink.Services.Auth;
using LifeLink.Services.Complaints;
using LifeLink.Services.HospitalActivity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace LifeLink.Tests
{
    public class Student4AdminGovernanceTests
    {
        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            var context = new AppDbContext(options);
            context.Database.EnsureCreated();
            return context;
        }

        private IConfiguration GetMockConfiguration()
        {
            var inMemorySettings = new Dictionary<string, string>
            {
                { "Jwt:Key", "CHANGE_ME_IN_ENVIRONMENT_PRODUCTION_SECRET_KEY_LIFELINK_2026_MIN_32_BYTES" },
                { "Jwt:Issuer", "LifeLinkAPI" },
                { "Jwt:Audience", "LifeLinkApp" },
                { "Jwt:ExpiryMinutes", "60" }
            };

            return new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings!)
                .Build();
        }

        #region Hospital Approval & Synchronization Tests

        [Fact]
        public async Task Hospital_Approval_Synchronizes_IsVerified_True()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var adminService = new AdminService(context, notifService);

            var adminId = Guid.NewGuid();
            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "Mercy Health Hospital",
                Email = "admin@mercyhealth.org",
                ApprovalStatus = ApprovalStatus.Pending,
                IsVerified = false
            };
            await context.Hospitals.AddAsync(hospital);
            await context.SaveChangesAsync();

            // Act
            var result = await adminService.ApproveHospitalAsync(hospital.HospitalId, adminId);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Approved", result.ApprovalStatus);
            Assert.True(result.IsVerified); // Enforced synchronization!
            Assert.Equal(adminId, result.ApprovedByAdminId);
            Assert.NotNull(result.ApprovedAt);

            var dbHospital = await context.Hospitals.FindAsync(hospital.HospitalId);
            Assert.NotNull(dbHospital);
            Assert.Equal(ApprovalStatus.Approved, dbHospital.ApprovalStatus);
            Assert.True(dbHospital.IsVerified);

            // Verify in-app notification
            var notif = await context.Notifications.FirstOrDefaultAsync(n => n.HospitalId == hospital.HospitalId);
            Assert.NotNull(notif);
            Assert.Equal("HospitalApproved", notif.NotificationType);

            // Verify email dispatch
            mockEmail.Verify(e => e.SendEmailAsync(hospital.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task Hospital_Rejection_Synchronizes_IsVerified_False_And_Records_Reason()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var adminService = new AdminService(context, notifService);

            var adminId = Guid.NewGuid();
            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "Invalid License Hospital",
                Email = "contact@invalidhospital.org",
                ApprovalStatus = ApprovalStatus.Pending,
                IsVerified = false
            };
            await context.Hospitals.AddAsync(hospital);
            await context.SaveChangesAsync();

            var rejectDto = new RejectHospitalDto
            {
                Reason = "Expired medical license and non-compliant cold-storage verification."
            };

            // Act
            var result = await adminService.RejectHospitalAsync(hospital.HospitalId, adminId, rejectDto);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("Rejected", result.ApprovalStatus);
            Assert.False(result.IsVerified); // Enforced synchronization!
            Assert.Equal(rejectDto.Reason, result.RejectionReason);

            var dbHospital = await context.Hospitals.FindAsync(hospital.HospitalId);
            Assert.NotNull(dbHospital);
            Assert.Equal(ApprovalStatus.Rejected, dbHospital.ApprovalStatus);
            Assert.False(dbHospital.IsVerified);

            mockEmail.Verify(e => e.SendEmailAsync(hospital.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task Hospital_Rejection_With_Report_And_Resubmission_Workflow()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var adminService = new AdminService(context, notifService);
            var hospitalService = new LifeLink.Services.Hospitals.HospitalService(context);

            var adminId = Guid.NewGuid();
            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "Apex Hospital",
                LicenseNumber = "PHSRC/PH/999",
                Address = "Old Address",
                ContactNumber = "0112223334",
                Email = "admin@apexhospital.org",
                ApprovalStatus = ApprovalStatus.Pending,
                IsVerified = false
            };
            await context.Hospitals.AddAsync(hospital);
            await context.SaveChangesAsync();

            // Act 1: Admin rejects with report
            var rejectDto = new RejectHospitalDto
            {
                Reason = "Please re-upload valid 2026 accreditation certificate.",
                ReportDocumentName = "Audit_Report.pdf",
                ReportDocumentUrl = "data:application/pdf;base64,dGVzdA=="
            };
            var rejectResult = await adminService.RejectHospitalAsync(hospital.HospitalId, adminId, rejectDto);

            // Assert rejection
            Assert.Equal("Rejected", rejectResult.ApprovalStatus);
            Assert.Equal("Audit_Report.pdf", rejectResult.RejectionReportName);
            Assert.Equal("data:application/pdf;base64,dGVzdA==", rejectResult.RejectionReportUrl);

            // Act 2: Hospital resubmits with updated information
            var resubmitDto = new LifeLink.DTOs.Hospitals.ResubmitHospitalDto
            {
                Name = "Apex Hospital Colombo",
                Address = "New Healthcare Blvd, Colombo",
                City = "Colombo",
                AccreditationDocumentName = "Accreditation_2026.pdf",
                AccreditationDocumentUrl = "data:application/pdf;base64,bmV3",
                Comments = "Attached renewed 2026 certificate and corrected facility address."
            };
            var resubmitResult = await hospitalService.ResubmitHospitalAsync(hospital.HospitalId, resubmitDto);

            // Assert resubmission
            Assert.Equal("Resubmitted", resubmitResult.ApprovalStatus);
            Assert.Equal("Apex Hospital Colombo", resubmitResult.Name);
            Assert.Equal("Colombo", resubmitResult.City);
            Assert.Contains("Hospital Name", resubmitResult.UpdatedFields!);
            Assert.Contains("Address", resubmitResult.UpdatedFields!);
            Assert.NotNull(resubmitResult.ResubmittedAt);
            Assert.NotEmpty(resubmitResult.ApprovalHistory);

            // Verify queue includes resubmitted hospital
            var pendingQueue = await adminService.GetPendingHospitalsAsync();
            Assert.Contains(pendingQueue, h => h.HospitalId == hospital.HospitalId && h.ApprovalStatus == "Resubmitted");
        }

        #endregion

        #region User Suspension & Reinstatement Tests

        [Fact]
        public async Task User_Suspension_Sets_Restricted_Flags_And_AccountStatus()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var adminService = new AdminService(context, notifService);

            var user = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "John",
                LastName = "Doe",
                Email = "johndoe@example.com",
                AccountStatus = AccountStatus.Active,
                IsSuspended = false
            };
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            var suspendDto = new SuspendUserDto
            {
                Reason = "Multiple false donor confirmations reported.",
                SuspendedUntil = DateTime.UtcNow.AddDays(14)
            };

            // Act
            var result = await adminService.SuspendUserAsync(user.UserId, suspendDto);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.IsSuspended);
            Assert.Equal(suspendDto.Reason, result.SuspensionReason);
            Assert.Equal(suspendDto.SuspendedUntil, result.SuspendedUntil);
            Assert.Equal("Suspended", result.AccountStatus);

            var dbUser = await context.Users.FindAsync(user.UserId);
            Assert.NotNull(dbUser);
            Assert.True(dbUser.IsSuspended);
            Assert.Equal(AccountStatus.Suspended, dbUser.AccountStatus);

            mockEmail.Verify(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.Is<string>(b => b.Contains("Appeal Instructions")), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task User_Reinstatement_Restores_Active_AccountStatus()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var adminService = new AdminService(context, notifService);

            var user = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Jane",
                LastName = "Smith",
                Email = "janesmith@example.com",
                AccountStatus = AccountStatus.Suspended,
                IsSuspended = true,
                SuspensionReason = "Pending investigation",
                SuspendedUntil = DateTime.UtcNow.AddDays(7)
            };
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            // Act
            var result = await adminService.ReinstateUserAsync(user.UserId);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.IsSuspended);
            Assert.Null(result.SuspensionReason);
            Assert.Null(result.SuspendedUntil);
            Assert.Equal("Active", result.AccountStatus);

            var dbUser = await context.Users.FindAsync(user.UserId);
            Assert.NotNull(dbUser);
            Assert.False(dbUser.IsSuspended);
            Assert.Equal(AccountStatus.Active, dbUser.AccountStatus);

            mockEmail.Verify(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        #endregion

        #region Hospital Suspension & Reinstatement Tests

        [Fact]
        public async Task Hospital_Suspension_And_Reinstatement_Lifecycle()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var adminService = new AdminService(context, notifService);

            var hospital = new Hospital
            {
                HospitalId = Guid.NewGuid(),
                Name = "St. Peter Hospital",
                Email = "contact@stpeter.org",
                ApprovalStatus = ApprovalStatus.Approved,
                IsVerified = true,
                IsSuspended = false
            };
            await context.Hospitals.AddAsync(hospital);
            await context.SaveChangesAsync();

            // 1. Suspend Hospital
            var suspendResult = await adminService.SuspendHospitalAsync(hospital.HospitalId, new SuspendHospitalDto
            {
                Reason = "Failure to submit mandatory blood storage verification logs.",
                SuspendedUntil = DateTime.UtcNow.AddDays(30)
            });
            Assert.True(suspendResult.IsSuspended);
            Assert.NotNull(suspendResult.SuspensionReason);

            // 2. Reinstate Hospital
            var reinstateResult = await adminService.ReinstateHospitalAsync(hospital.HospitalId);
            Assert.False(reinstateResult.IsSuspended);
            Assert.Null(reinstateResult.SuspensionReason);
            Assert.Null(reinstateResult.SuspendedUntil);
        }

        #endregion

        #region Complaint & Activity Report Lifecycle Tests

        [Fact]
        public async Task Complaint_Lifecycle_Full_Progression_And_Audit_Logging()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var complaintService = new ComplaintService(context, notifService);

            var userId = Guid.NewGuid();
            var hospitalId = Guid.NewGuid();
            var adminId = Guid.NewGuid();

            var user = new User { UserId = userId, Email = "complainant@example.com", FirstName = "Mark", LastName = "Ruffalo" };
            var hospital = new Hospital { HospitalId = hospitalId, Name = "Valley Hospital", Email = "contact@valley.org", IsVerified = true };
            var admin = new User { UserId = adminId, Email = "admin@lifelink.org", FirstName = "Admin", LastName = "Master" };

            await context.Users.AddRangeAsync(user, admin);
            await context.Hospitals.AddAsync(hospital);
            await context.SaveChangesAsync();

            // Step 1: Create Complaint (Status: OPEN)
            var createDto = new CreateComplaintDto
            {
                ComplaintType = "ServiceQuality",
                Subject = "Delayed Blood Delivery",
                Description = "Emergency transfer request was delayed by 3 hours without explanation.",
                HospitalId = hospitalId
            };
            var complaint = await complaintService.CreateComplaintAsync(userId, null, createDto);

            Assert.NotNull(complaint);
            Assert.Equal("OPEN", complaint.Status);
            Assert.Single(complaint.AuditLogs);
            Assert.Equal("OPEN", complaint.AuditLogs[0].NewStatus);

            // Step 2: Admin Reviews Complaint (Status: UNDER_REVIEW)
            var reviewed = await complaintService.ReviewComplaintAsync(complaint.ComplaintId, adminId, new ReviewComplaintDto
            {
                Notes = "Investigating transfer logs with logistics provider."
            });
            Assert.Equal("UNDER_REVIEW", reviewed.Status);
            Assert.Equal(2, reviewed.AuditLogs.Count);

            // Step 3: Admin Requests Activity Report from Hospital (Status: AWAITING_INFORMATION)
            var requested = await complaintService.RequestActivityReportAsync(complaint.ComplaintId, adminId, new RequestActivityReportDto
            {
                Instructions = "Submit logistics dispatch timestamps and ambulance logs."
            });
            Assert.Equal("AWAITING_INFORMATION", requested.Status);
            Assert.Equal(3, requested.AuditLogs.Count);

            // Verify hospital received notification
            var hospitalNotif = await context.Notifications.FirstOrDefaultAsync(n => n.HospitalId == hospitalId && n.NotificationType == "ActivityReportRequested");
            Assert.NotNull(hospitalNotif);

            // Step 4: Admins cannot mark a complaint as solved (current rule: creator only)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                complaintService.ResolveComplaintAsync(complaint.ComplaintId, adminId, new ResolveComplaintDto
                {
                    Status = "RESOLVED",
                    ResolutionNotes = "Hospital provided proof of traffic road blockage; procedure guidelines updated."
                }));

            // Step 5: The complaint creator marks it solved (Status: RESOLVED)
            var resolved = await complaintService.SolveComplaintAsync(complaint.ComplaintId, userId,
                "Hospital provided proof of traffic road blockage; procedure guidelines updated.");
            Assert.Equal("RESOLVED", resolved.Status);
            Assert.NotNull(resolved.ResolvedAt);
            Assert.Equal(4, resolved.AuditLogs.Count); // refused admin attempt adds no audit entry
        }

        [Fact]
        public async Task HospitalActivityReport_Submission_Preserves_Investigation_Context()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var complaintService = new ComplaintService(context, notifService);
            var activityService = new HospitalActivityService(context);

            var hospitalId = Guid.NewGuid();
            var adminId = Guid.NewGuid();

            var hospital = new Hospital { HospitalId = hospitalId, Name = "Trinity Medical", Email = "trinity@hospital.org" };
            var admin = new User { UserId = adminId, Email = "lead_admin@lifelink.org", FirstName = "Chief", LastName = "Admin" };

            await context.Hospitals.AddAsync(hospital);
            await context.Users.AddAsync(admin);
            await context.SaveChangesAsync();

            var complaint = await complaintService.CreateComplaintAsync(null, hospitalId, new CreateComplaintDto
            {
                ComplaintType = "InventoryMismatch",
                Subject = "Reported unit discrepancy",
                Description = "Stock levels differed from dashboard count.",
                HospitalId = hospitalId
            });

            // Move to review by admin
            await complaintService.ReviewComplaintAsync(complaint.ComplaintId, adminId);

            // Act - Hospital submits activity report
            var reportDto = new SubmitActivityReportDto
            {
                HospitalId = hospitalId,
                ComplaintId = complaint.ComplaintId,
                Title = "Inventory Discrepancy Audit Log",
                Description = "Physical count confirmed 1 unit was quarantined due to low temperature sensor alarm."
            };

            var report = await activityService.SubmitActivityReportAsync(reportDto);

            // Assert
            Assert.NotNull(report);
            Assert.Equal(hospitalId, report.HospitalId);
            Assert.Equal(complaint.ComplaintId, report.ComplaintId);
            Assert.Equal(adminId, report.RequestedByAdminId); // Preserved investigation context!
            Assert.Equal("Inventory Discrepancy Audit Log", report.Title);

            // Verify complaint audit log includes the activity submission
            var updatedComplaint = await complaintService.GetComplaintByIdAsync(complaint.ComplaintId);
            Assert.NotNull(updatedComplaint);
            Assert.Contains(updatedComplaint.AuditLogs, a => a.Notes != null && a.Notes.Contains("submitted activity report"));
        }

        [Fact]
        public async Task HospitalActivityReport_Submission_Fails_If_Hospital_Not_Associated()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var notifService = new AdminNotificationService(context, new Mock<IEmailService>().Object, NullLogger<AdminNotificationService>.Instance);
            var complaintService = new ComplaintService(context, notifService);
            var activityService = new HospitalActivityService(context);

            var associatedHospitalId = Guid.NewGuid();
            var unassociatedHospitalId = Guid.NewGuid();

            var hospitalA = new Hospital { HospitalId = associatedHospitalId, Name = "Hospital A", Email = "a@hospital.org" };
            var hospitalB = new Hospital { HospitalId = unassociatedHospitalId, Name = "Hospital B", Email = "b@hospital.org" };

            await context.Hospitals.AddRangeAsync(hospitalA, hospitalB);
            await context.SaveChangesAsync();

            var complaint = await complaintService.CreateComplaintAsync(null, associatedHospitalId, new CreateComplaintDto
            {
                ComplaintType = "ServiceQuality",
                Subject = "Delayed response",
                Description = "Emergency ward delay.",
                HospitalId = associatedHospitalId
            });

            // Act & Assert - Hospital B attempts to submit evidence for Hospital A's complaint
            var invalidReportDto = new SubmitActivityReportDto
            {
                HospitalId = unassociatedHospitalId,
                ComplaintId = complaint.ComplaintId,
                Title = "Unrelated report",
                Description = "Trying to submit arbitrary report."
            };

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => activityService.SubmitActivityReportAsync(invalidReportDto));
            Assert.Contains("not associated with this complaint investigation", ex.Message);
        }

        #endregion

        #region Appeal Lifecycle & Automatic Reinstatement Tests

        [Fact]
        public async Task Appeal_Approval_Automatically_Reinstates_Suspended_User()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var appealService = new AppealService(context, notifService);

            var userId = Guid.NewGuid();
            var adminId = Guid.NewGuid();

            var user = new User
            {
                UserId = userId,
                Email = "suspended_donor@example.com",
                FirstName = "Sam",
                LastName = "Wilson",
                IsSuspended = true,
                SuspensionReason = "Missed appointments",
                AccountStatus = AccountStatus.Suspended
            };
            var admin = new User { UserId = adminId, Email = "admin@example.com", FirstName = "Admin", LastName = "User" };

            await context.Users.AddRangeAsync(user, admin);
            await context.SaveChangesAsync();

            // Act 1: User submits appeal
            var appeal = await appealService.SubmitAppealAsync(new CreateAppealDto
            {
                Reason = "I had an unexpected medical emergency and could not cancel in advance. Documentation attached.",
                UserId = userId
            }, userId);

            Assert.NotNull(appeal);
            Assert.Equal("PENDING", appeal.Status);

            // Act 2: Admin approves appeal
            var reviewResult = await appealService.ApproveAppealAsync(appeal.AppealId, adminId, new ReviewAppealDto
            {
                AdminResponse = "Medical documentation accepted. Suspension lifted."
            });

            // Assert
            Assert.NotNull(reviewResult);
            Assert.Equal("APPROVED", reviewResult.Status);

            // Verify Automatic Reinstatement of User!
            var dbUser = await context.Users.FindAsync(userId);
            Assert.NotNull(dbUser);
            Assert.False(dbUser.IsSuspended);
            Assert.Null(dbUser.SuspensionReason);
            Assert.Null(dbUser.SuspendedUntil);
            Assert.Equal(AccountStatus.Active, dbUser.AccountStatus);

            // Verify notification and email
            mockEmail.Verify(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.Is<string>(b => b.Contains("APPROVED")), It.IsAny<bool>()), Times.Once);
        }

        [Fact]
        public async Task Appeal_Approval_Automatically_Reinstates_Suspended_Hospital()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var appealService = new AppealService(context, notifService);

            var hospitalId = Guid.NewGuid();
            var adminId = Guid.NewGuid();

            var hospital = new Hospital
            {
                HospitalId = hospitalId,
                Name = "Suspended Medical Center",
                Email = "ops@suspendedcenter.org",
                IsSuspended = true,
                SuspensionReason = "Pending audit"
            };
            var admin = new User { UserId = adminId, Email = "admin@example.com" };

            await context.Hospitals.AddAsync(hospital);
            await context.Users.AddAsync(admin);
            await context.SaveChangesAsync();

            var appeal = await appealService.SubmitAppealAsync(new CreateAppealDto
            {
                Reason = "Audit complete, compliance cert attached.",
                HospitalId = hospitalId
            });

            var reviewResult = await appealService.ApproveAppealAsync(appeal.AppealId, adminId, new ReviewAppealDto
            {
                AdminResponse = "Approved after review."
            });

            Assert.Equal("APPROVED", reviewResult.Status);

            var dbHospital = await context.Hospitals.FindAsync(hospitalId);
            Assert.NotNull(dbHospital);
            Assert.False(dbHospital.IsSuspended);
            Assert.Null(dbHospital.SuspensionReason);
        }

        [Fact]
        public async Task Appeal_Rejection_Maintains_Suspension()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var appealService = new AppealService(context, notifService);

            var userId = Guid.NewGuid();
            var adminId = Guid.NewGuid();

            var user = new User
            {
                UserId = userId,
                Email = "repeat_offender@example.com",
                IsSuspended = true,
                AccountStatus = AccountStatus.Suspended
            };
            var admin = new User { UserId = adminId, Email = "admin@example.com" };

            await context.Users.AddRangeAsync(user, admin);
            await context.SaveChangesAsync();

            var appeal = await appealService.SubmitAppealAsync(new CreateAppealDto
            {
                Reason = "Please unban me.",
                UserId = userId
            }, userId);

            var reviewResult = await appealService.RejectAppealAsync(appeal.AppealId, adminId, new ReviewAppealDto
            {
                AdminResponse = "Insufficient explanation. Suspension stands."
            });

            Assert.Equal("REJECTED", reviewResult.Status);

            var dbUser = await context.Users.FindAsync(userId);
            Assert.NotNull(dbUser);
            Assert.True(dbUser.IsSuspended);
            Assert.Equal(AccountStatus.Suspended, dbUser.AccountStatus);

            mockEmail.Verify(e => e.SendEmailAsync(user.Email, It.IsAny<string>(), It.Is<string>(b => b.Contains("REJECTED")), It.IsAny<bool>()), Times.Once);
        }

        #endregion

        #region Admin Dashboard Statistics Tests

        [Fact]
        public async Task Admin_Dashboard_Statistics_Calculates_All_9_Metrics()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var mockEmail = new Mock<IEmailService>();
            var notifService = new AdminNotificationService(context, mockEmail.Object, NullLogger<AdminNotificationService>.Instance);
            var adminService = new AdminService(context, notifService);

            // 1. Users (3 total: 1 active, 1 suspended, 1 admin)
            var user1 = new User { UserId = Guid.NewGuid(), Email = "user1@example.com", IsSuspended = false };
            var user2 = new User { UserId = Guid.NewGuid(), Email = "user2@example.com", IsSuspended = true, AccountStatus = AccountStatus.Suspended };
            var admin = new User { UserId = Guid.NewGuid(), Email = "admin@example.com", IsSuspended = false };
            await context.Users.AddRangeAsync(user1, user2, admin);

            // 2. Hospitals (2 total: 1 approved, 1 pending & suspended)
            var hosp1 = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hosp 1", ApprovalStatus = ApprovalStatus.Approved, IsVerified = true };
            var hosp2 = new Hospital { HospitalId = Guid.NewGuid(), Name = "Hosp 2", ApprovalStatus = ApprovalStatus.Pending, IsVerified = false, IsSuspended = true };
            await context.Hospitals.AddRangeAsync(hosp1, hosp2);

            // 3. Doctors (1 total)
            var doc = new Doctor { DoctorId = Guid.NewGuid(), HospitalId = hosp1.HospitalId, Email = "doc@hosp1.org", FirstName = "Greg", LastName = "House" };
            await context.Doctors.AddAsync(doc);

            // 4. Blood Requests (2 total: 1 Pending, 1 Completed)
            var req1 = new BloodRequest { BloodRequestId = Guid.NewGuid(), HospitalId = hosp1.HospitalId, PatientUserId = user1.UserId, BloodGroup = "O+", UnitsRequired = 2, Status = BloodRequestStatus.Pending };
            var req2 = new BloodRequest { BloodRequestId = Guid.NewGuid(), HospitalId = hosp1.HospitalId, PatientUserId = user1.UserId, BloodGroup = "A+", UnitsRequired = 1, Status = BloodRequestStatus.Completed };
            await context.BloodRequests.AddRangeAsync(req1, req2);

            // 5. Complaints (2 total: 1 OPEN, 1 RESOLVED)
            var c1 = new Complaint { ComplaintId = Guid.NewGuid(), Status = ComplaintStatus.OPEN, Subject = "Issue 1", Description = "Desc 1" };
            var c2 = new Complaint { ComplaintId = Guid.NewGuid(), Status = ComplaintStatus.RESOLVED, Subject = "Issue 2", Description = "Desc 2" };
            await context.Complaints.AddRangeAsync(c1, c2);

            // 6. Appeals (2 total: 1 PENDING, 1 REJECTED)
            var a1 = new Appeal { AppealId = Guid.NewGuid(), Status = AppealStatus.PENDING, Reason = "Appeal reason 1" };
            var a2 = new Appeal { AppealId = Guid.NewGuid(), Status = AppealStatus.REJECTED, Reason = "Appeal reason 2" };
            await context.Appeals.AddRangeAsync(a1, a2);

            await context.SaveChangesAsync();

            // Act
            var stats = await adminService.GetDashboardStatsAsync();

            // Assert (all 9 metrics!)
            Assert.Equal(3, stats.TotalUsers);
            Assert.Equal(2, stats.TotalHospitals);
            Assert.Equal(1, stats.TotalDoctors);
            Assert.Equal(1, stats.ActiveRequests);
            Assert.Equal(1, stats.PendingComplaints);
            Assert.Equal(1, stats.PendingHospitalApprovals);
            Assert.Equal(1, stats.PendingAppeals);
            Assert.Equal(1, stats.ActiveSuspendedUsers);
            Assert.Equal(1, stats.ActiveSuspendedHospitals);
        }

        #endregion

        #region Restricted Governance Mode & Middleware Tests

        [Fact]
        public async Task RestrictedGovernanceMode_Allows_Login_For_Suspended_User()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var passwordHasher = new PasswordHasherService();
            var jwtService = new JwtService(GetMockConfiguration());
            var passwordResetService = new PasswordResetService(context);
            var mockEmailService = new Mock<IEmailService>();

            var authService = new AuthService(context, passwordHasher, jwtService, passwordResetService, mockEmailService.Object);

            var user = new User
            {
                UserId = Guid.NewGuid(),
                FirstName = "Suspended",
                LastName = "Donor",
                Email = "restricted_donor@example.com",
                AccountStatus = AccountStatus.Suspended,
                IsSuspended = true,
                SuspensionReason = "Temporary block"
            };
            user.PasswordHash = passwordHasher.HashPassword(user, "Password123!");
            context.Users.Add(user);
            await context.SaveChangesAsync();

            // Act - In Restricted Governance Mode, authentication succeeds and returns JWT
            var result = await authService.LoginAsync(new LoginRequestDto
            {
                Email = "restricted_donor@example.com",
                Password = "Password123!"
            });

            // Assert
            Assert.NotNull(result);
            Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
            Assert.Equal("Suspended", result.User.AccountStatus);
            Assert.True(result.User.IsSuspended);
        }

        [Fact]
        public async Task RestrictedGovernanceModeMiddleware_Blocks_Operational_Action_For_Suspended_User()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var userId = Guid.NewGuid();
            var user = new User
            {
                UserId = userId,
                Email = "blocked@example.com",
                IsSuspended = true,
                AccountStatus = AccountStatus.Suspended
            };
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            bool nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new RestrictedGovernanceModeMiddleware(next);

            var httpContext = new DefaultHttpContext();
            var responseBody = new MemoryStream();
            httpContext.Response.Body = responseBody;

            // Authenticated as suspended user
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "User")
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

            // Set endpoint WITHOUT [AllowSuspendedAccess] (operational action)
            var endpoint = new Endpoint(c => Task.CompletedTask, new EndpointMetadataCollection(), "CreateBloodRequest");
            httpContext.SetEndpoint(endpoint);

            // Act
            await middleware.InvokeAsync(httpContext, context);

            // Assert
            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status403Forbidden, httpContext.Response.StatusCode);

            responseBody.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(responseBody);
            var responseJson = await reader.ReadToEndAsync();
            Assert.Contains("Account is suspended. Only governance and appeal actions are allowed in Restricted Governance Mode.", responseJson);
        }

        [Fact]
        public async Task RestrictedGovernanceModeMiddleware_Permits_Governance_Action_With_AllowSuspendedAccess()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            var userId = Guid.NewGuid();
            var user = new User
            {
                UserId = userId,
                Email = "suspended_user@example.com",
                IsSuspended = true,
                AccountStatus = AccountStatus.Suspended
            };
            await context.Users.AddAsync(user);
            await context.SaveChangesAsync();

            bool nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new RestrictedGovernanceModeMiddleware(next);

            var httpContext = new DefaultHttpContext();
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, "User")
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

            // Set endpoint WITH [AllowSuspendedAccess]
            var metadata = new EndpointMetadataCollection(new AllowSuspendedAccessAttribute());
            var endpoint = new Endpoint(c => Task.CompletedTask, metadata, "SubmitAppeal");
            httpContext.SetEndpoint(endpoint);

            // Act
            await middleware.InvokeAsync(httpContext, context);

            // Assert
            Assert.True(nextCalled);
            Assert.Equal(StatusCodes.Status200OK, httpContext.Response.StatusCode);
        }

        [Fact]
        public async Task RestrictedGovernanceModeMiddleware_Allows_Admin_Always()
        {
            // Arrange
            var context = GetInMemoryDbContext();
            bool nextCalled = false;
            RequestDelegate next = (ctx) =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new RestrictedGovernanceModeMiddleware(next);

            var httpContext = new DefaultHttpContext();
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, "Admin")
            };
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

            var endpoint = new Endpoint(c => Task.CompletedTask, new EndpointMetadataCollection(), "AnyOperationalEndpoint");
            httpContext.SetEndpoint(endpoint);

            // Act
            await middleware.InvokeAsync(httpContext, context);

            // Assert
            Assert.True(nextCalled);
        }

        #endregion
    }
}
