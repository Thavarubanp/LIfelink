using System;

namespace LifeLink.Services.Inventory
{
    /// <summary>
    /// Collected-date rules for packets entered by hospital staff. "Today" is the Sri Lanka date (UTC+05:30, no daylight
    /// saving), so a packet entered just after local midnight is not treated as a future date by a UTC server.
    /// </summary>
    public static class PacketDateRules
    {
        private static readonly TimeSpan SriLankaOffset = new(5, 30, 0);

        public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(utcNow + SriLankaOffset);

        /// <summary>Validates a collected date and returns it as the stored UTC date (midnight).</summary>
        public static DateTime Validate(DateOnly? collectionDate, int shelfLifeDays, DateTime utcNow)
        {
            if (collectionDate == null)
            {
                throw new InvalidOperationException("Collected date is required.");
            }

            var today = Today(utcNow);
            if (collectionDate.Value > today)
            {
                throw new InvalidOperationException("Collected date cannot be in the future.");
            }

            if (collectionDate.Value.AddDays(shelfLifeDays) <= today)
            {
                throw new InvalidOperationException(
                    $"A packet collected on {collectionDate.Value:yyyy-MM-dd} would already be expired (shelf life {shelfLifeDays} days).");
            }

            return collectionDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        }
    }
}
