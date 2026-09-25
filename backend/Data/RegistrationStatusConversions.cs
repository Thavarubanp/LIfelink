using System;
using LifeLink.Entities;

namespace LifeLink.Data
{
    /// <summary>
    /// Reads registration status text written by the earlier resubmission workflow: the retired hospital status
    /// "Resubmitted" (a hospital update waiting for the admin) reads as AwaitingAdminReview, and the old history values
    /// "Pending" and "Resubmitted" read as Submitted and HospitalReply. New values are written unchanged.
    /// </summary>
    public static class RegistrationStatusConversions
    {
        public static ApprovalStatus ParseApprovalStatus(string value) => value switch
        {
            "Resubmitted" => ApprovalStatus.AwaitingAdminReview,
            _ => Enum.Parse<ApprovalStatus>(value)
        };

        public static RegistrationEntryType ParseEntryType(string value) => value switch
        {
            "Pending" => RegistrationEntryType.Submitted,
            "Resubmitted" => RegistrationEntryType.HospitalReply,
            _ => Enum.Parse<RegistrationEntryType>(value)
        };
    }
}
