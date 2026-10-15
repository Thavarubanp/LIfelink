import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/routing/routes.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../blood_requests/blood_request_models.dart';
import '../home/shell_page_header.dart';
import '../attention/tab_attention_controller.dart';
import 'screening_answers_sheet.dart';
import 'screening_repository.dart';

class MyAcceptancesScreen extends ConsumerStatefulWidget {
  const MyAcceptancesScreen({
    super.key,
    this.browsePath = AppRoutes.donorRequests,
    this.screeningPathPrefix = AppRoutes.donorAcceptances,
  });

  final String browsePath;
  final String screeningPathPrefix;

  @override
  ConsumerState<MyAcceptancesScreen> createState() =>
      _MyAcceptancesScreenState();
}

class _MyAcceptancesScreenState extends ConsumerState<MyAcceptancesScreen> {
  String? _withdrawingId;

  String _screeningPath(String acceptanceId) =>
      '${widget.screeningPathPrefix}/$acceptanceId/screening';

  Future<void> _withdraw(DonorAcceptance a) async {
    if (_withdrawingId != null) return;
    final yes = await showDialog<bool>(
      context: context,
      builder: (_) => AlertDialog(
        title: const Text('Withdraw from donation?'),
        content: Text(
          a.status == 'Verified'
              ? 'Your reserved slot will be released for another donor.'
              : 'You can accept another request afterwards.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Withdraw'),
          ),
        ],
      ),
    );
    if (yes != true || !mounted) return;
    setState(() => _withdrawingId = a.id);
    try {
      await ref.read(screeningRepositoryProvider).withdraw(a.id);
      ref.invalidate(myAcceptancesProvider);
      ref.read(tabAttentionProvider.notifier).refresh();
      if (mounted) {
        showSnack(context, 'You have withdrawn from this donation.');
      }
    } catch (e) {
      if (ApiError.from(e).isConflict) ref.invalidate(myAcceptancesProvider);
      if (mounted) {
        showSnack(context, ApiError.from(e).message, type: SnackType.error);
      }
    } finally {
      if (mounted) setState(() => _withdrawingId = null);
    }
  }

  Future<void> _answers(DonorAcceptance a, bool edit) async {
    try {
      final data = await ref.read(screeningRepositoryProvider).answers(a.id);
      if (!mounted) return;
      if (edit && !data.canEdit) {
        showSnack(
          context,
          data.editUnavailableReason ??
              'These answers can no longer be updated.',
          type: SnackType.warning,
        );
        ref.invalidate(myAcceptancesProvider);
        return;
      }
      if (edit && data.questionnaire == null) {
        final yes = await showDialog<bool>(
          context: context,
          builder: (_) => AlertDialog(
            title: const Text('Update in interview'),
            content: const Text(
              'This earlier questionnaire is updated through the interview.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(context, false),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(context, true),
                child: const Text('Continue'),
              ),
            ],
          ),
        );
        if (yes == true && mounted) {
          await ref.read(screeningRepositoryProvider).reopen(a.id);
          ref.read(tabAttentionProvider.notifier).refresh();
          if (mounted) {
            context.push(_screeningPath(a.id));
          }
        }
        return;
      }
      final saved = await showScreeningAnswersSheet(
        context,
        data: data,
        editing: edit,
        onSave: (answers) =>
            ref.read(screeningRepositoryProvider).updateAnswers(a.id, answers),
      );
      if (!mounted) return;
      if (saved) {
        ref.invalidate(myAcceptancesProvider);
        ref.invalidate(screeningAnswersProvider(a.id));
        ref.read(tabAttentionProvider.notifier).refresh();
        if (mounted) {
          showSnack(context, 'Your updated answers were sent.');
        }
      }
    } catch (e) {
      if (ApiError.from(e).isConflict) ref.invalidate(myAcceptancesProvider);
      if (mounted) {
        showSnack(context, ApiError.from(e).message, type: SnackType.error);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(myAcceptancesProvider);
    return Scaffold(
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(myAcceptancesProvider);
          await ref
              .read(myAcceptancesProvider.future)
              .then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const ShellPageHeader(title: 'My donations'),
                const SizedBox(height: 16),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(myAcceptancesProvider),
                  loadingMessage: 'Loading donations...',
                  data: (items) => items.isEmpty
                      ? EmptyView(
                          icon: Icons.volunteer_activism_outlined,
                          message:
                              'You have not accepted a donation request yet.',
                          action: FilledButton(
                            onPressed: () => context.go(widget.browsePath),
                            child: const Text('Browse requests'),
                          ),
                        )
                      : Column(
                          children: [
                            for (final a in items)
                              Padding(
                                padding: const EdgeInsets.only(bottom: 12),
                                child: _AcceptanceCard(
                                  acceptance: a,
                                  withdrawing: _withdrawingId == a.id,
                                  onWithdraw: _withdrawingId == null
                                      ? () => _withdraw(a)
                                      : null,
                                  onView: () => _answers(a, false),
                                  onEdit: () => _answers(a, true),
                                  onScreen: () =>
                                      context.push(_screeningPath(a.id)),
                                ),
                              ),
                          ],
                        ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _AcceptanceCard extends StatelessWidget {
  const _AcceptanceCard({
    required this.acceptance,
    required this.withdrawing,
    required this.onWithdraw,
    required this.onView,
    required this.onEdit,
    required this.onScreen,
  });
  final DonorAcceptance acceptance;
  final bool withdrawing;
  final VoidCallback? onWithdraw;
  final VoidCallback onView, onEdit, onScreen;
  @override
  Widget build(BuildContext context) {
    final a = acceptance;
    final latest = a.screeningHistory.isEmpty ? null : a.screeningHistory.last;
    final canScreen =
        !a.requestSuspended &&
        const {'Accepted', 'ScreeningPending'}.contains(a.status);
    final canEdit =
        !a.requestSuspended &&
        a.status == 'ScreeningCompleted' &&
        latest?.status == 'Pending';
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                CircleAvatar(
                  backgroundColor: a.requestDeleted
                      ? Colors.grey
                      : Theme.of(context).colorScheme.primary,
                  foregroundColor: Colors.white,
                  child: Text(a.requestDeleted ? '—' : a.requestBloodGroup),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        a.requestDeleted
                            ? 'Blood request deleted'
                            : a.hospitalName,
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                      Text('Accepted ${Fmt.sriLankaDateTime(a.acceptedAt)}'),
                      Wrap(
                        spacing: 6,
                        children: [
                          StatusBadge(a.requestDeleted ? 'Closed' : a.status),
                          if (a.requestSuspended)
                            const StatusBadge(
                              'Suspended',
                              variant: BadgeVariant.danger,
                            ),
                        ],
                      ),
                      if (a.rejectionReason != null)
                        Text(
                          'Reason: ${a.rejectionReason}',
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            if (a.screeningHistory.isNotEmpty) ...[
              const Text(
                'Screening timeline',
                style: TextStyle(fontWeight: FontWeight.w700),
              ),
              for (final d in a.screeningHistory)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  dense: true,
                  leading: Icon(
                    d.status == 'Approved'
                        ? Icons.check_circle
                        : d.status == 'Rejected'
                        ? Icons.cancel
                        : Icons.schedule,
                  ),
                  title: Text('Report v${d.reportVersion} · ${d.status}'),
                  subtitle: Text(
                    d.rejectionReason ??
                        d.approvalNotes ??
                        Fmt.sriLankaDateTime(d.submittedAt),
                  ),
                ),
            ],
            AdaptiveActions(
              children: [
                if (canScreen)
                  FilledButton.icon(
                    onPressed: onScreen,
                    icon: const Icon(Icons.medical_services_outlined),
                    label: Text(
                      a.status == 'Accepted'
                          ? 'Start screening'
                          : 'Continue screening',
                    ),
                  ),
                if (a.screeningHistory.isNotEmpty)
                  OutlinedButton(
                    onPressed: onView,
                    child: const Text('View my answers'),
                  ),
                if (canEdit)
                  OutlinedButton(
                    onPressed: onEdit,
                    child: const Text('Update my answers'),
                  ),
                if (a.isActive)
                  TextButton.icon(
                    onPressed: onWithdraw,
                    icon: withdrawing
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.undo, size: 18),
                    label: const Text('Withdraw'),
                  ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
