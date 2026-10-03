using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using LifeLink.Services.Inventory;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Acceptances
{
    /// <summary>
    /// Ends donor acceptances without deleting anything: a reserved slot (Verified) is released, a screening report
    /// still awaiting the doctor is marked Closed, and the acceptance keeps its history with the reason.
    /// A hospital donation offer's held packets return to the hospital's inventory.
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

            if (acceptance.DonorHospitalId != null)
            {
                await InventoryLedger.ReleaseHeldPacketsAsync(context, acceptance.AcceptanceId,
                    $"Hospital donation offer closed ({finalStatus}): {reason ?? reportNote}", null);
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
            // Re-check in memory: an acceptance already changed in this unit of work (for example just approved) is skipped
            var acceptances = (await context.Acceptances
                .Where(a => a.BloodRequestId == request.BloodRequestId && statuses.Contains(a.Status))
                .ToListAsync())
                .Where(a => statuses.Contains(a.Status))
                .ToList();
            foreach (var acceptance in acceptances)
            {
                await CloseAsync(context, acceptance, request, finalStatus, reason, reportNote);
            }
            return acceptances;
        }
    }
}
