import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import '../../core/utils/format.dart';

/// InventoryAnalysisRunDto.
class AnalysisRun {
  const AnalysisRun({
    required this.runId,
    required this.startedAt,
    required this.finishedAt,
    required this.trigger,
    required this.hospitalName,
    required this.status,
    required this.lowStockAlerts,
    required this.expiringAlerts,
    required this.skippedDuplicates,
  });

  factory AnalysisRun.fromJson(Map<String, dynamic> j) => AnalysisRun(
        runId: str(j['runId']),
        startedAt: parseDate(j['startedAt']),
        finishedAt: parseDate(j['finishedAt']),
        trigger: str(j['trigger']),
        hospitalName: j['hospitalName']?.toString(),
        status: str(j['status']),
        lowStockAlerts: intOf(j['lowStockAlerts']),
        expiringAlerts: intOf(j['expiringAlerts']),
        skippedDuplicates: intOf(j['skippedDuplicates']),
      );

  final String runId;
  final DateTime? startedAt;
  final DateTime? finishedAt;
  final String trigger; // Manual | Scheduled
  final String? hospitalName;
  final String status; // Running | Completed | CompletedRuleBased | Failed | Interrupted
  final int lowStockAlerts;
  final int expiringAlerts;
  final int skippedDuplicates;

  /// The web panel's summary of a run's result.
  String get summary {
    switch (status) {
      case 'Running':
        return 'running…';
      case 'Failed':
        return 'failed';
      case 'Interrupted':
        return 'interrupted (it did not finish)';
    }
    final skipped = skippedDuplicates > 0 ? ', $skippedDuplicates repeated alert(s) skipped' : '';
    final rules = status == 'CompletedRuleBased' ? ' (rule-based: the AI agents were unavailable)' : '';
    return '$lowStockAlerts low-stock, ${Fmt.plural(expiringAlerts, 'expiring-soon alert')} sent$skipped$rules';
  }

  String get triggerLabel => trigger == 'Manual' ? 'manual by ${hospitalName ?? 'a hospital'}' : 'scheduled';
}

/// InventoryAnalysisStatusDto: the same answer for every hospital (times in UTC).
class AnalysisStatus {
  const AnalysisStatus({
    required this.state,
    required this.cooldownEndsAt,
    required this.serverNow,
    required this.lastRun,
    required this.nextScheduledAt,
    required this.scheduleEnabled,
    required this.cooldownSeconds,
  });

  factory AnalysisStatus.fromJson(Map<String, dynamic> j) => AnalysisStatus(
        state: str(j['state'], 'Idle'),
        cooldownEndsAt: parseDate(j['cooldownEndsAt']),
        serverNow: parseDate(j['serverNow']) ?? DateTime.now().toUtc(),
        lastRun: j['lastRun'] is Map ? AnalysisRun.fromJson(Map<String, dynamic>.from(j['lastRun'] as Map)) : null,
        nextScheduledAt: parseDate(j['nextScheduledAt']),
        scheduleEnabled: boolOf(j['scheduleEnabled']),
        cooldownSeconds: intOf(j['cooldownSeconds']),
      );

  final String state; // Idle | Running | Cooldown
  final DateTime? cooldownEndsAt;
  final DateTime serverNow;
  final AnalysisRun? lastRun;
  final DateTime? nextScheduledAt;
  final bool scheduleEnabled;
  final int cooldownSeconds;

  /// Cooldown left, measured on the server's clock ([serverOffset] = server time minus phone time).
  Duration cooldownLeft(DateTime phoneNow, Duration serverOffset) {
    if (state != 'Cooldown' || cooldownEndsAt == null) return Duration.zero;
    final left = cooldownEndsAt!.difference(phoneNow.toUtc().add(serverOffset));
    return left.isNegative ? Duration.zero : left;
  }

  /// "Next scheduled run in 12 min." / "due now." / "Scheduled runs are off."
  String? nextRunText(DateTime phoneNow, Duration serverOffset) {
    if (!scheduleEnabled) return 'Scheduled runs are off.';
    if (nextScheduledAt == null) return null;
    final ms = nextScheduledAt!.difference(phoneNow.toUtc().add(serverOffset)).inMilliseconds;
    final minutes = (ms / 60000).ceil();
    return minutes <= 0 ? 'Next scheduled run: due now.' : 'Next scheduled run in $minutes min.';
  }
}

class AnalysisRepository {
  AnalysisRepository(this._api);

  final ApiClient _api;

  /// Polled in the background: never extends the session.
  Future<AnalysisStatus> status({bool background = false}) async =>
      AnalysisStatus.fromJson(unwrapMap(await _api.get('/Inventory/analysis/status', options: apiOptions(background: background))));

  /// Runs the analysis now; 409 while running or during the 2-minute cooldown. The run can take a while.
  Future<AnalysisRun> run() async => AnalysisRun.fromJson(
      unwrapMap(await _api.post('/Inventory/analysis/run', options: apiOptions(receiveTimeout: const Duration(minutes: 3)))));
}

final analysisRepositoryProvider = Provider<AnalysisRepository>((ref) => AnalysisRepository(ref.watch(apiClientProvider)));
