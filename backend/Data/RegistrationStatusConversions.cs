using System;
using LifeLink.Entities;

namespace LifeLink.Data
{
    /// <summary>
    /// Reads registration status text written by the earlier resubmission workflow: the retired hospital status
    /// "Resubmitted" reads as Rejected, and the old history values "Pending" and "Resubmitted" read as Submitted and
    /// HospitalReply. New values are written unchanged, so this only matters until old rows are converted.
    /// </summary>
    public static class RegistrationStatusConversions
    {
        public static ApprovalStatus ParseApprovalStatus(string value) => value switch
        {
            "Resubmitted" => ApprovalStatus.Rejected,
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
