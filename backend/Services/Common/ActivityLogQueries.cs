using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.DTOs.ActivityLogs;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>Read side of the activity log: who sees which entries, plus filtering by type and date and paging.</summary>
    public static class ActivityLogQueries
    {
        public const int MaxPageSize = 100;
        private static readonly TimeSpan SriLankaOffset = TimeSpan.FromHours(5.5);

        /// <summary>A user's log: what they did, and what was done to their account (for example by the Admin).</summary>
        public static IQueryable<ActivityLog> ForUser(AppDbContext context, Guid userId) =>
            context.ActivityLogs.Where(a => a.ActorUserId == userId || a.SubjectUserId == userId);

        /// <summary>A hospital's log: its staff, its doctors and admin actions on it (or on its staff account).</summary>
        public static IQueryable<ActivityLog> ForHospital(AppDbContext context, Guid hospitalId)
        {
            var staffEmail = context.Hospitals.Where(h => h.HospitalId == hospitalId).Select(h => h.Email!.ToLower());
            var staffUserIds = context.Users.Where(u => staffEmail.Contains(u.Email.ToLower())).Select(u => (Guid?)u.UserId);
            return context.ActivityLogs.Where(a => a.HospitalId == hospitalId || staffUserIds.Contains(a.SubjectUserId));
        }

        /// <summary>
        /// Filters (type, Sri Lanka date range) and pages the entries, newest first. Admin names are shown as
        /// "Administrator" to everyone except the Admin.
        /// </summary>
        public static async Task<ActivityLogPageDto> PageAsync(IQueryable<ActivityLog> query, ActivityLogQueryDto filter, bool viewerIsAdmin)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, MaxPageSize);

            if (!string.IsNullOrWhiteSpace(filter.Type))
            {
                var type = filter.Type.Trim();
                query = query.Where(a => a.EntityType == type);
            }
            if (filter.From.HasValue)
            {
                var fromUtc = DateTime.SpecifyKind(filter.From.Value.Date, DateTimeKind.Utc) - SriLankaOffset;
                query = query.Where(a => a.OccurredAt >= fromUtc);
            }
            if (filter.To.HasValue)
            {
                var toUtc = DateTime.SpecifyKind(filter.To.Value.Date.AddDays(1), DateTimeKind.Utc) - SriLankaOffset;
                query = query.Where(a => a.OccurredAt < toUtc);
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(a => a.OccurredAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new ActivityLogEntryDto
                {
                    Id = a.Id,
                    OccurredAt = a.OccurredAt,
                    ActorRole = a.ActorRole,
                    ActorName = a.ActorName,
                    Action = a.Action,
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Summary = a.Summary
                })
                .ToListAsync();

            if (!viewerIsAdmin)
            {
                foreach (var item in items.Where(i => i.ActorRole == "Admin"))
                {
                    item.ActorName = "Administrator";
                }
            }

            return new ActivityLogPageDto
            {
                Items = items,
                Total = total,
                Page = page,
                PageSize = pageSize,
                RecordedFrom = ActivityLogger.RecordedFrom,
                Types = ActivityLogger.Types.All
            };
        }
    }
}
