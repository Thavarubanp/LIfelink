import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/common.dart';
import 'analysis_repository.dart';

/// Inventory analysis (agentic task + workflow status): the last run (Sri Lanka time, scheduled or manual by which
/// hospital, its result), the next scheduled run, and "Run analysis now" with a running state and the 2-minute
/// cooldown countdown on the server's clock. Refreshes every 15 s in the background (never extends the session).
class InventoryAnalysisPanel extends ConsumerStatefulWidget {
  const InventoryAnalysisPanel({super.key, this.onRunComplete, this.pollInterval = const Duration(seconds: 15)});

  final VoidCallback? onRunComplete;
  final Duration pollInterval;

  @override
  ConsumerState<InventoryAnalysisPanel> createState() => _InventoryAnalysisPanelState();
}

class _InventoryAnalysisPanelState extends ConsumerState<InventoryAnalysisPanel> {
  AnalysisStatus? _status;
  Duration _offset = Duration.zero; // server clock minus phone clock
  String? _error;
  bool _running = false;
  Timer? _poll;
  Timer? _tick;

  @override
  void initState() {
    super.initState();
    _load(background: false);
    _poll = Timer.periodic(widget.pollInterval, (_) => _load(background: true));
    _tick = Timer.periodic(const Duration(seconds: 1), (_) {
      if (mounted) setState(() {});
    });
  }

  @override
  void dispose() {
    _poll?.cancel();
    _tick?.cancel();
    super.dispose();
  }

  Future<void> _load({required bool background}) async {
    try {
      final status = await ref.read(analysisRepositoryProvider).status(background: background);
      if (!mounted) return;
      setState(() {
        _status = status;
        _offset = status.serverNow.difference(DateTime.now().toUtc());
        _error = null;
      });
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    }
  }

  Future<void> _run() async {
    setState(() => _running = true);
    try {
      final result = await ref.read(analysisRepositoryProvider).run();
      if (mounted) {
        showSnack(
          context,
          '${result.lowStockAlerts} low-stock, ${Fmt.plural(result.expiringAlerts, 'expiring-soon alert')} sent.',
          type: SnackType.success,
          title: 'Analysis complete',
        );
      }
      widget.onRunComplete?.call();
    } catch (e) {
      if (mounted) showErrorSnack(context, e, title: 'Analysis not run');
    } finally {
      if (mounted) setState(() => _running = false);
      await _load(background: false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final status = _status;
    final now = DateTime.now();
    final isRunning = _running || status?.state == 'Running';
    final cooldown = status?.cooldownLeft(now, _offset) ?? Duration.zero;
    final inCooldown = !isRunning && cooldown > Duration.zero;
    final last = status?.lastRun;
    final next = status?.nextRunText(now, _offset);

    return SectionCard(
      key: const Key('analysis-panel'),
      title: 'Inventory analysis',
      icon: Icons.insights,
      trailing: TextButton(
        onPressed: () => context.push(AppRoutes.hospitalRecommendations),
        child: const Text('Recommendations'),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text(
            'Checks every hospital\'s stock: a hospital below its threshold is alerted with the hospitals holding that exact '
            'blood group, and those hospitals are asked to help; packets expiring soon are flagged.',
            style: TextStyle(fontSize: 12, color: AppColors.slate500),
          ),
          const SizedBox(height: 10),
          if (status == null && _error != null)
            Text(_error!, style: const TextStyle(color: AppColors.rose600))
          else if (status == null)
            const Row(children: [
              SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)),
              SizedBox(width: 8),
              Text('Loading…'),
            ])
          else ...[
            if (isRunning)
              const Row(
                key: Key('analysis-running'),
                children: [
                  SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2, color: AppColors.red600)),
                  SizedBox(width: 8),
                  Text('Analysis running…', style: TextStyle(fontWeight: FontWeight.w700, color: AppColors.red600)),
                ],
              )
            else
              Text.rich(
                key: const Key('analysis-last-run'),
                last == null
                    ? const TextSpan(text: 'No analysis has run yet.')
                    : TextSpan(children: [
                        const TextSpan(text: 'Last analysis: ', style: TextStyle(fontWeight: FontWeight.w700)),
                        TextSpan(text: '${Fmt.sriLankaDateTime(last.startedAt)} (${last.triggerLabel}) – ${last.summary}'),
                      ]),
              ),
            if (next != null) ...[
              const SizedBox(height: 4),
              Row(children: [
                const Icon(Icons.schedule, size: 14, color: AppColors.slate500),
                const SizedBox(width: 4),
                Expanded(child: Text(next, style: const TextStyle(fontSize: 12, color: AppColors.slate500))),
              ]),
            ],
          ],
          const SizedBox(height: 12),
          FilledButton.icon(
            key: const Key('analysis-run'),
            onPressed: status == null || isRunning || inCooldown ? null : _run,
            icon: isRunning
                ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                : const Icon(Icons.play_arrow),
            label: Text(isRunning
                ? 'Analysis running…'
                : inCooldown
                    ? 'Available again in ${Fmt.countdown(cooldown)}'
                    : 'Run analysis now'),
          ),
        ],
      ),
    );
  }
}
