using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using LifeLink.Common;
using LifeLink.DTOs.Hospitals;
using LifeLink.Entities;

namespace LifeLink.Services.Hospitals
{
    /// <summary>
    /// A hospital registration is one continuous conversation with strict turns:
    ///   Pending --admin reject--> Rejected --hospital reply--> AwaitingAdminReview
    ///   AwaitingAdminReview --admin comment (more information)--> Rejected, --admin reject--> Rejected, --approve--> Approved
    /// The hospital replies only while Rejected, so it can never send two replies without an admin action in between.
    /// </summary>
    public static class RegistrationThread
    {
        /// <summary>Registrations waiting for an admin decision. Translatable to SQL.</summary>
        public static readonly Expression<Func<Hospital, bool>> NeedsAdminReview = h =>
            h.ApprovalStatus == ApprovalStatus.Pending || h.ApprovalStatus == ApprovalStatus.AwaitingAdminReview;

        public static List<HospitalApprovalHistory> Ordered(Hospital hospital) =>
            (hospital.ApprovalHistories ?? new List<HospitalApprovalHistory>()).OrderBy(e => e.Timestamp).ToList();

        public static bool IsAwaitingAdminReview(Hospital hospital) => hospital.ApprovalStatus == ApprovalStatus.AwaitingAdminReview;

        public static bool IsAdminEntry(RegistrationEntryType type) =>
            type is RegistrationEntryType.Rejected or RegistrationEntryType.AdminComment or RegistrationEntryType.Approved;

        /// <summary>
        /// Both sides see the same entries; only admins see which admin acted. Empty (zero-byte) files are left out.
        /// </summary>
        public static List<HospitalApprovalHistoryDto> ToDtos(Hospital hospital, bool includeAdminIdentity) =>
            Ordered(hospital).Select(e =>
            {
                var hasFile = AttachmentRules.HasContent(e.ReportDocumentUrl);
                return new HospitalApprovalHistoryDto
                {
                    Id = e.Id,
                    Type = e.Status.ToString(),
                    FromAdmin = IsAdminEntry(e.Status),
                    Timestamp = e.Timestamp,
                    AdminId = includeAdminIdentity ? e.AdminId : null,
                    AdminName = includeAdminIdentity ? (e.AdminName ?? e.Admin?.Email) : null,
                    Message = e.Comments,
                    ChangedFields = e.ChangedFields,
                    AttachmentName = hasFile ? (e.ReportDocumentName ?? "attachment") : null,
                    AttachmentUrl = hasFile ? e.ReportDocumentUrl : null
                };
            }).ToList();

        /// <summary>
        /// Refuses an admin action when the hospital replied after the latest entry the admin had seen, so an admin
        /// never approves or answers corrections they have not opened. No check when the client sends no entry ID.
        /// </summary>
        public static void EnsureNoUnseenHospitalReply(Hospital hospital, Guid? lastSeenEntryId)
        {
            if (lastSeenEntryId == null) return;
            var entries = Ordered(hospital);
            var seenIndex = entries.FindIndex(e => e.Id == lastSeenEntryId.Value);
            if (entries.Skip(seenIndex + 1).Any(e => e.Status == RegistrationEntryType.HospitalReply))
            {
                throw new ConflictException("The hospital sent a new reply after you opened this registration. Review it before continuing.");
            }
        }
    }
}
