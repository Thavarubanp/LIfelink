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
import 'screening_answers_sheet.dart';
import 'screening_repository.dart';

class MyAcceptancesScreen extends ConsumerWidget {
  const MyAcceptancesScreen({super.key});

  Future<void> _withdraw(BuildContext context, WidgetRef ref, DonorAcceptance a) async {
    final yes = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
      title: const Text('Withdraw from donation?'),
      content: Text(a.status == 'Verified'
          ? 'Your reserved slot will be released for another donor.'
          : 'You can accept another request afterwards.'),
      actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Keep')),
        FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Withdraw'))],
    ));
    if (yes != true) return;
    try {
      await ref.read(screeningRepositoryProvider).withdraw(a.id);
      ref.invalidate(myAcceptancesProvider);
      if (context.mounted) showSnack(context, 'You have withdrawn from this donation.');
    } catch (e) {
      if (ApiError.from(e).isConflict) ref.invalidate(myAcceptancesProvider);
      if (context.mounted) showSnack(context, ApiError.from(e).message, type: SnackType.error);
    }
  }

  Future<void> _answers(BuildContext context, WidgetRef ref, DonorAcceptance a, bool edit) async {
    try {
      final data = await ref.read(screeningRepositoryProvider).answers(a.id);
      if (!context.mounted) return;
      if (edit && !data.canEdit) {
        showSnack(context, data.editUnavailableReason ?? 'These answers can no longer be updated.', type: SnackType.warning);
        ref.invalidate(myAcceptancesProvider);
        return;
      }
      if (edit && data.questionnaire == null) {
        final yes = await showDialog<bool>(context: context, builder: (_) => AlertDialog(
          title: const Text('Update in interview'), content: const Text('This earlier questionnaire is updated through the interview.'),
          actions: [TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
            FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Continue'))],
        ));
        if (yes == true && context.mounted) {
          await ref.read(screeningRepositoryProvider).reopen(a.id);
          if (context.mounted) context.push(AppRoutes.screening(a.id));
        }
        return;
      }
      final saved = await showScreeningAnswersSheet(context, data: data, editing: edit,
        onSave: (answers) => ref.read(screeningRepositoryProvider).updateAnswers(a.id, answers));
      if (saved) {
        ref.invalidate(myAcceptancesProvider);
        ref.invalidate(screeningAnswersProvider(a.id));
        if (context.mounted) showSnack(context, 'Your updated answers were sent.');
      }
    } catch (e) {
      if (ApiError.from(e).isConflict) ref.invalidate(myAcceptancesProvider);
      if (context.mounted) showSnack(context, ApiError.from(e).message, type: SnackType.error);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(myAcceptancesProvider);
    return Scaffold(appBar: AppBar(title: const Text('My donations')), body: RefreshableScroll(
      onRefresh: () async { ref.invalidate(myAcceptancesProvider); await ref.read(myAcceptancesProvider.future).then((_) {}, onError: (_) {}); },
      child: ContentWidth(child: Padding(padding: const EdgeInsets.all(16), child: AsyncView(
        value: value, onRetry: () => ref.invalidate(myAcceptancesProvider), loadingMessage: 'Loading donations...',
        data: (items) => items.isEmpty ? EmptyView(icon: Icons.volunteer_activism_outlined,
          message: 'You have not accepted a donation request yet.',
          action: FilledButton(onPressed: () => context.go(AppRoutes.donorRequests), child: const Text('Browse requests'))) : Column(children: [
            for (final a in items) Padding(padding: const EdgeInsets.only(bottom: 12), child: _AcceptanceCard(
              acceptance: a, onWithdraw: () => _withdraw(context, ref, a),
              onView: () => _answers(context, ref, a, false), onEdit: () => _answers(context, ref, a, true))),
          ]),
      ))),
    ));
  }
}

class _AcceptanceCard extends StatelessWidget {
  const _AcceptanceCard({required this.acceptance, required this.onWithdraw, required this.onView, required this.onEdit});
  final DonorAcceptance acceptance;
  final VoidCallback onWithdraw, onView, onEdit;
  @override
  Widget build(BuildContext context) {
    final a = acceptance;
    final latest = a.screeningHistory.isEmpty ? null : a.screeningHistory.last;
    final canScreen = !a.requestSuspended && const {'Accepted', 'ScreeningPending'}.contains(a.status);
    final canEdit = !a.requestSuspended && a.status == 'ScreeningCompleted' && latest?.status == 'Pending';
    return Card(child: Padding(padding: const EdgeInsets.all(14), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        CircleAvatar(backgroundColor: a.requestDeleted ? Colors.grey : Theme.of(context).colorScheme.primary,
          foregroundColor: Colors.white, child: Text(a.requestDeleted ? '—' : a.requestBloodGroup)),
        const SizedBox(width: 12), Expanded(child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(a.requestDeleted ? 'Blood request deleted' : a.hospitalName, style: const TextStyle(fontWeight: FontWeight.w700)),
          Text('Accepted ${Fmt.sriLankaDateTime(a.acceptedAt)}'),
          Wrap(spacing: 6, children: [StatusBadge(a.requestDeleted ? 'Closed' : a.status),
            if (a.requestSuspended) const StatusBadge('Suspended', variant: BadgeVariant.danger)]),
          if (a.rejectionReason != null) Text('Reason: ${a.rejectionReason}', style: TextStyle(color: Theme.of(context).colorScheme.error)),
        ]))]),
      const SizedBox(height: 12),
      if (a.screeningHistory.isNotEmpty) ...[
        const Text('Screening timeline', style: TextStyle(fontWeight: FontWeight.w700)),
        for (final d in a.screeningHistory) ListTile(contentPadding: EdgeInsets.zero, dense: true,
          leading: Icon(d.status == 'Approved' ? Icons.check_circle : d.status == 'Rejected' ? Icons.cancel : Icons.schedule),
          title: Text('Report v${d.reportVersion} · ${d.status}'),
          subtitle: Text(d.rejectionReason ?? d.approvalNotes ?? Fmt.sriLankaDateTime(d.submittedAt))),
      ],
      Wrap(spacing: 8, runSpacing: 8, children: [
        if (canScreen) FilledButton.icon(onPressed: () => context.push(AppRoutes.screening(a.id)),
          icon: const Icon(Icons.medical_services_outlined), label: Text(a.status == 'Accepted' ? 'Start screening' : 'Continue screening')),
        if (a.screeningHistory.isNotEmpty) OutlinedButton(onPressed: onView, child: const Text('View my answers')),
        if (canEdit) OutlinedButton(onPressed: onEdit, child: const Text('Update my answers')),
        if (a.isActive) TextButton(onPressed: onWithdraw, child: const Text('Withdraw')),
      ]),
    ])));
  }
}
