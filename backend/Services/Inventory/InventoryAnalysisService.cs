using System;
using System.Linq;
using System.Threading.Tasks;
using LifeLink.Common;
using LifeLink.Data;
using LifeLink.Entities;
using LifeLink.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LifeLink.Services.Inventory
{
    public class InventoryAnalysisRunDto
    {
        public Guid RunId { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string Trigger { get; set; } = string.Empty;
        public string? HospitalName { get; set; }      // who started a manual run
        public string Status { get; set; } = string.Empty; // Running | Completed | CompletedRuleBased | Failed | Interrupted
        public int LowStockAlerts { get; set; }
        public int ExpiringAlerts { get; set; }
        public int SkippedDuplicates { get; set; }
    }

    /// <summary>The same answer for every hospital: lock state, last run and next scheduled run (times in UTC).</summary>
    public class InventoryAnalysisStatusDto
    {
        public string State { get; set; } = "Idle";      // Idle | Running | Cooldown
        public DateTime? CooldownEndsAt { get; set; }
        public DateTime ServerNow { get; set; }
        public InventoryAnalysisRunDto? LastRun { get; set; }
        public DateTime? NextScheduledAt { get; set; }
        public bool ScheduleEnabled { get; set; }
        public int CooldownSeconds { get; set; }
    }

    /// <summary>
    /// Runs the inventory analysis (InventoryMonitor) under one global lock, the BackgroundJobLeases row "InventoryAnalysis",
    /// taken and released with single atomic statements (safe through the Neon transaction-mode pooler):
    /// - free when its LeasedUntil has passed; while running the holder is "running:&lt;runId&gt;" with a 5-minute safety expiry;
    /// - when a run ends the holder becomes "cooldown:&lt;runId&gt;" until finish + 2 minutes (global cooldown).
    /// Scheduled and manual (hospital staff) runs share the lock; a scheduled run that finds it busy is retried in the next
    /// 5-minute sweep round. Every run is recorded in InventoryAnalysisRuns, which is also where the scheduler reads its
    /// last scheduled run (Q21), so the schedule survives restarts and changes of lease holder.
    /// </summary>
    public class InventoryAnalysisService
    {
        public const string LeaseName = "InventoryAnalysis";
        public static readonly TimeSpan RunSafetyExpiry = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);
        private const string RunningPrefix = "running:";
        private const string CooldownPrefix = "cooldown:";

        private readonly AppDbContext _context;
        private readonly InventoryMonitor _monitor;
        private readonly ILogger<InventoryAnalysisService> _logger;
        private readonly TimeSpan _schedulePeriod;
        private readonly bool _scheduleEnabled;

        public InventoryAnalysisService(AppDbContext context, InventoryMonitor monitor, IConfiguration configuration, ILogger<InventoryAnalysisService> logger)
        {
            _context = context;
            _monitor = monitor;
            _logger = logger;
            _schedulePeriod = SchedulePeriod(configuration);
            _scheduleEnabled = configuration.GetValue("InventoryMonitoring:Enabled", true);
        }

        public static TimeSpan SchedulePeriod(IConfiguration configuration) =>
            TimeSpan.FromMinutes(Math.Max(5, configuration.GetValue("InventoryMonitoring:IntervalMinutes", 30)));

        /// <summary>
        /// Runs one analysis. Throws ConflictException (409) when a run is in progress or the cooldown has not ended.
        /// </summary>
        public async Task<InventoryAnalysisRunDto> RunAsync(string trigger, Guid? hospitalId = null, Guid? userId = null)
        {
            var runId = Guid.NewGuid();
            if (!await TryTakeLockAsync(_context, runId))
            {
                var status = await GetLockStateAsync();
                if (status.State == "Running")
                {
                    throw new ConflictException("Analysis is already running.");
                }
                var seconds = Math.Max(1, (int)Math.Ceiling(((status.CooldownEndsAt ?? DateTime.UtcNow) - DateTime.UtcNow).TotalSeconds));
                throw new ConflictException($"Analysis ran moments ago — try again in {seconds} s.");
            }

            var run = new InventoryAnalysisRun
            {
                RunId = runId,
                StartedAt = DateTime.UtcNow,
                Trigger = trigger,
                TriggeredByHospitalId = hospitalId,
                TriggeredByUserId = userId,
                Status = InventoryAnalysisStatuses.Running
            };
            _context.InventoryAnalysisRuns.Add(run);
            await _context.SaveChangesAsync();

            try
            {
                _context.ChangeTracker.Clear();
                var result = await _monitor.RunInventoryCheckAsync();
                _context.ChangeTracker.Clear();
                var saved = await _context.InventoryAnalysisRuns.SingleAsync(r => r.RunId == runId);
                saved.Status = result.UsedAgents ? InventoryAnalysisStatuses.Completed : InventoryAnalysisStatuses.CompletedRuleBased;
                saved.FinishedAt = DateTime.UtcNow;
                saved.LowStockAlerts = result.LowStockAlerts;
                saved.ExpiringAlerts = result.ExpiringAlerts;
                saved.SkippedDuplicates = result.SkippedDuplicates;
                if (trigger == InventoryAnalysisTriggers.Manual && hospitalId.HasValue)
                {
                    await ActivityLogger.AddAsync(_context, userId, "Inventory.AnalysisRun", ActivityLogger.Types.Inventory, runId,
                        $"Ran the inventory analysis: {result.LowStockAlerts} low-stock and {result.ExpiringAlerts} expiring-soon alert(s) sent" +
                        (result.SkippedDuplicates > 0 ? $", {result.SkippedDuplicates} repeated alert(s) skipped." : "."), hospitalId);
                }
                await _context.SaveChangesAsync();
                return await MapAsync(saved);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Inventory analysis run {RunId} failed.", runId);
                _context.ChangeTracker.Clear();
                var failed = await _context.InventoryAnalysisRuns.SingleAsync(r => r.RunId == runId);
                failed.Status = InventoryAnalysisStatuses.Failed;
                failed.FinishedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return await MapAsync(failed);
            }
            finally
            {
                if (!await StartCooldownAsync(_context, runId))
                {
                    _logger.LogWarning("Could not start the inventory analysis cooldown for run {RunId}; the lock frees itself at the safety expiry.", runId);
                }
            }
        }

        /// <summary>The scheduled run: due when the last scheduled run started at least one period ago (read from the table).</summary>
        public async Task<InventoryAnalysisRunDto?> RunScheduledIfDueAsync()
        {
            if (!_scheduleEnabled) return null;
            var lastScheduled = await _context.InventoryAnalysisRuns.AsNoTracking()
                .Where(r => r.Trigger == InventoryAnalysisTriggers.Scheduled)
                .OrderByDescending(r => r.StartedAt)
                .Select(r => (DateTime?)r.StartedAt)
                .FirstOrDefaultAsync();
            if (lastScheduled.HasValue && DateTime.UtcNow - lastScheduled.Value < _schedulePeriod) return null;

            try
            {
                return await RunAsync(InventoryAnalysisTriggers.Scheduled);
            }
            catch (ConflictException ex)
            {
                _logger.LogInformation("Scheduled inventory analysis skipped ({Reason}); it is retried in the next round.", ex.Message);
                return null;
            }
        }

        public async Task<InventoryAnalysisStatusDto> GetStatusAsync()
        {
            var status = await GetLockStateAsync();
            var last = await _context.InventoryAnalysisRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync();
            status.LastRun = last == null ? null : await MapAsync(last);
            status.ScheduleEnabled = _scheduleEnabled;
            if (_scheduleEnabled)
            {
                var lastScheduled = await _context.InventoryAnalysisRuns.AsNoTracking()
                    .Where(r => r.Trigger == InventoryAnalysisTriggers.Scheduled)
                    .OrderByDescending(r => r.StartedAt)
                    .Select(r => (DateTime?)r.StartedAt)
                    .FirstOrDefaultAsync();
                status.NextScheduledAt = lastScheduled.HasValue ? lastScheduled.Value + _schedulePeriod : status.ServerNow;
            }
            return status;
        }

        private async Task<InventoryAnalysisStatusDto> GetLockStateAsync()
        {
            var (state, cooldownEndsAt) = await ReadLockAsync(_context);
            return new InventoryAnalysisStatusDto
            {
                ServerNow = DateTime.UtcNow, CooldownSeconds = (int)Cooldown.TotalSeconds, State = state, CooldownEndsAt = cooldownEndsAt
            };
        }

        private async Task<InventoryAnalysisRunDto> MapAsync(InventoryAnalysisRun run)
        {
            var hospitalName = run.TriggeredByHospitalId.HasValue
                ? await _context.Hospitals.Where(h => h.HospitalId == run.TriggeredByHospitalId).Select(h => h.Name).FirstOrDefaultAsync()
                : null;
            var interrupted = run.Status == InventoryAnalysisStatuses.Running && run.FinishedAt == null &&
                              DateTime.UtcNow - run.StartedAt > RunSafetyExpiry;
            return new InventoryAnalysisRunDto
            {
                RunId = run.RunId,
                StartedAt = run.StartedAt,
                FinishedAt = run.FinishedAt,
                Trigger = run.Trigger,
                HospitalName = hospitalName,
                Status = interrupted ? "Interrupted" : run.Status,
                LowStockAlerts = run.LowStockAlerts,
                ExpiringAlerts = run.ExpiringAlerts,
                SkippedDuplicates = run.SkippedDuplicates
            };
        }

        // ---------- the lock (one atomic statement each on PostgreSQL) ----------

        /// <summary>Takes the lock for a run when it is free (no run in progress and no cooldown). One atomic statement.</summary>
        public static async Task<bool> TryTakeLockAsync(AppDbContext context, Guid runId, string leaseName = LeaseName)
        {
            var now = DateTime.UtcNow;
            var holder = RunningPrefix + runId.ToString("N");
            var until = now + RunSafetyExpiry;
            if (!context.Database.IsRelational())
            {
                // In-memory test database (single instance): same rule without the atomic statement
                var lease = await context.BackgroundJobLeases.FirstOrDefaultAsync(l => l.Name == leaseName);
                if (lease != null && lease.LeasedUntil > now) return false;
                if (lease == null) context.BackgroundJobLeases.Add(new BackgroundJobLease { Name = leaseName, Holder = holder, LeasedUntil = until });
                else { lease.Holder = holder; lease.LeasedUntil = until; }
                await context.SaveChangesAsync();
                return true;
            }

            var rows = await context.Database.SqlQuery<string>($"""
                INSERT INTO "BackgroundJobLeases" ("Name", "Holder", "LeasedUntil") VALUES ({leaseName}, {holder}, {until})
                ON CONFLICT ("Name") DO UPDATE SET "Holder" = EXCLUDED."Holder", "LeasedUntil" = EXCLUDED."LeasedUntil"
                WHERE "BackgroundJobLeases"."LeasedUntil" < {now}
                RETURNING "Holder" AS "Value"
                """).ToListAsync();
            return rows.Count == 1;
        }

        /// <summary>The run's lock becomes the 2-minute cooldown (only if this run still holds it).</summary>
        public static async Task<bool> StartCooldownAsync(AppDbContext context, Guid runId, string leaseName = LeaseName)
        {
            var running = RunningPrefix + runId.ToString("N");
            var cooling = CooldownPrefix + runId.ToString("N");
            var until = DateTime.UtcNow + Cooldown;
            try
            {
                if (!context.Database.IsRelational())
                {
                    context.ChangeTracker.Clear();
                    var lease = await context.BackgroundJobLeases.FirstOrDefaultAsync(l => l.Name == leaseName && l.Holder == running);
                    if (lease == null) return false;
                    lease.Holder = cooling;
                    lease.LeasedUntil = until;
                    await context.SaveChangesAsync();
                    return true;
                }
                var changed = await context.Database.ExecuteSqlAsync($"""
                    UPDATE "BackgroundJobLeases" SET "Holder" = {cooling}, "LeasedUntil" = {until}
                    WHERE "Name" = {leaseName} AND "Holder" = {running}
                    """);
                return changed == 1;
            }
            catch (Exception)
            {
                // The lock still frees itself at the 5-minute safety expiry
                return false;
            }
        }

        /// <summary>Lock state for a lease name (Idle / Running / Cooldown with its end).</summary>
        public static async Task<(string State, DateTime? CooldownEndsAt)> ReadLockAsync(AppDbContext context, string leaseName = LeaseName)
        {
            var now = DateTime.UtcNow;
            var lease = await context.BackgroundJobLeases.AsNoTracking().FirstOrDefaultAsync(l => l.Name == leaseName);
            if (lease == null || lease.LeasedUntil <= now) return ("Idle", null);
            if (lease.Holder.StartsWith(RunningPrefix, StringComparison.Ordinal)) return ("Running", null);
            if (lease.Holder.StartsWith(CooldownPrefix, StringComparison.Ordinal)) return ("Cooldown", lease.LeasedUntil);
            return ("Idle", null);
        }
    }
}
