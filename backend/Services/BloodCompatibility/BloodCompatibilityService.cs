using System;
using System.Collections.Generic;
using LifeLink.Common;

namespace LifeLink.Services.BloodCompatibility
{
    public class BloodCompatibilityService : IBloodCompatibilityService
    {
        private static readonly Dictionary<string, List<string>> DonorToRecipientsMatrix = new(StringComparer.OrdinalIgnoreCase)
        {
            { "O-", new List<string> { "O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+" } },
            { "O+", new List<string> { "O+", "A+", "B+", "AB+" } },
            { "A-", new List<string> { "A-", "A+", "AB-", "AB+" } },
            { "A+", new List<string> { "A+", "AB+" } },
            { "B-", new List<string> { "B-", "B+", "AB-", "AB+" } },
            { "B+", new List<string> { "B+", "AB+" } },
            { "AB-", new List<string> { "AB-", "AB+" } },
            { "AB+", new List<string> { "AB+" } }
        };

        public bool IsCompatible(string donorBloodGroup, string recipientBloodGroup)
        {
            if (!BloodValidationHelper.IsValidBloodGroup(donorBloodGroup) ||
                !BloodValidationHelper.IsValidBloodGroup(recipientBloodGroup))
            {
                return false;
            }

            var normalizedDonor = BloodValidationHelper.NormalizeBloodGroup(donorBloodGroup);
            var normalizedRecipient = BloodValidationHelper.NormalizeBloodGroup(recipientBloodGroup);

            if (DonorToRecipientsMatrix.TryGetValue(normalizedDonor, out var compatibleRecipients))
            {
                return compatibleRecipients.Contains(normalizedRecipient);
            }

            return false;
        }

        public List<string> GetCompatibleRecipients(string donorBloodGroup)
        {
            var normalizedDonor = BloodValidationHelper.NormalizeBloodGroup(donorBloodGroup);

            if (DonorToRecipientsMatrix.TryGetValue(normalizedDonor, out var recipients))
            {
                return new List<string>(recipients);
            }

            return new List<string>();
        }
    }
}
