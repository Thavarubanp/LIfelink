using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.ActivityLogs;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// Adds non-sensitive context to an already-authorized, already-paged activity result. Resolution is deliberately
    /// action-specific: an EntityId has different meanings for different actions.
    /// </summary>
    public static class ActivityLogEnrichment
    {
        private static readonly HashSet<string> BloodRequestActions = new(StringComparer.Ordinal)
        {
            "BloodRequest.Created", "BloodRequest.Edited", "BloodRequest.Deleted", "BloodRequest.Cancelled",
            "BloodRequest.Expired", "BloodRequest.Verified", "BloodRequest.RejectedByHospital",
            "BloodRequest.ApprovedByDoctor", "BloodRequest.RejectedByDoctor", "BloodRequest.Suspended",
            "BloodRequest.SuspensionLifted", "Donation.Recorded"
        };

        private static readonly HashSet<string> AcceptanceActions = new(StringComparer.Ordinal)
        {
            "Donation.Accepted", "Donation.Withdrawn", "Donation.Released", "Donation.HospitalOffered",
            "Donation.HospitalWithdrawn", "Donation.HospitalApproved", "Donation.HospitalRejected",
            "Screening.AnswersEdited", "Screening.Reopened"
        };

        private static readonly HashSet<string> VerificationActions = new(StringComparer.Ordinal)
        {
            "Screening.Submitted", "Screening.Approved", "Screening.Rejected"
        };

        private static readonly HashSet<string> InventoryActions = new(StringComparer.Ordinal)
        {
            "Inventory.GroupAdded", "Inventory.ThresholdsChanged", "Inventory.PacketsIssued", "Inventory.GroupDeleted"
        };

        private static readonly HashSet<string> TransferActions = new(StringComparer.Ordinal)
        {
            "Transfer.OfferCreated", "Transfer.RequestCreated", "Transfer.Accepted", "Transfer.Rejected",
            "Transfer.Withdrawn", "Transfer.Suspended", "Transfer.SuspensionLifted"
        };

        private static readonly HashSet<string> EmergencyActions = new(StringComparer.Ordinal)
        {
            "Emergency.Raised", "Emergency.Approved", "Emergency.Rejected", "Emergency.Completed"
        };

        private static readonly HashSet<string> HospitalActions = new(StringComparer.Ordinal)
        {
            "Hospital.ProfileUpdated", "Hospital.Registered", "Hospital.RegistrationReply", "Hospital.Approved",
            "Hospital.RegistrationRejected", "Hospital.RegistrationComment", "Hospital.Suspended", "Hospital.Reinstated"
        };

        private static readonly HashSet<string> AccountActions = new(StringComparer.Ordinal)
        {
            "Account.Registered", "Account.Deleted", "Account.FirstPasswordChange", "Account.PasswordChanged",
            "Account.Suspended", "Account.Reinstated", "Account.Blocked", "Account.PromotedToAdmin", "Admin.MessageSent"
        };

        private static readonly HashSet<string> ComplaintActions = new(StringComparer.Ordinal)
        {
            "Complaint.Filed", "Complaint.Solved", "Complaint.Deleted", "Complaint.AdminReplied", "Complaint.Replied"
        };

        private static readonly HashSet<string> AppealActions = new(StringComparer.Ordinal)
        {
            "Appeal.Submitted", "Appeal.Replied", "Appeal.AdminReplied", "Appeal.Approved", "Appeal.Rejected",
            "Appeal.Closed", "Appeal.AccountBlocked"
        };

        private sealed record RequestInfo(Guid Id, Guid PatientUserId, Guid HospitalId, string BloodGroup, string HospitalName);
        private sealed record AcceptanceInfo(Guid Id, Guid RequestId, Guid DonorUserId, Guid? DonorHospitalId,
            Guid RequestHospitalId, string BloodGroup, string HospitalName);
        private sealed record VerificationInfo(Guid Id, Guid AcceptanceId, Guid RequestId, Guid DonorUserId,
            Guid RequestHospitalId, string BloodGroup, string HospitalName);
        private sealed record InventoryInfo(Guid Id, Guid HospitalId, string BloodGroup, string HospitalName);
        private sealed record PacketInfo(Guid Id, Guid HospitalId, Guid CreatedByHospitalId, string BloodGroup,
            string TrackingNumber, string HospitalName);
        private sealed record TransferInfo(Guid Id, Guid SenderId, Guid ReceiverId, string BloodGroup, string SenderName, string ReceiverName);
        private sealed record EmergencyInfo(Guid Id, Guid HospitalId, string BloodGroup, string HospitalName);

        public static async Task EnrichAsync(AppDbContext context, IReadOnlyCollection<ActivityLogEntryDto> items,
            ActivityLogViewerContext viewer)
        {
            if (items.Count == 0) return;

            var activityIds = items.Select(i => i.Id).ToList();
            var activityHospitals = await context.ActivityLogs.AsNoTracking()
                .Where(a => activityIds.Contains(a.Id) && a.HospitalId.HasValue)
                .ToDictionaryAsync(a => a.Id, a => a.HospitalId!.Value);

            var requestIds = Ids(items, BloodRequestActions);
            var acceptanceIds = Ids(items, AcceptanceActions);
            var verificationIds = Ids(items, VerificationActions);
            var inventoryIds = Ids(items, InventoryActions);
            var packetIds = items.Where(i => i.Action == "Inventory.PacketEdited" && i.EntityId.HasValue)
                .Select(i => i.EntityId!.Value).Distinct().ToList();
            var transferIds = Ids(items, TransferActions);
            var emergencyIds = Ids(items, EmergencyActions);
            var analysisIds = items.Where(i => i.Action == "Inventory.AnalysisRun" && i.EntityId.HasValue)
                .Select(i => i.EntityId!.Value).Distinct().ToList();

            var requests = (await context.BloodRequests.AsNoTracking()
                .Where(r => requestIds.Contains(r.BloodRequestId))
                .Join(context.Hospitals.AsNoTracking(), r => r.HospitalId, h => h.HospitalId,
                    (r, h) => new RequestInfo(r.BloodRequestId, r.PatientUserId, r.HospitalId, r.BloodGroup, h.Name))
                .ToListAsync()).ToDictionary(x => x.Id);

            var acceptances = (await context.Acceptances.AsNoTracking()
                .Where(a => acceptanceIds.Contains(a.AcceptanceId))
                .Join(context.BloodRequests.AsNoTracking(), a => a.BloodRequestId, r => r.BloodRequestId, (a, r) => new { a, r })
                .Join(context.Hospitals.AsNoTracking(), x => x.r.HospitalId, h => h.HospitalId,
                    (x, h) => new AcceptanceInfo(x.a.AcceptanceId, x.r.BloodRequestId, x.a.DonorUserId,
                        x.a.DonorHospitalId, x.r.HospitalId, x.r.BloodGroup, h.Name))
                .ToListAsync()).ToDictionary(x => x.Id);

            var verifications = (await context.DonorVerifications.AsNoTracking()
                .Where(v => verificationIds.Contains(v.DonorVerificationId))
                .Join(context.Acceptances.AsNoTracking(), v => v.AcceptanceId, a => a.AcceptanceId, (v, a) => new { v, a })
                .Join(context.BloodRequests.AsNoTracking(), x => x.a.BloodRequestId, r => r.BloodRequestId, (x, r) => new { x.v, x.a, r })
                .Join(context.Hospitals.AsNoTracking(), x => x.r.HospitalId, h => h.HospitalId,
                    (x, h) => new VerificationInfo(x.v.DonorVerificationId, x.a.AcceptanceId, x.r.BloodRequestId,
                        x.a.DonorUserId, x.r.HospitalId, x.r.BloodGroup, h.Name))
                .ToListAsync()).ToDictionary(x => x.Id);

            var inventories = (await context.BloodInventories.AsNoTracking()
                .Where(i => inventoryIds.Contains(i.InventoryId))
                .Join(context.Hospitals.AsNoTracking(), i => i.HospitalId, h => h.HospitalId,
                    (i, h) => new InventoryInfo(i.InventoryId, i.HospitalId, i.BloodGroup, h.Name))
                .ToListAsync()).ToDictionary(x => x.Id);

            var packets = (await context.BloodPackets.AsNoTracking()
                .Where(p => packetIds.Contains(p.PacketId))
                .Join(context.Hospitals.AsNoTracking(), p => p.HospitalId, h => h.HospitalId,
                    (p, h) => new PacketInfo(p.PacketId, p.HospitalId, p.CreatedByHospitalId, p.BloodGroup, p.TrackingNumber, h.Name))
                .ToListAsync()).ToDictionary(x => x.Id);

            var transfers = (await context.HospitalTransferRequests.AsNoTracking()
                .Where(t => transferIds.Contains(t.TransferRequestId))
                .Join(context.Hospitals.AsNoTracking(), t => t.SenderHospitalId, h => h.HospitalId, (t, h) => new { t, SenderName = h.Name })
                .Join(context.Hospitals.AsNoTracking(), x => x.t.ReceiverHospitalId, h => h.HospitalId,
                    (x, h) => new TransferInfo(x.t.TransferRequestId, x.t.SenderHospitalId, x.t.ReceiverHospitalId,
                        x.t.BloodGroup, x.SenderName, h.Name))
                .ToListAsync()).ToDictionary(x => x.Id);

            var emergencies = (await context.EmergencyRequests.AsNoTracking()
                .Where(e => emergencyIds.Contains(e.EmergencyRequestId))
                .Join(context.Hospitals.AsNoTracking(), e => e.HospitalId, h => h.HospitalId,
                    (e, h) => new EmergencyInfo(e.EmergencyRequestId, e.HospitalId, e.BloodGroup, h.Name))
                .ToListAsync()).ToDictionary(x => x.Id);

            var analyses = await context.InventoryAnalysisRuns.AsNoTracking()
                .Where(r => analysisIds.Contains(r.RunId))
                .ToDictionaryAsync(r => r.RunId, r => r.TriggeredByHospitalId);

            var directHospitalIds = items.Where(i => (HospitalActions.Contains(i.Action)
                    || (i.Action == "Admin.MessageSent" && i.EntityType == ActivityLogger.Types.Hospital)) && i.EntityId.HasValue)
                .Select(i => i.EntityId!.Value).Distinct().ToList();
            var doctorIds = items.Where(i => (i.Action == "Doctor.Added" || i.Action == "Doctor.Removed" || i.Action == "Profile.Updated") && i.EntityId.HasValue)
                .Select(i => i.EntityId!.Value).Distinct().ToList();
            var doctors = await context.Doctors.AsNoTracking().Where(d => doctorIds.Contains(d.DoctorId))
                .ToDictionaryAsync(d => d.DoctorId, d => d.HospitalId);
            var hospitalLookupIds = directHospitalIds.Concat(doctors.Values)
                .Concat(analyses.Values.Where(id => id.HasValue).Select(id => id!.Value))
                .Concat(activityHospitals.Values);
            if (viewer.HospitalId.HasValue) hospitalLookupIds = hospitalLookupIds.Append(viewer.HospitalId.Value);
            var allHospitalIds = hospitalLookupIds.Distinct().ToList();
            var hospitals = await context.Hospitals.AsNoTracking().Where(h => allHospitalIds.Contains(h.HospitalId))
                .ToDictionaryAsync(h => h.HospitalId, h => h.Name);

            foreach (var item in items)
            {
                if (!item.EntityId.HasValue)
                {
                    if (item.Action == "Inventory.PacketsAdded" && activityHospitals.TryGetValue(item.Id, out var packetsHospitalId)
                        && CanSeeHospital(viewer, packetsHospitalId))
                    {
                        hospitals.TryGetValue(packetsHospitalId, out var packetsHospitalName);
                        item.HospitalName = packetsHospitalName;
                    }
                    continue;
                }

                var id = item.EntityId.Value;
                if (BloodRequestActions.Contains(item.Action) && requests.TryGetValue(id, out var request) && CanSeeRequest(viewer, request))
                {
                    item.RecordReference = Reference("Blood request", id);
                    item.BloodRequestId = id;
                    item.BloodGroup = request.BloodGroup;
                    item.HospitalName = request.HospitalName;
                }
                else if (AcceptanceActions.Contains(item.Action) && acceptances.TryGetValue(id, out var acceptance) && CanSeeAcceptance(viewer, acceptance))
                {
                    item.RecordReference = Reference("Acceptance", id);
                    item.AcceptanceId = id;
                    item.BloodRequestId = acceptance.RequestId;
                    item.BloodGroup = acceptance.BloodGroup;
                    item.HospitalName = acceptance.HospitalName;
                }
                else if (VerificationActions.Contains(item.Action) && verifications.TryGetValue(id, out var verification) && CanSeeVerification(viewer, verification))
                {
                    item.RecordReference = Reference("Screening", id);
                    item.ScreeningVerificationId = id;
                    item.AcceptanceId = verification.AcceptanceId;
                    item.BloodRequestId = verification.RequestId;
                    item.BloodGroup = verification.BloodGroup;
                    item.HospitalName = verification.HospitalName;
                }
                else if (InventoryActions.Contains(item.Action) && inventories.TryGetValue(id, out var inventory) && CanSeeHospital(viewer, inventory.HospitalId))
                {
                    item.RecordReference = Reference("Inventory", id);
                    item.BloodGroup = inventory.BloodGroup;
                    item.HospitalName = inventory.HospitalName;
                }
                else if (item.Action == "Inventory.PacketEdited" && packets.TryGetValue(id, out var packet) && CanSeePacket(viewer, packet))
                {
                    item.RecordReference = Reference("Blood packet", id);
                    item.PacketTrackingNumber = packet.TrackingNumber;
                    item.BloodGroup = packet.BloodGroup;
                    item.HospitalName = packet.HospitalName;
                }
                else if (TransferActions.Contains(item.Action) && transfers.TryGetValue(id, out var transfer) && CanSeeTransfer(viewer, transfer))
                {
                    item.RecordReference = Reference("Transfer", id);
                    item.BloodGroup = transfer.BloodGroup;
                    item.TransferSourceHospitalName = transfer.SenderName;
                    item.TransferDestinationHospitalName = transfer.ReceiverName;
                }
                else if (EmergencyActions.Contains(item.Action) && emergencies.TryGetValue(id, out var emergency) && CanSeeHospital(viewer, emergency.HospitalId))
                {
                    item.RecordReference = Reference("Emergency", id);
                    item.BloodGroup = emergency.BloodGroup;
                    item.HospitalName = emergency.HospitalName;
                }
                else if (HospitalActions.Contains(item.Action) && hospitals.TryGetValue(id, out var hospitalName) && CanSeeHospital(viewer, id))
                {
                    item.RecordReference = Reference("Hospital", id);
                    item.HospitalName = hospitalName;
                }
                else if (item.Action == "Admin.MessageSent" && item.EntityType == ActivityLogger.Types.Hospital
                    && hospitals.TryGetValue(id, out var messageHospitalName) && CanSeeHospital(viewer, id))
                {
                    item.RecordReference = Reference("Hospital", id);
                    item.HospitalName = messageHospitalName;
                }
                else if ((item.Action == "Doctor.Added" || item.Action == "Doctor.Removed" || item.Action == "Profile.Updated")
                    && doctors.TryGetValue(id, out var doctorHospitalId) && CanSeeHospital(viewer, doctorHospitalId))
                {
                    item.RecordReference = Reference("Doctor", id);
                    hospitals.TryGetValue(doctorHospitalId, out var doctorHospitalName);
                    item.HospitalName = doctorHospitalName;
                }
                else if (ComplaintActions.Contains(item.Action)) item.RecordReference = Reference("Complaint", id);
                else if (AppealActions.Contains(item.Action)) item.RecordReference = Reference("Appeal", id);
                else if (AccountActions.Contains(item.Action) && item.EntityType != ActivityLogger.Types.Hospital)
                    item.RecordReference = Reference("Account", id);
                else if (item.Action == "Profile.Updated") item.RecordReference = Reference("Account", id);
                else if (item.Action == "Inventory.AnalysisRun" && analyses.TryGetValue(id, out var analysisHospitalId)
                    && (!analysisHospitalId.HasValue || CanSeeHospital(viewer, analysisHospitalId.Value)))
                {
                    item.RecordReference = Reference("Analysis run", id);
                    if (analysisHospitalId.HasValue)
                    {
                        hospitals.TryGetValue(analysisHospitalId.Value, out var analysisHospitalName);
                        item.HospitalName = analysisHospitalName;
                    }
                }
            }
        }

        private static List<Guid> Ids(IEnumerable<ActivityLogEntryDto> items, HashSet<string> actions) =>
            items.Where(i => actions.Contains(i.Action) && i.EntityId.HasValue).Select(i => i.EntityId!.Value).Distinct().ToList();

        private static string Reference(string label, Guid id) => $"{label} #{id.ToString()[..8]}";

        private static bool CanSeeHospital(ActivityLogViewerContext viewer, Guid hospitalId) =>
            viewer.IsAdmin || ((viewer.IsHospitalStaff || viewer.IsDoctor) && viewer.HospitalId == hospitalId);

        private static bool CanSeeRequest(ActivityLogViewerContext viewer, RequestInfo request) =>
            viewer.IsAdmin || (viewer.IsUser && request.PatientUserId == viewer.UserId)
            || ((viewer.IsHospitalStaff || viewer.IsDoctor) && viewer.HospitalId == request.HospitalId);

        private static bool CanSeeAcceptance(ActivityLogViewerContext viewer, AcceptanceInfo acceptance) =>
            viewer.IsAdmin || (viewer.IsUser && acceptance.DonorUserId == viewer.UserId)
            || (viewer.IsHospitalStaff && (viewer.HospitalId == acceptance.RequestHospitalId || viewer.HospitalId == acceptance.DonorHospitalId))
            || (viewer.IsDoctor && viewer.HospitalId == acceptance.RequestHospitalId);

        private static bool CanSeeVerification(ActivityLogViewerContext viewer, VerificationInfo verification) =>
            viewer.IsAdmin || (viewer.IsUser && verification.DonorUserId == viewer.UserId)
            || ((viewer.IsHospitalStaff || viewer.IsDoctor) && viewer.HospitalId == verification.RequestHospitalId);

        private static bool CanSeePacket(ActivityLogViewerContext viewer, PacketInfo packet) =>
            viewer.IsAdmin || (viewer.IsHospitalStaff && (viewer.HospitalId == packet.HospitalId || viewer.HospitalId == packet.CreatedByHospitalId));

        private static bool CanSeeTransfer(ActivityLogViewerContext viewer, TransferInfo transfer) =>
            viewer.IsAdmin || (viewer.IsHospitalStaff && (viewer.HospitalId == transfer.SenderId || viewer.HospitalId == transfer.ReceiverId));

    }
}
