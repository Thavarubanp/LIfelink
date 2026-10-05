using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.Attention;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>Role-scoped aggregate counts for sidebar workflow badges. No record or identity data leaves this service.</summary>
    public class RoleAttentionService
    {
        private readonly AppDbContext _context;
        private readonly ICurrentUserService _currentUser;

        public RoleAttentionService(AppDbContext context, ICurrentUserService currentUser)
        {
            _context = context;
            _currentUser = currentUser;
        }

        public async Task<RoleAttentionDto> GetAsync()
        {
            var roles = _currentUser.Roles.ToList();

            // Admin retains its separate AdminAttention endpoint and polling semantics.
            if (roles.Contains("Admin")) return new RoleAttentionDto();

            if (roles.Contains("HospitalStaff"))
            {
                var hospitalId = await CallerHospitalResolver.ResolveAsync(_context, _currentUser);
                if (!hospitalId.HasValue) return new RoleAttentionDto();

                return new RoleAttentionDto
                {
                    PendingHospitalVerifications = await _context.BloodRequests.CountAsync(r =>
                        r.HospitalId == hospitalId.Value && r.Status == BloodRequestStatus.Pending),
                    PendingTransferResponses = await _context.HospitalTransferRequests.CountAsync(t =>
                        t.Status == TransferRequestStatus.Pending.ToString() &&
                        ((t.TransferType == TransferTypes.Offer && t.ReceiverHospitalId == hospitalId.Value) ||
                         (t.TransferType == TransferTypes.Request && t.SenderHospitalId == hospitalId.Value)))
                };
            }

            if (roles.Contains("Doctor") && _currentUser.UserId.HasValue)
            {
                var hospitalId = await _context.Doctors
                    .Where(d => d.UserId == _currentUser.UserId.Value)
                    .Select(d => (System.Guid?)d.HospitalId)
                    .FirstOrDefaultAsync();
                if (!hospitalId.HasValue) return new RoleAttentionDto();

                var hospitalAcceptanceIds = _context.Acceptances
                    .Where(a => a.Status == AcceptanceStatus.ScreeningCompleted &&
                        _context.BloodRequests.Any(r => r.BloodRequestId == a.BloodRequestId && r.HospitalId == hospitalId.Value))
                    .Select(a => a.AcceptanceId);

                var pendingReviews = await _context.DonorVerifications.CountAsync(v =>
                    hospitalAcceptanceIds.Contains(v.AcceptanceId) &&
                    v.Status == VerificationStatus.Pending &&
                    !_context.DonorVerifications.Any(newer => newer.AcceptanceId == v.AcceptanceId && newer.ReportVersion > v.ReportVersion));

                return new RoleAttentionDto { PendingScreeningReviews = pendingReviews };
            }

            return new RoleAttentionDto();
        }
    }
}
