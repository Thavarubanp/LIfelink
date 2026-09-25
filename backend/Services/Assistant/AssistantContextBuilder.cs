using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using LifeLink.Services.Common;
using LifeLink.Services.Transfer;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Assistant
{
    /// <summary>
    /// Builds the only user data the AI assistant ever receives: a small, role-scoped snapshot of the caller's own
    /// records. Each role has a fixed list of fields; nothing about other users, hospitals, doctors, complaints,
    /// appeals, requests, inventories or notifications is included (the Admin gets counts only).
    /// </summary>
    public class AssistantContextBuilder
    {
        private readonly AppDbContext _context;

        public AssistantContextBuilder(AppDbContext context)
        {
            _context = context;
        }

        public static string RoleOf(IReadOnlyCollection<string> roles) =>
            roles.Contains("Admin") ? "Admin" :
            roles.Contains("HospitalStaff") ? "HospitalStaff" :
            roles.Contains("Doctor") ? "Doctor" : "Donor";

        public async Task<Dictionary<string, object?>> BuildAsync(User user, string role, string? email)
        {
            return role switch
            {
                "Admin" => await AdminAsync(),
                "HospitalStaff" => await HospitalAsync(email),
                "Doctor" => await DoctorAsync(user.UserId),
                _ => await DonorAsync(user)
            };
        }

        private async Task<Dictionary<string, object?>> DonorAsync(User user)
        {
            var now = DateTime.UtcNow;
            var acceptances = await _context.Acceptances
                .Where(a => a.DonorUserId == user.UserId)
                .OrderByDescending(a => a.AcceptedAt)
                .Take(5)
                .ToListAsync();
            var requestIds = acceptances.Select(a => a.BloodRequestId).ToList();
            var ownRequests = await _context.BloodRequests
                .Where(r => r.PatientUserId == user.UserId)
                .OrderByDescending(r => r.CreatedAt)
                .Take(5)
                .ToListAsync();
            var allRequestIds = requestIds.Concat(ownRequests.Select(r => r.BloodRequestId)).Distinct().ToList();
            var requests = await _context.BloodRequests.Where(r => allRequestIds.Contains(r.BloodRequestId)).ToDictionaryAsync(r => r.BloodRequestId);
            var hospitalIds = requests.Values.Select(r => r.HospitalId).Distinct().ToList();
            var hospitalNames = await _context.Hospitals.Where(h => hospitalIds.Contains(h.HospitalId)).ToDictionaryAsync(h => h.HospitalId, h => h.Name);
            var acceptanceIds = acceptances.Select(a => a.AcceptanceId).ToList();
            var reports = await _context.DonorVerifications.Where(v => acceptanceIds.Contains(v.AcceptanceId)).ToListAsync();

            string? HospitalOf(Guid requestId) => requests.TryGetValue(requestId, out var r) ? hospitalNames.GetValueOrDefault(r.HospitalId) : null;

            return new Dictionary<string, object?>
            {
                ["role"] = "Donor",
                ["profile"] = new Dictionary<string, object?>
                {
                    ["firstName"] = user.FirstName,
                    ["bloodGroup"] = user.BloodGroup,
                    ["bloodGroupConfirmed"] = await DonorEligibility.IsBloodGroupConfirmedAsync(_context, user.UserId),
                    ["lastDonationDate"] = user.LastDonationDate?.ToString("yyyy-MM-dd"),
                    ["nextEligibleDate"] = DonorEligibility.NextEligibleDate(user)?.ToString("yyyy-MM-dd"),
                    ["canDonateNow"] = DonorEligibility.IsIntervalSatisfied(user, now)
                },
                ["acceptances"] = acceptances.Select(a =>
                {
                    var latest = reports.Where(v => v.AcceptanceId == a.AcceptanceId).OrderByDescending(v => v.ReportVersion).FirstOrDefault();
                    requests.TryGetValue(a.BloodRequestId, out var r);
                    return new Dictionary<string, object?>
                    {
                        ["acceptanceId"] = a.AcceptanceId,
                        ["status"] = a.Status.ToString(),
                        ["requestBloodGroup"] = r?.BloodGroup,
                        ["requestStatus"] = r?.Status.ToString(),
                        ["hospitalName"] = HospitalOf(a.BloodRequestId),
                        ["rejectionReason"] = a.RejectionReason,
                        ["latestReport"] = latest == null ? null : new Dictionary<string, object?>
                        {
                            ["version"] = latest.ReportVersion,
                            ["status"] = latest.Status.ToString(),
                            ["approvalNotes"] = latest.Status == VerificationStatus.Approved ? latest.Notes : null,
                            ["rejectionReason"] = latest.Status == VerificationStatus.Rejected ? latest.Notes : null
                        }
                    };
                }).ToList(),
                ["requests"] = ownRequests.Select(r => new Dictionary<string, object?>
                {
                    ["status"] = r.Status.ToString(),
                    ["bloodGroup"] = r.BloodGroup,
                    ["unitsRequired"] = r.UnitsRequired,
                    ["fulfilledUnits"] = r.FulfilledUnits,
                    ["reservedUnits"] = r.ReservedUnits,
                    ["hospitalName"] = hospitalNames.GetValueOrDefault(r.HospitalId),
                    ["rejectionReason"] = r.RejectionReason
                }).ToList(),
                ["notifications"] = await NotificationSummaryAsync(n => n.UserId == user.UserId)
            };
        }

        private async Task<Dictionary<string, object?>> DoctorAsync(Guid userId)
        {
            var doctor = await _context.Doctors.Include(d => d.Hospital).FirstOrDefaultAsync(d => d.UserId == userId);
            if (doctor == null) return new Dictionary<string, object?> { ["role"] = "Doctor" };

            var hospitalRequestIds = _context.BloodRequests.Where(r => r.HospitalId == doctor.HospitalId).Select(r => r.BloodRequestId);
            var hospitalAcceptances = _context.Acceptances.Where(a => hospitalRequestIds.Contains(a.BloodRequestId));
            var pending = await _context.DonorVerifications
                .Where(v => v.Status == VerificationStatus.Pending && hospitalAcceptances.Any(a => a.AcceptanceId == v.AcceptanceId))
                .OrderBy(v => v.CreatedAt)
                .ToListAsync();
            var acceptanceRequest = await hospitalAcceptances.ToDictionaryAsync(a => a.AcceptanceId, a => a.BloodRequestId);
            var requestGroups = await _context.BloodRequests.Where(r => r.HospitalId == doctor.HospitalId).ToDictionaryAsync(r => r.BloodRequestId, r => r.BloodGroup);

            var assignedIds = _context.BloodRequestVerifications
                .Where(v => v.DoctorId == doctor.DoctorId && v.Status == VerificationStatus.Pending)
                .Select(v => v.BloodRequestId);
            var awaitingDecision = await _context.BloodRequests
                .Where(r => assignedIds.Contains(r.BloodRequestId) && r.Status == BloodRequestStatus.Verified)
                .Select(r => new { r.BloodGroup, r.Priority, r.UnitsRequired, r.CreatedAt })
                .ToListAsync();

            return new Dictionary<string, object?>
            {
                ["role"] = "Doctor",
                ["hospitalName"] = doctor.Hospital?.Name,
                ["pendingReports"] = new Dictionary<string, object?>
                {
                    ["total"] = pending.Count,
                    ["assignedToMe"] = pending.Count(v => v.DoctorId == doctor.DoctorId),
                    ["items"] = pending.Take(5).Select(v => new Dictionary<string, object?>
                    {
                        ["requestBloodGroup"] = acceptanceRequest.TryGetValue(v.AcceptanceId, out var rid) ? requestGroups.GetValueOrDefault(rid) : null,
                        ["riskLevel"] = RiskLevel(v.ReportJson),
                        ["submittedAt"] = v.CreatedAt.ToString("yyyy-MM-dd"),
                        ["assignedToMe"] = v.DoctorId == doctor.DoctorId
                    }).ToList()
                },
                ["approvedAwaitingDonation"] = await hospitalAcceptances.CountAsync(a => a.Status == AcceptanceStatus.Verified),
                ["assignedRequestsAwaitingDecision"] = awaitingDecision.Select(r => new Dictionary<string, object?>
                {
                    ["bloodGroup"] = r.BloodGroup, ["priority"] = r.Priority, ["unitsRequired"] = r.UnitsRequired,
                    ["createdAt"] = r.CreatedAt.ToString("yyyy-MM-dd")
                }).ToList()
            };
        }

        private async Task<Dictionary<string, object?>> HospitalAsync(string? email)
        {
            var normalized = (email ?? string.Empty).Trim().ToLower();
            var hospital = await _context.Hospitals.FirstOrDefaultAsync(h => h.Email != null && h.Email.ToLower() == normalized);
            if (hospital == null) return new Dictionary<string, object?> { ["role"] = "HospitalStaff" };

            var now = DateTime.UtcNow;
            var inventory = await _context.BloodInventories.Where(i => i.HospitalId == hospital.HospitalId).OrderBy(i => i.BloodGroup).ToListAsync();
            var packets = await _context.BloodPackets
                .Where(p => p.HospitalId == hospital.HospitalId && p.Status == BloodPacketStatus.Available)
                .Select(p => new { p.BloodGroup, p.ExpiryDate })
                .ToListAsync();
            var transfers = await _context.HospitalTransferRequests
                .Include(t => t.SenderHospital).Include(t => t.ReceiverHospital)
                .Where(t => (t.SenderHospitalId == hospital.HospitalId || t.ReceiverHospitalId == hospital.HospitalId) && t.Status == TransferRequestStatus.Pending.ToString())
                .ToListAsync();
            var hospitalRequestIds = _context.BloodRequests.Where(r => r.HospitalId == hospital.HospitalId).Select(r => r.BloodRequestId);

            return new Dictionary<string, object?>
            {
                ["role"] = "HospitalStaff",
                ["hospitalName"] = hospital.Name,
                ["expiryAlertDays"] = hospital.ExpiryAlertDays,
                ["inventory"] = inventory.Select(i =>
                {
                    var group = packets.Where(p => p.BloodGroup == i.BloodGroup).ToList();
                    return new Dictionary<string, object?>
                    {
                        ["bloodGroup"] = i.BloodGroup,
                        ["units"] = i.UnitsAvailable,
                        ["threshold"] = i.MinimumThreshold,
                        ["expiringSoon"] = group.Count(p => p.ExpiryDate <= now.AddDays(hospital.ExpiryAlertDays)),
                        ["nextExpiry"] = group.Count > 0 ? group.Min(p => p.ExpiryDate).ToString("yyyy-MM-dd") : null
                    };
                }).ToList(),
                ["pendingVerifications"] = await _context.BloodRequests.CountAsync(r => r.HospitalId == hospital.HospitalId && r.Status == BloodRequestStatus.Pending),
                ["transfers"] = new Dictionary<string, object?>
                {
                    ["incomingPending"] = transfers.Count(t => TransferRequestService.CounterpartHospitalId(t) == hospital.HospitalId),
                    ["outgoingPending"] = transfers.Count(t => TransferRequestService.CreatorHospitalId(t) == hospital.HospitalId),
                    ["items"] = transfers.Take(5).Select(t => new Dictionary<string, object?>
                    {
                        ["type"] = t.TransferType,
                        ["direction"] = TransferRequestService.CounterpartHospitalId(t) == hospital.HospitalId ? "Incoming" : "Outgoing",
                        ["bloodGroup"] = t.BloodGroup,
                        ["units"] = t.UnitsRequested,
                        ["counterpart"] = t.SenderHospitalId == hospital.HospitalId ? t.ReceiverHospital?.Name : t.SenderHospital?.Name
                    }).ToList()
                },
                ["openEmergencies"] = await _context.EmergencyRequests.CountAsync(e => e.HospitalId == hospital.HospitalId &&
                    (e.Status == EmergencyRequestStatus.Pending.ToString() || e.Status == EmergencyRequestStatus.Approved.ToString())),
                ["approvedDonorsAwaitingDonation"] = await _context.Acceptances.CountAsync(a => hospitalRequestIds.Contains(a.BloodRequestId) && a.Status == AcceptanceStatus.Verified),
                ["notifications"] = await NotificationSummaryAsync(n => n.HospitalId == hospital.HospitalId && n.UserId == null)
            };
        }

        private async Task<Dictionary<string, object?>> AdminAsync() => new()
        {
            ["role"] = "Admin",
            ["pendingHospitalRegistrations"] = await _context.Hospitals.CountAsync(LifeLink.Services.Hospitals.RegistrationThread.NeedsAdminReview),
            ["openComplaints"] = await _context.Complaints.CountAsync(c => c.Status == ComplaintStatus.OPEN || c.Status == ComplaintStatus.UNDER_REVIEW || c.Status == ComplaintStatus.AWAITING_INFORMATION),
            ["pendingAppeals"] = await _context.Appeals.CountAsync(a => a.Status == AppealStatus.PENDING)
        };

        private async Task<Dictionary<string, object?>> NotificationSummaryAsync(System.Linq.Expressions.Expression<Func<LifeLink.Entities.Notification, bool>> mine)
        {
            var query = _context.Notifications.Where(mine);
            return new Dictionary<string, object?>
            {
                ["unread"] = await query.CountAsync(n => !n.IsRead),
                ["latestTitles"] = await query.Where(n => !n.IsRead).OrderByDescending(n => n.CreatedAt).Select(n => n.Title).Take(3).ToListAsync()
            };
        }

        private static string? RiskLevel(string? reportJson)
        {
            if (string.IsNullOrWhiteSpace(reportJson)) return null;
            try
            {
                using var doc = JsonDocument.Parse(reportJson);
                return doc.RootElement.TryGetProperty("risk_level", out var risk) ? risk.GetString() : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
