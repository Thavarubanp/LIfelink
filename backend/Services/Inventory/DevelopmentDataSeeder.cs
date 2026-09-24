using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Data;
using LifeLink.Entities;
using Microsoft.EntityFrameworkCore;

namespace LifeLink.Services.Inventory
{
    /// <summary>
    /// Development-only demo stock, run explicitly with: dotnet run -- --seed-demo-data (ASPNETCORE_ENVIRONMENT=Development).
    /// Creates blood group categories and "Seed" packets for approved hospitals that have none yet, with collection
    /// dates spread over the last month so some packets fall inside the expiry alert window. Every packet is audited
    /// with a SEEDED ledger row. Never runs in Production and is never reachable through the API.
    /// </summary>
    public static class DevelopmentDataSeeder
    {
        private static readonly string[] Groups = { "O+", "O-", "A+", "A-", "B+", "B-", "AB+", "AB-" };

        public static async Task<int> SeedDemoStockAsync(AppDbContext context)
        {
            var hospitals = await context.Hospitals.Where(h => h.IsVerified && !h.IsSuspended).ToListAsync();
            var created = 0;
            var random = new Random(2026);

            foreach (var hospital in hospitals)
            {
                if (await context.BloodPackets.AnyAsync(p => p.HospitalId == hospital.HospitalId && p.Source == BloodPacketSource.Seed))
                {
                    continue; // already seeded
                }

                foreach (var group in Groups)
                {
                    var inventory = await InventoryLedger.GetOrCreateInventoryAsync(context, hospital.HospitalId, group);
                    if (inventory.MinimumThreshold == 0) inventory.MinimumThreshold = 5;

                    var count = random.Next(2, 12);
                    for (var i = 0; i < count; i++)
                    {
                        var collected = DateTime.UtcNow.Date.AddDays(-random.Next(0, hospital.PacketShelfLifeDays - 1));
                        created += (await InventoryLedger.AddCollectedPacketsAsync(context, hospital.HospitalId, group, 1, collected,
                            BloodPacketSource.Seed, null, TransactionType.Seeded, "Development demo stock (seeder)", null)).Count;
                    }
                }
            }

            await context.SaveChangesAsync();
            return created;
        }
    }
}
