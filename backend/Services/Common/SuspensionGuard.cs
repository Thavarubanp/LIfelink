using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.Entities;
using LifeLink.Services.Notification;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Common
{
    /// <summary>
    /// Admin suspension of a blood request or a transfer (Phase 3B). While suspended, every action on it is refused with
    /// 409, except (owner's Q7) a donor withdrawing their acceptance and the creator deleting the request. A suspend or lift
    /// changes the row's concurrency token, so an action that changes the same row at the same moment fails with 409.
    /// </summary>
    public static class SuspensionGuard
    {
        public const string RequestSuspendedMessage =
            "This blood request is temporarily suspended by the administrator. No action can be taken on it until the suspension is lifted.";
        public const string TransferSuspendedMessage =
            "This transfer is temporarily suspended by the administrator. No action can be taken on it until the suspension is lifted.";

        public static bool IsSuspended(BloodRequest request) => request.AdminSuspendedAt != null;
        public static bool IsSuspended(HospitalTransferRequest transfer) => transfer.AdminSuspendedAt != null;

        public static void EnsureNotSuspended(BloodRequest request)
        {
            if (IsSuspended(request)) throw new ConflictException(RequestSuspendedMessage);
        }

        public static void EnsureNotSuspended(HospitalTransferRequest transfer)
        {
            if (IsSuspended(transfer)) throw new ConflictException(TransferSuspendedMessage);
        }

        /// <summary>Loads the request (tracked) and refuses the action when it is suspended.</summary>
        public static async Task EnsureRequestNotSuspendedAsync(AppDbContext context, Guid bloodRequestId)
        {
            var request = await context.BloodRequests.FindAsync(bloodRequestId);
            if (request != null) EnsureNotSuspended(request);
        }

        /// <summary>Stages an in-app notification to every admin (for example: a suspended request was deleted by its creator).</summary>
        public static async Task NotifyAdminsAsync(AppDbContext context, string type, string title, string message)
        {
            var adminIds = await context.UserRoles.Where(ur => ur.Role.Name == "Admin").Select(ur => ur.UserId).Distinct().ToListAsync();
            foreach (var adminId in adminIds)
            {
                await context.Notifications.AddAsync(NotificationFactory.ForUser(adminId, "Admin", type, title, message));
            }
        }
    }
}
