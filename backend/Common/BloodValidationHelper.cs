using System;
using System.Collections.Generic;

namespace LifeLink.Common
{
    public static class BloodValidationHelper
    {
        private static readonly HashSet<string> AllowedBloodGroups = new(StringComparer.OrdinalIgnoreCase)
        {
            "O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+"
        };

        private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
        {
            "Normal", "High", "Critical"
        };

        public static bool IsValidBloodGroup(string? bloodGroup)
        {
            return !string.IsNullOrWhiteSpace(bloodGroup) && AllowedBloodGroups.Contains(bloodGroup.Trim());
        }

        public static string NormalizeBloodGroup(string bloodGroup)
        {
            if (!IsValidBloodGroup(bloodGroup))
            {
                throw new ArgumentException($"Invalid blood group: '{bloodGroup}'. Allowed values are O-, O+, A-, A+, B-, B+, AB-, AB+.");
            }
            return bloodGroup.Trim().ToUpperInvariant();
        }

        public static bool IsValidPriority(string? priority)
        {
            return !string.IsNullOrWhiteSpace(priority) && AllowedPriorities.Contains(priority.Trim());
        }

        public static string NormalizePriority(string priority)
        {
            if (!IsValidPriority(priority))
            {
                throw new ArgumentException($"Invalid priority: '{priority}'. Allowed values are Normal, High, Critical.");
            }

            var trimmed = priority.Trim();
            if (trimmed.Equals("normal", StringComparison.OrdinalIgnoreCase)) return "Normal";
            if (trimmed.Equals("high", StringComparison.OrdinalIgnoreCase)) return "High";
            if (trimmed.Equals("critical", StringComparison.OrdinalIgnoreCase)) return "Critical";
            return trimmed;
        }

        public static bool IsValidUnits(int units)
        {
            return units >= 1 && units <= 10;
        }
    }
}
