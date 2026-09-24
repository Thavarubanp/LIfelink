using System;
using System.Collections.Generic;

namespace LifeLink.Services.Notification
{
    /// <summary>A donor the backend selected as eligible for an alert; the agent re-checks these fields.</summary>
    public record DonorCandidate(Guid UserId, string FullName, string BloodGroup, string Location, DateTime? LastDonationDate, DateTime? DateOfBirth)
    {
        public Dictionary<string, object?> ToAgentPayload() => new()
        {
            ["user_id"] = UserId.ToString(),
            ["full_name"] = FullName,
            ["blood_group"] = BloodGroup,
            ["location"] = Location,
            ["last_donation_date"] = LastDonationDate?.ToString("yyyy-MM-dd"),
            ["date_of_birth"] = DateOfBirth?.ToString("yyyy-MM-dd"),
            ["account_status"] = "Active",
            ["is_suspended"] = false,
            ["is_blocked"] = false
        };
    }
}
