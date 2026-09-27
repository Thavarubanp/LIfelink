using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Appeals
{
    /// <summary>
    /// Records an admin decision on an appeal thread (status, reviewer, time, response and the "[STATUS] response"
    /// thread message). Used when the Admin decides an appeal, and when reinstating a user or hospital from the Admin
    /// dashboard resolves its still-open appeal the same way an approved appeal does.
    /// </summary>
    public static class AppealDecisions
    {
        public const string UserReinstatedNote = "Your account was reinstated by an administrator.";
        public const string HospitalReinstatedNote = "Your hospital was reinstated by an administrator.";

        /// <summary>Appeal statuses whose thread is still open (a rejected appeal stays open for replies).</summary>
        public static readonly AppealStatus[] OpenStatuses = { AppealStatus.PENDING, AppealStatus.REJECTED };

        public static void Record(AppDbContext context, Appeal appeal, AppealStatus status, Guid adminId, string response)
        {
            appeal.Status = status;
            appeal.ReviewedByAdminId = adminId;
            appeal.ReviewedAt = DateTime.UtcNow;
            appeal.AdminResponse = response.Trim();
            context.AppealMessages.Add(new AppealMessage
            {
                MessageId = Guid.NewGuid(),
                AppealId = appeal.AppealId,
                AdminId = adminId,
                Message = $"[{status}] {response}".Trim(),
                CreatedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Marks the open appeals of a reinstated user (their own user appeals) or hospital as APPROVED with the given
        /// note, exactly like approving the appeal. Closed appeals (APPROVED, CLOSED) are left unchanged. The caller
        /// saves, so the reinstatement and the appeal update commit together.
        /// </summary>
        public static async Task<List<Appeal>> ApproveOpenAppealsOnReinstatementAsync(
            AppDbContext context, Guid? userId, Guid? hospitalId, Guid? adminId, string note)
        {
            var open = await context.Appeals
                .Where(a => OpenStatuses.Contains(a.Status) &&
                            (hospitalId != null ? a.HospitalId == hospitalId : a.UserId == userId && a.HospitalId == null))
                .ToListAsync();
            if (open.Count == 0) return open;

            if (adminId == null)
            {
                throw new InvalidOperationException("An administrator is required to resolve the open appeal while reinstating.");
            }

            foreach (var appeal in open)
            {
                Record(context, appeal, AppealStatus.APPROVED, adminId.Value, note);
            }
            return open;
        }
    }
}
