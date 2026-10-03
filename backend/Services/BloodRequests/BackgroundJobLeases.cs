using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.BloodRequests
{
    /// <summary>
    /// Lease for a background job when several backend instances share one database: only the holder runs it.
    /// Taking or renewing is one INSERT ... ON CONFLICT DO UPDATE ... WHERE statement (atomic, and safe through a
    /// transaction-mode connection pooler, unlike a session-level advisory lock).
    /// </summary>
    public static class BackgroundJobLeases
    {
        public const string SweepJob = "BackgroundSweep";

        /// <summary>True when this holder has (or just renewed, or took over an expired) lease until now + duration.</summary>
        public static async Task<bool> TryAcquireAsync(AppDbContext context, string name, string holder, TimeSpan duration)
        {
            if (!context.Database.IsRelational())
            {
                return true; // in-memory test databases: a single instance
            }

            var now = DateTime.UtcNow;
            var until = now + duration;
            var rows = await context.Database.SqlQuery<string>($"""
                INSERT INTO "BackgroundJobLeases" ("Name", "Holder", "LeasedUntil") VALUES ({name}, {holder}, {until})
                ON CONFLICT ("Name") DO UPDATE SET "Holder" = EXCLUDED."Holder", "LeasedUntil" = EXCLUDED."LeasedUntil"
                WHERE "BackgroundJobLeases"."LeasedUntil" < {now} OR "BackgroundJobLeases"."Holder" = {holder}
                RETURNING "Holder" AS "Value"
                """).ToListAsync();
            return rows.Count == 1;
        }
    }
}
