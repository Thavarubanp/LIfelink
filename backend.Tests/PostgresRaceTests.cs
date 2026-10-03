using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Acceptances;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Admin;
using LifeLink.Services.Auth;
using LifeLink.Services.BloodCompatibility;
using LifeLink.Services.BloodRequests;
using LifeLink.Services.Common;
using LifeLink.Services.Notification;
using LifeLink.Services.Planning;
using LifeLink.Services.Verification;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using Xunit;

namespace LifeLink.Tests
{
    /// <summary>Runs only when LIFELINK_PG_TESTS names an appsettings file with ConnectionStrings:DefaultConnection.</summary>
    public sealed class PostgresFactAttribute : FactAttribute
    {
        public PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LIFELINK_PG_TESTS")))
            {
                Skip = "PostgreSQL checks run only with LIFELINK_PG_TESTS set (the path of an appsettings file).";
            }
        }
    }

    /// <summary>
    /// The rules InMemory cannot check, on a real PostgreSQL database with the migrations applied: a losing save is
    /// rolled back completely, the partial unique indexes, the foreign key, conditional session updates and the sweep
    /// lock. Everything runs inside one transaction that is always rolled back, so nothing is kept. No packets are
    /// created (the tracking-number sequence is not transactional) and emails go to a mock.
    /// </summary>
    public class PostgresRaceTests
    {
        private static string ConnectionString()
        {
            var path = Environment.GetEnvironmentVariable("LIFELINK_PG_TESTS")!;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
        }

        private sealed class Scope : IAsyncDisposable
        {
            public NpgsqlConnection Connection = null!;
            public NpgsqlTransaction Transaction = null!;

            public AppDbContext NewContext()
            {
                var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(Connection).Options);
                context.Database.UseTransaction(Transaction);
                return context;
            }

            public async ValueTask DisposeAsync()
            {
                await Transaction.RollbackAsync(); // nothing written by a test is kept
                await Connection.DisposeAsync();
            }
        }

        private static async Task<Scope> OpenAsync()
        {
            var connection = new NpgsqlConnection(ConnectionString());
            await connection.OpenAsync();
            return new Scope { Connection = connection, Transaction = await connection.BeginTransactionAsync() };
        }

        private sealed class Rows
        {
            public Hospital Hospital = null!;
            public User Patient = null!;
            public User Donor = null!;
            public User Admin = null!;
            public Doctor Doctor = null!;
        }

        private static async Task<Rows> SeedAsync(AppDbContext db)
        {
            var tag = Guid.NewGuid().ToString("N")[..10];
            var rows = new Rows
            {
                Hospital = new Hospital { HospitalId = Guid.NewGuid(), Name = $"PG test {tag}", Email = $"h-{tag}@test.invalid", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved, PacketShelfLifeDays = 35, ExpiryAlertDays = 7 },
                Patient = new User { UserId = Guid.NewGuid(), FirstName = "Pat", LastName = "Test", Email = $"p-{tag}@test.invalid", PasswordHash = "x" },
                Donor = new User { UserId = Guid.NewGuid(), FirstName = "Don", LastName = "Test", Email = $"d-{tag}@test.invalid", PasswordHash = "x", BloodGroup = "O+" },
                Admin = new User { UserId = Guid.NewGuid(), FirstName = "Ad", LastName = "Test", Email = $"a-{tag}@test.invalid", PasswordHash = "x" }
            };
            var doctorUser = new User { UserId = Guid.NewGuid(), FirstName = "Doc", LastName = "Test", Email = $"doc-{tag}@test.invalid", PasswordHash = "x" };
            rows.Doctor = new Doctor { DoctorId = Guid.NewGuid(), UserId = doctorUser.UserId, HospitalId = rows.Hospital.HospitalId, FirstName = "Doc", LastName = "Test", Email = $"doc-{tag}@test.invalid", LicenseNumber = $"SLMC-{tag}".ToUpperInvariant(), IsActive = true, MustChangePassword = false };
            db.Hospitals.Add(rows.Hospital);
            db.Users.AddRange(rows.Patient, rows.Donor, rows.Admin, doctorUser);
            db.Doctors.Add(rows.Doctor);
            await db.SaveChangesAsync();
            return rows;
        }

        private static BloodRequest NewRequest(Rows r, BloodRequestStatus status = BloodRequestStatus.Approved) => new()
        {
            BloodRequestId = Guid.NewGuid(), PatientUserId = r.Patient.UserId, HospitalId = r.Hospital.HospitalId, BloodGroup = "O+", UnitsRequired = 2,
            Reason = "PG test", Priority = "High", Status = status, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, ExpiryDate = DateTime.UtcNow.AddDays(5)
        };

        private static AcceptanceService Acceptances(AppDbContext db) => new(db, new BloodCompatibilityService(), new Mock<IPlanningAgentService>().Object);

        [PostgresFact]
        public async Task A_Losing_Accept_Is_Rolled_Back_Completely()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            var request = NewRequest(r);
            var setup = scope.NewContext();
            setup.BloodRequests.Add(request);
            await setup.SaveChangesAsync();

            var donorSide = scope.NewContext();
            await donorSide.BloodRequests.FindAsync(request.BloodRequestId); // the donor read the request first
            await new BloodRequestService(scope.NewContext()).CancelRequestAsync(request.BloodRequestId, r.Patient.UserId);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Acceptances(donorSide).AcceptRequestAsync(r.Donor.UserId,
                new CreateAcceptanceDto { BloodRequestId = request.BloodRequestId, DonorBloodGroup = "O+" }));

            var check = scope.NewContext();
            Assert.False(await check.Acceptances.AnyAsync(a => a.BloodRequestId == request.BloodRequestId)); // no stranded acceptance
        }

        [PostgresFact]
        public async Task A_Losing_Doctor_Approval_Leaves_The_Withdrawn_Donor_And_The_Report_As_They_Were()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            var request = NewRequest(r);
            var acceptance = new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = request.BloodRequestId, DonorUserId = r.Donor.UserId, Status = AcceptanceStatus.ScreeningCompleted, AcceptedAt = DateTime.UtcNow };
            var report = new DonorVerification { DonorVerificationId = Guid.NewGuid(), AcceptanceId = acceptance.AcceptanceId, DoctorId = r.Doctor.DoctorId, Status = VerificationStatus.Pending, ReportVersion = 1, ReportJson = "{}", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var setup = scope.NewContext();
            setup.BloodRequests.Add(request);
            setup.Acceptances.Add(acceptance);
            setup.DonorVerifications.Add(report);
            await setup.SaveChangesAsync();

            var doctorSide = scope.NewContext();
            await doctorSide.DonorVerifications.FindAsync(report.DonorVerificationId);
            await doctorSide.Acceptances.FindAsync(acceptance.AcceptanceId);
            await doctorSide.BloodRequests.FindAsync(request.BloodRequestId);
            await Acceptances(scope.NewContext()).CancelAcceptanceAsync(acceptance.AcceptanceId, r.Donor.UserId);

            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                new VerificationService(doctorSide, new Mock<INotificationAgentService>().Object).ApproveDonorVerificationAsync(report.DonorVerificationId, r.Doctor.UserId!.Value, null));

            var check = scope.NewContext();
            Assert.Equal(AcceptanceStatus.Cancelled, (await check.Acceptances.AsNoTracking().SingleAsync(a => a.AcceptanceId == acceptance.AcceptanceId)).Status);
            Assert.Equal(VerificationStatus.Closed, (await check.DonorVerifications.AsNoTracking().SingleAsync(v => v.DonorVerificationId == report.DonorVerificationId)).Status);
            Assert.Equal(0, (await check.BloodRequests.AsNoTracking().SingleAsync(b => b.BloodRequestId == request.BloodRequestId)).ReservedUnits);
        }

        [PostgresFact]
        public async Task Hospital_Approval_Clicked_Twice_Keeps_One_Decision()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            var pending = new Hospital { HospitalId = Guid.NewGuid(), Name = "PG pending", Email = $"pending-{Guid.NewGuid():N}@test.invalid", ApprovalStatus = ApprovalStatus.Pending, PacketShelfLifeDays = 35, ExpiryAlertDays = 7 };
            var setup = scope.NewContext();
            setup.Hospitals.Add(pending);
            await setup.SaveChangesAsync();
            var email = new Mock<IEmailService>();
            AdminService Admin(AppDbContext db) => new(db, new AdminNotificationService(db, email.Object, NullLogger<AdminNotificationService>.Instance));

            var secondClick = scope.NewContext();
            await secondClick.Hospitals.FindAsync(pending.HospitalId);
            await Admin(scope.NewContext()).ApproveHospitalAsync(pending.HospitalId, r.Admin.UserId);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Admin(secondClick).ApproveHospitalAsync(pending.HospitalId, r.Admin.UserId));

            var check = scope.NewContext();
            Assert.Equal(1, await check.HospitalApprovalHistories.CountAsync(h => h.HospitalId == pending.HospitalId && h.Status == RegistrationEntryType.Approved));
            email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Once);
        }

        [PostgresFact]
        public async Task Only_One_Active_Request_Per_Creator_Hospital_And_Group_Even_Under_Double_Submit()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            var first = scope.NewContext();
            first.BloodRequests.Add(NewRequest(r, BloodRequestStatus.Pending));
            await first.SaveChangesAsync();

            var second = scope.NewContext();
            second.BloodRequests.Add(NewRequest(r, BloodRequestStatus.Pending));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Equal(DatabaseConflicts.DuplicateActiveRequestMessage, DatabaseConflicts.ConflictMessage(ex));

            // A closed request never blocks a new one (same rule as before)
            var closed = scope.NewContext();
            closed.BloodRequests.Add(NewRequest(r, BloodRequestStatus.Cancelled));
            await closed.SaveChangesAsync();
        }

        [PostgresFact]
        public async Task Only_One_Open_Appeal_Per_Account_And_Per_Hospital()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            Appeal Open(Guid? userId, Guid? hospitalId, AppealStatus status = AppealStatus.PENDING) =>
                new() { AppealId = Guid.NewGuid(), UserId = userId, HospitalId = hospitalId, Reason = "PG test", Status = status, SubmittedAt = DateTime.UtcNow };

            var setup = scope.NewContext();
            setup.Appeals.AddRange(Open(r.Donor.UserId, null), Open(r.Donor.UserId, null, AppealStatus.CLOSED), Open(null, r.Hospital.HospitalId, AppealStatus.REJECTED));
            await setup.SaveChangesAsync(); // one open per account / hospital, closed ones do not count

            var user = scope.NewContext();
            user.Appeals.Add(Open(r.Donor.UserId, null, AppealStatus.REJECTED));
            var userEx = await Assert.ThrowsAsync<DbUpdateException>(() => user.SaveChangesAsync());
            Assert.Equal(DatabaseConflicts.DuplicateOpenAppealMessage, DatabaseConflicts.ConflictMessage(userEx));

            var hospital = scope.NewContext();
            hospital.Appeals.Add(Open(null, r.Hospital.HospitalId));
            var hospitalEx = await Assert.ThrowsAsync<DbUpdateException>(() => hospital.SaveChangesAsync());
            Assert.Equal(DatabaseConflicts.DuplicateOpenAppealMessage, DatabaseConflicts.ConflictMessage(hospitalEx));
        }

        [PostgresFact]
        public async Task An_Acceptance_For_A_Request_Deleted_At_The_Same_Moment_Is_Refused_By_The_Foreign_Key()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            var db = scope.NewContext();
            db.Acceptances.Add(new Acceptance { AcceptanceId = Guid.NewGuid(), BloodRequestId = Guid.NewGuid(), DonorUserId = r.Donor.UserId, AcceptedAt = DateTime.UtcNow });

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ((PostgresException)ex.InnerException!).SqlState);
            Assert.Equal(ConflictException.DefaultMessage, DatabaseConflicts.ConflictMessage(ex));
        }

        [PostgresFact]
        public async Task The_Same_Idempotency_Key_Saved_Twice_At_Once_Fails_On_The_Primary_Key()
        {
            await using var scope = await OpenAsync();
            var key = $"pg-test:{Guid.NewGuid():N}";
            var first = scope.NewContext();
            first.IdempotencyKeys.Add(new IdempotencyKey { Key = key, Endpoint = "POST test", CreatedAt = DateTime.UtcNow });
            await first.SaveChangesAsync();

            var second = scope.NewContext();
            second.IdempotencyKeys.Add(new IdempotencyKey { Key = key, Endpoint = "POST test", CreatedAt = DateTime.UtcNow });
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            Assert.Equal(DatabaseConflicts.AlreadySubmittedMessage, DatabaseConflicts.ConflictMessage(ex));
        }

        [PostgresFact]
        public async Task Session_Updates_Are_Conditional_And_A_Timed_Out_Session_Cannot_Be_Revived()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            var settings = new SessionSettings { IdleTimeout = TimeSpan.FromMinutes(10), WarningPeriod = TimeSpan.FromMinutes(1) };
            var sessions = new SessionService(scope.NewContext(), settings);

            var active = await sessions.StartAsync(r.Donor.UserId);
            var idle = await sessions.StartAsync(r.Donor.UserId);
            var raw = scope.NewContext();
            await raw.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserSessions\" SET \"LastActivityAt\" = {DateTime.UtcNow.AddMinutes(-9)} WHERE \"SessionId\" = {active.SessionId}");
            await raw.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"UserSessions\" SET \"LastActivityAt\" = {DateTime.UtcNow.AddMinutes(-11)} WHERE \"SessionId\" = {idle.SessionId}");

            Assert.Equal(SessionState.Active, await sessions.CheckAsync(active.SessionId, r.Donor.UserId, recordActivity: true));
            Assert.Equal(SessionState.Idle, await sessions.CheckAsync(idle.SessionId, r.Donor.UserId, recordActivity: true));

            var check = scope.NewContext();
            Assert.True((await check.UserSessions.AsNoTracking().SingleAsync(s => s.SessionId == active.SessionId)).LastActivityAt > DateTime.UtcNow.AddMinutes(-1));
            var ended = await check.UserSessions.AsNoTracking().SingleAsync(s => s.SessionId == idle.SessionId);
            Assert.Equal(SessionEndReasons.Idle, ended.EndReason);
            Assert.True(ended.LastActivityAt < DateTime.UtcNow.AddMinutes(-10)); // the late request did not move it on

            await sessions.EndAsync(active.SessionId, SessionEndReasons.SignedOut);
            Assert.Equal(SessionState.Ended, await sessions.CheckAsync(active.SessionId, r.Donor.UserId, recordActivity: true));
        }

        [PostgresFact]
        public async Task Doctor_Email_And_Per_Hospital_Slmc_Are_Unique_Only_Among_Doctors_Who_Were_Not_Removed()
        {
            await using var scope = await OpenAsync();
            var r = await SeedAsync(scope.NewContext());
            var tag = Guid.NewGuid().ToString("N")[..10];
            var other = new Hospital { HospitalId = Guid.NewGuid(), Name = $"PG test other {tag}", Email = $"h2-{tag}@test.invalid", IsVerified = true, ApprovalStatus = ApprovalStatus.Approved, PacketShelfLifeDays = 35, ExpiryAlertDays = 7 };
            var setup = scope.NewContext();
            setup.Hospitals.Add(other);
            await setup.SaveChangesAsync();
            Doctor NewDoctor(Guid hospitalId, string email, string slmc) => new()
            {
                DoctorId = Guid.NewGuid(), HospitalId = hospitalId, FirstName = "Doc", LastName = "Test", Email = email, LicenseNumber = slmc, IsActive = true, MustChangePassword = false
            };

            // Same SLMC at the same hospital: refused by IX_Doctors_HospitalId_LicenseNumber
            var sameHospital = scope.NewContext();
            sameHospital.Doctors.Add(NewDoctor(r.Hospital.HospitalId, $"doc2-{tag}@test.invalid", r.Doctor.LicenseNumber));
            var slmcEx = await Assert.ThrowsAsync<DbUpdateException>(() => sameHospital.SaveChangesAsync());
            Assert.True(SlmcUniquenessHelper.IsSlmcUniqueViolation(slmcEx));

            // Same SLMC at another hospital (a separate account with another email): allowed
            var otherHospital = scope.NewContext();
            otherHospital.Doctors.Add(NewDoctor(other.HospitalId, $"doc3-{tag}@test.invalid", r.Doctor.LicenseNumber));
            await otherHospital.SaveChangesAsync();

            // Same email twice among doctors who were not removed: refused by IX_Doctors_Email
            var sameEmail = scope.NewContext();
            sameEmail.Doctors.Add(NewDoctor(other.HospitalId, r.Doctor.Email, $"SLMC-X-{tag}".ToUpperInvariant()));
            await Assert.ThrowsAsync<DbUpdateException>(() => sameEmail.SaveChangesAsync());

            // Once the doctor is removed (soft delete), the same email and SLMC can be used again at the same hospital
            var remove = scope.NewContext();
            var existing = await remove.Doctors.SingleAsync(d => d.DoctorId == r.Doctor.DoctorId);
            existing.DeletedAt = DateTime.UtcNow;
            existing.IsActive = false;
            await remove.SaveChangesAsync();
            var reuse = scope.NewContext();
            reuse.Doctors.Add(NewDoctor(r.Hospital.HospitalId, r.Doctor.Email, r.Doctor.LicenseNumber));
            await reuse.SaveChangesAsync();
            Assert.Equal(2, await scope.NewContext().Doctors.CountAsync(d => d.HospitalId == r.Hospital.HospitalId)); // the removed row is kept
        }

        [PostgresFact]
        public async Task Only_One_Backend_Instance_Holds_The_Sweep_Lease_Until_It_Expires()
        {
            await using var scope = await OpenAsync();
            var db = scope.NewContext();
            var job = $"pg-test-{Guid.NewGuid():N}"; // a test-only lease, so the real sweep lease is never touched

            Assert.True(await BackgroundJobLeases.TryAcquireAsync(db, job, "instance-1", TimeSpan.FromMinutes(7)));
            Assert.False(await BackgroundJobLeases.TryAcquireAsync(db, job, "instance-2", TimeSpan.FromMinutes(7))); // skips the round
            Assert.True(await BackgroundJobLeases.TryAcquireAsync(db, job, "instance-1", TimeSpan.FromMinutes(7)));  // the holder renews

            // The holder stopped: once its lease has expired, another instance takes over
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJobLeases\" SET \"LeasedUntil\" = {DateTime.UtcNow.AddMinutes(-1)} WHERE \"Name\" = {job}");
            Assert.True(await BackgroundJobLeases.TryAcquireAsync(db, job, "instance-2", TimeSpan.FromMinutes(7)));
            Assert.False(await BackgroundJobLeases.TryAcquireAsync(db, job, "instance-1", TimeSpan.FromMinutes(7)));
        }

        [PostgresFact]
        public async Task The_Inventory_Analysis_Lock_Admits_One_Run_Then_A_Cooldown_Then_Frees_Itself()
        {
            await using var scope = await OpenAsync();
            var db = scope.NewContext();
            var lease = $"pg-test-analysis-{Guid.NewGuid():N}"; // test-only name: the real "InventoryAnalysis" lock is never touched
            var first = Guid.NewGuid();

            Assert.True(await LifeLink.Services.Inventory.InventoryAnalysisService.TryTakeLockAsync(db, first, lease));
            Assert.False(await LifeLink.Services.Inventory.InventoryAnalysisService.TryTakeLockAsync(db, Guid.NewGuid(), lease)); // running
            Assert.Equal("Running", (await LifeLink.Services.Inventory.InventoryAnalysisService.ReadLockAsync(db, lease)).State);

            // Only the run holding the lock can turn it into the cooldown
            Assert.False(await LifeLink.Services.Inventory.InventoryAnalysisService.StartCooldownAsync(db, Guid.NewGuid(), lease));
            Assert.True(await LifeLink.Services.Inventory.InventoryAnalysisService.StartCooldownAsync(db, first, lease));
            var (state, endsAt) = await LifeLink.Services.Inventory.InventoryAnalysisService.ReadLockAsync(db, lease);
            Assert.Equal("Cooldown", state);
            Assert.InRange((endsAt!.Value - DateTime.UtcNow).TotalSeconds, 100, 121);
            Assert.False(await LifeLink.Services.Inventory.InventoryAnalysisService.TryTakeLockAsync(db, Guid.NewGuid(), lease)); // cooling down

            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BackgroundJobLeases\" SET \"LeasedUntil\" = {DateTime.UtcNow.AddSeconds(-1)} WHERE \"Name\" = {lease}");
            Assert.True(await LifeLink.Services.Inventory.InventoryAnalysisService.TryTakeLockAsync(db, Guid.NewGuid(), lease));
        }
    }
}
