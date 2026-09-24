using System;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;

namespace LifeLink.Tests
{
    /// <summary>Stands in for a doctor approving a donor's screening report: the donor is Verified and holds a reserved slot.</summary>
    internal static class TestReservations
    {
        public static async Task ReserveAsync(AppDbContext context, params Guid[] acceptanceIds)
        {
            foreach (var id in acceptanceIds)
            {
                var acceptance = await context.Acceptances.FindAsync(id) ?? throw new InvalidOperationException("Unknown acceptance");
                var request = await context.BloodRequests.FindAsync(acceptance.BloodRequestId) ?? throw new InvalidOperationException("Unknown request");
                acceptance.Status = AcceptanceStatus.Verified;
                request.ReservedUnits++;
            }
            await context.SaveChangesAsync();
        }
    }
}
