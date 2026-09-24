using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Acceptances
{
    /// <summary>
    /// Ends donor acceptances without deleting anything: a reserved slot (Verified) is released, a screening report
    /// still awaiting the doctor is marked Closed, and the acceptance keeps its history with the reason.
    /// Used by withdraw, release, request deletion, expiry and fulfilment.
    /// </summary>
    public static class AcceptanceClosure
    {
        public static readonly AcceptanceStatus[] ActiveStatuses =
        {
            AcceptanceStatus.Accepted,
            AcceptanceStatus.ScreeningPending,
            AcceptanceStatus.ScreeningCompleted,
            AcceptanceStatus.Verified
        };

        public static readonly AcceptanceStatus[] InScreeningStatuses =
        {
            AcceptanceStatus.Accepted,
            AcceptanceStatus.ScreeningPending,
            AcceptanceStatus.ScreeningCompleted
        };

        public static bool IsActive(AcceptanceStatus status) => ActiveStatuses.Contains(status);

        public static async Task CloseAsync(AppDbContext context, Acceptance acceptance, BloodRequest request,
            AcceptanceStatus finalStatus, string? reason, string reportNote)
        {
            var now = DateTime.UtcNow;

            if (acceptance.Status == AcceptanceStatus.Verified)
            {
                request.ReservedUnits = Math.Max(0, request.ReservedUnits - 1);
                request.UpdatedAt = now;
            }

            var pendingReports = await context.DonorVerifications
                .Where(v => v.AcceptanceId == acceptance.AcceptanceId && v.Status == VerificationStatus.Pending)
                .ToListAsync();
            foreach (var report in pendingReports)
            {
                report.Status = VerificationStatus.Closed;
                report.Notes = reportNote;
                report.UpdatedAt = now;
            }

            acceptance.Status = finalStatus;
            acceptance.RejectionReason = reason;
            if (finalStatus == AcceptanceStatus.Cancelled)
            {
                acceptance.CancelledAt = now;
            }
        }

        /// <summary>Closes every acceptance of the request in the given statuses; returns the closed ones.</summary>
        public static async Task<List<Acceptance>> CloseAllAsync(AppDbContext context, BloodRequest request,
            IReadOnlyCollection<AcceptanceStatus> statuses, AcceptanceStatus finalStatus, string reason, string reportNote)
        {
            var acceptances = await context.Acceptances
                .Where(a => a.BloodRequestId == request.BloodRequestId && statuses.Contains(a.Status))
                .ToListAsync();
            foreach (var acceptance in acceptances)
            {
                await CloseAsync(context, acceptance, request, finalStatus, reason, reportNote);
            }
            return acceptances;
        }
    }
}
