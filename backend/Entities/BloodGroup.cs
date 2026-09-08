using System;
using System.Collections.Generic;

namespace LifeLink.Entities
{
    public static class BloodGroup
    {
        public const string APositive = "A+";
        public const string ANegative = "A-";
        public const string BPositive = "B+";
        public const string BNegative = "B-";
        public const string ABPositive = "AB+";
        public const string ABNegative = "AB-";
        public const string OPositive = "O+";
        public const string ONegative = "O-";

        public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
        {
            APositive, ANegative, BPositive, BNegative, ABPositive, ABNegative, OPositive, ONegative
        };

        public static bool IsValid(string? bloodGroup)
        {
            return !string.IsNullOrWhiteSpace(bloodGroup) && All.Contains(bloodGroup);
        }
    }
}
