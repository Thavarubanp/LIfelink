using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.DTOs.Admin;
using LifeLink.Entities;
using LifeLink.Services.Acceptances;
using LifeLink.Services.Common;
using LifeLink.Services.Notification;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Admin
{
    public interface IAdminOversightActionsService
    {
        Task SuspendRequestAsync(Guid requestId, Guid adminUserId, string? reason);
        Task LiftRequestAsync(Guid requestId, Guid adminUserId);
        Task SuspendTransferAsync(Guid transferId, Guid adminUserId, string? reason);
        Task LiftTransferAsync(Guid transferId, Guid adminUserId);
        Task SendMessageAsync(Guid adminUserId, AdminMessageDto dto);
    }

    /// <summary>
    /// Admin actions of Phase 3B: suspend / lift a blood request or a transfer (the admin cannot edit or delete them),
    /// and one-way "Message from Administrator" to one user or one hospital. Each action is one save: the change, its
    /// notifications and its activity log entries commit together. A suspend or lift changes the row's concurrency token,
    /// so an action on the same row at the same moment fails with 409.
    /// </summary>
    public class AdminOversightActionsService : IAdminOversightActionsService
    {
        public const string MessageType = "AdminMessage";
        public const string MessageTitlePrefix = "Message from Administrator";

        private static readonly BloodRequestStatus[] OpenRequestStatuses =
            { BloodRequestStatus.Pending, BloodRequestStatus.Verified, BloodRequestStatus.Approved };

        private readonly AppDbContext _context;

        public AdminOversightActionsService(AppDbContext context)
        {
            _context = context;
        }

        public async Task SuspendRequestAsync(Guid requestId, Guid adminUserId, string? reason)
        {
            var message = RequireReason(reason);
            var request = await _context.BloodRequests.FindAsync(requestId)
                          ?? throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");
            if (SuspensionGuard.IsSuspended(request))
            {
                throw new ConflictException("This blood request is already suspended.");
            }
            if (!OpenRequestStatuses.Contains(request.Status))
            {
                throw new InvalidOperationException($"Only open requests (pending, verified or approved) can be suspended. This request is {request.Status}.");
            }

            var now = DateTime.UtcNow;
            request.AdminSuspendedAt = now;
            request.AdminSuspendedByUserId = adminUserId;
            request.AdminSuspensionReason = message;
            request.UpdatedAt = now;

            var label = await RequestLabelAsync(request);
            await NotifyRequestPartiesAsync(request, "BloodRequestSuspended", "Blood Request Suspended",
                $"The administrator has temporarily suspended {label}. No action can be taken on it until the suspension is lifted. Reason: {message}",
                $"The administrator has temporarily suspended {label}. You can still withdraw. Reason: {message}");
            await ActivityLogger.AddAsync(_context, adminUserId, "BloodRequest.Suspended", ActivityLogger.Types.BloodRequest, request.BloodRequestId,
                $"Suspended {label}: {message}", request.HospitalId, request.PatientUserId);
            await SaveAsync();
        }

        public async Task LiftRequestAsync(Guid requestId, Guid adminUserId)
        {
            var request = await _context.BloodRequests.FindAsync(requestId)
                          ?? throw new KeyNotFoundException($"Blood request with ID {requestId} was not found.");
            if (!SuspensionGuard.IsSuspended(request))
            {
                throw new ConflictException("This blood request is not suspended.");
            }

            request.AdminSuspendedAt = null;
            request.AdminSuspendedByUserId = null;
            request.AdminSuspensionReason = null;
            request.UpdatedAt = DateTime.UtcNow;

            var label = await RequestLabelAsync(request);
            // A deleted request stays deleted: only its creator's side is told; nobody can act on it anyway
            if (request.Status != BloodRequestStatus.Deleted)
            {
                const string text = "The administrator has lifted the suspension of {0}. It can be handled normally again.";
                await NotifyRequestPartiesAsync(request, "BloodRequestSuspensionLifted", "Blood Request Suspension Lifted",
                    string.Format(text, label), string.Format(text, label));
            }
            await ActivityLogger.AddAsync(_context, adminUserId, "BloodRequest.SuspensionLifted", ActivityLogger.Types.BloodRequest, request.BloodRequestId,
                $"Lifted the suspension of {label}.", request.HospitalId, request.PatientUserId);
            await SaveAsync();
        }

        public async Task SuspendTransferAsync(Guid transferId, Guid adminUserId, string? reason)
        {
            var message = RequireReason(reason);
            var transfer = await _context.HospitalTransferRequests.FindAsync(transferId)
                           ?? throw new KeyNotFoundException($"Transfer with ID {transferId} was not found.");
            if (SuspensionGuard.IsSuspended(transfer))
            {
                throw new ConflictException("This transfer is already suspended.");
            }
            if (transfer.Status != TransferRequestStatus.Pending.ToString())
            {
                throw new InvalidOperationException($"Only pending transfers can be suspended. This transfer is {transfer.Status}.");
            }

            var now = DateTime.UtcNow;
            transfer.AdminSuspendedAt = now;
            transfer.AdminSuspendedByUserId = adminUserId;
            transfer.AdminSuspensionReason = message;
            transfer.UpdatedAt = now;

            var label = TransferLabel(transfer);
            await NotifyTransferHospitalsAsync(transfer, "TransferSuspended", "Transfer Suspended",
                $"The administrator has temporarily suspended {label}. It cannot be accepted, rejected or withdrawn until the suspension is lifted. Reason: {message}");
            await LogForBothHospitalsAsync(transfer, adminUserId, "Transfer.Suspended", $"Suspended {label}: {message}");
            await SaveAsync();
        }

        public async Task LiftTransferAsync(Guid transferId, Guid adminUserId)
        {
            var transfer = await _context.HospitalTransferRequests.FindAsync(transferId)
                           ?? throw new KeyNotFoundException($"Transfer with ID {transferId} was not found.");
            if (!SuspensionGuard.IsSuspended(transfer))
            {
                throw new ConflictException("This transfer is not suspended.");
            }

            transfer.AdminSuspendedAt = null;
            transfer.AdminSuspendedByUserId = null;
            transfer.AdminSuspensionReason = null;
            transfer.UpdatedAt = DateTime.UtcNow;

            var label = TransferLabel(transfer);
            await NotifyTransferHospitalsAsync(transfer, "TransferSuspensionLifted", "Transfer Suspension Lifted",
                $"The administrator has lifted the suspension of {label}. It can be handled normally again.");
            await LogForBothHospitalsAsync(transfer, adminUserId, "Transfer.SuspensionLifted", $"Lifted the suspension of {label}.");
            await SaveAsync();
        }

        /// <summary>
        /// One-way message to ONE user (donor/patient) or ONE hospital. Doctors are reached through their hospital, and
        /// admins cannot be messaged. Nobody can reply; to contact the admin, users and hospitals file a complaint.
        /// A suspended account receives it too and can read it once reinstated.
        /// </summary>
        public async Task SendMessageAsync(Guid adminUserId, AdminMessageDto dto)
        {
            var subject = dto.Subject?.Trim() ?? string.Empty;
            var body = dto.Message?.Trim() ?? string.Empty;
            if (subject.Length is < 3 or > 120)
            {
                throw new InvalidOperationException("The subject must be between 3 and 120 characters.");
            }
            if (body.Length is < 5 or > 2000)
            {
                throw new InvalidOperationException("The message must be between 5 and 2000 characters.");
            }
            if (dto.UserId.HasValue == dto.HospitalId.HasValue)
            {
                throw new InvalidOperationException("Choose exactly one recipient: one user or one hospital.");
            }

            var title = $"{MessageTitlePrefix}: {subject}";
            if (dto.UserId.HasValue)
            {
                var user = await _context.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                               .FirstOrDefaultAsync(u => u.UserId == dto.UserId.Value)
                           ?? throw new KeyNotFoundException("User not found.");
                if (user.AccountStatus == AccountStatus.Deleted)
                {
                    throw new InvalidOperationException("This account was deleted.");
                }
                var roles = user.UserRoles.Select(ur => ur.Role.Name).ToList();
                if (roles.Contains("Doctor"))
                {
                    throw new InvalidOperationException("Doctors cannot be messaged directly. Send the message to their hospital instead.");
                }
                if (roles.Contains("Admin"))
                {
                    throw new InvalidOperationException("Administrators cannot be messaged.");
                }
                if (roles.Contains("HospitalStaff"))
                {
                    throw new InvalidOperationException("This is a hospital account. Send the message from the hospital's profile instead.");
                }

                await _context.Notifications.AddAsync(NotificationFactory.ForUser(user.UserId, "User", MessageType, title, body));
                await ActivityLogger.AddAsync(_context, adminUserId, "Admin.MessageSent", ActivityLogger.Types.Account, user.UserId,
                    $"Message from Administrator: \"{subject}\".", subjectUserId: user.UserId);
            }
            else
            {
                var hospital = await _context.Hospitals.FindAsync(dto.HospitalId!.Value)
                               ?? throw new KeyNotFoundException("Hospital not found.");
                await _context.Notifications.AddAsync(NotificationFactory.ForHospital(hospital.HospitalId, MessageType, title, body));
                await ActivityLogger.AddAsync(_context, adminUserId, "Admin.MessageSent", ActivityLogger.Types.Hospital, hospital.HospitalId,
                    $"Message from Administrator: \"{subject}\".", hospital.HospitalId);
            }
            await _context.SaveChangesAsync();
        }

        // Creator (or its hospital when a hospital created it), the request's hospital, the assigned doctor, and the active
        // donors / donating hospitals. Acceptors get their own text (they can still withdraw).
        private async Task NotifyRequestPartiesAsync(BloodRequest request, string type, string title, string partyText, string acceptorText)
        {
            var hospitalIds = new HashSet<Guid> { request.HospitalId };
            var userIds = new HashSet<Guid>();

            var createdByHospital = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == request.PatientUserId && ur.Role.Name == "HospitalStaff");
            var createdByAdmin = await _context.UserRoles
                .AnyAsync(ur => ur.UserId == request.PatientUserId && ur.Role.Name == "Admin");
            if (!createdByHospital && !createdByAdmin)
            {
                userIds.Add(request.PatientUserId);
            }

            var assignedDoctorId = await _context.BloodRequestVerifications
                .Where(v => v.BloodRequestId == request.BloodRequestId && v.DoctorId != null && v.Status != VerificationStatus.Closed)
                .OrderByDescending(v => v.UpdatedAt)
                .Select(v => v.DoctorId)
                .FirstOrDefaultAsync();
            var doctorUserId = await NotificationFactory.DoctorUserIdAsync(_context, assignedDoctorId);

            foreach (var hospitalId in hospitalIds)
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForHospital(hospitalId, type, title, partyText));
            }
            foreach (var userId in userIds)
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForUser(userId, "User", type, title, partyText));
            }
            if (doctorUserId.HasValue)
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForUser(doctorUserId.Value, "Doctor", type, title, partyText));
            }

            var acceptances = await _context.Acceptances
                .Where(a => a.BloodRequestId == request.BloodRequestId && AcceptanceClosure.ActiveStatuses.Contains(a.Status))
                .Select(a => new { a.DonorUserId, a.DonorHospitalId })
                .ToListAsync();
            foreach (var hospitalId in acceptances.Where(a => a.DonorHospitalId != null).Select(a => a.DonorHospitalId!.Value)
                         .Where(id => !hospitalIds.Contains(id)).Distinct())
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForHospital(hospitalId, type, title, acceptorText));
            }
            foreach (var donorId in acceptances.Where(a => a.DonorHospitalId == null).Select(a => a.DonorUserId)
                         .Where(id => !userIds.Contains(id)).Distinct())
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForUser(donorId, "Donor", type, title, acceptorText));
            }
        }

        private async Task NotifyTransferHospitalsAsync(HospitalTransferRequest transfer, string type, string title, string text)
        {
            foreach (var hospitalId in new[] { transfer.SenderHospitalId, transfer.ReceiverHospitalId }.Distinct())
            {
                await _context.Notifications.AddAsync(NotificationFactory.ForHospital(hospitalId, type, title, text));
            }
        }

        private async Task LogForBothHospitalsAsync(HospitalTransferRequest transfer, Guid adminUserId, string action, string summary)
        {
            foreach (var hospitalId in new[] { transfer.SenderHospitalId, transfer.ReceiverHospitalId }.Distinct())
            {
                await ActivityLogger.AddAsync(_context, adminUserId, action, ActivityLogger.Types.Transfer, transfer.TransferRequestId, summary, hospitalId);
            }
        }

        private async Task<string> RequestLabelAsync(BloodRequest request)
        {
            var hospitalName = await _context.Hospitals.Where(h => h.HospitalId == request.HospitalId).Select(h => h.Name).FirstOrDefaultAsync();
            return $"blood request #{NotificationFactory.ShortId(request.BloodRequestId)} ({request.BloodGroup}, {request.UnitsRequired} unit(s) at {hospitalName ?? "the hospital"})";
        }

        private static string TransferLabel(HospitalTransferRequest t) =>
            $"transfer #{NotificationFactory.ShortId(t.TransferRequestId)} ({t.UnitsRequested} x {t.BloodGroup})";

        private static string RequireReason(string? reason)
        {
            var message = reason?.Trim();
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new InvalidOperationException("A reason for the suspension is required.");
            }
            if (message.Length > 500)
            {
                throw new InvalidOperationException("The reason cannot exceed 500 characters.");
            }
            return message;
        }

        // A suspend or lift racing another action on the same row: the later save fails with 409
        private async Task SaveAsync()
        {
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConflictException();
            }
        }
    }
}
