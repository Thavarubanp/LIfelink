import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../blood_requests/blood_request_models.dart';
import 'screening_repository.dart';

class SuspendedWithdrawalsScreen extends ConsumerStatefulWidget {
  const SuspendedWithdrawalsScreen({super.key});

  @override
  ConsumerState<SuspendedWithdrawalsScreen> createState() =>
      _SuspendedWithdrawalsScreenState();
}

class _SuspendedWithdrawalsScreenState
    extends ConsumerState<SuspendedWithdrawalsScreen> {
  String? _busyId;

  Future<void> _refresh() async {
    ref.invalidate(activeWithdrawalProvider);
    await ref
        .read(activeWithdrawalProvider.future)
        .then((_) {}, onError: (_) {});
  }

  Future<void> _withdraw(DonorAcceptance acceptance) async {
    final yes = await confirmDialog(
      context,
      title: 'Withdraw from donation?',
      message: acceptance.status == 'Verified'
          ? 'Your reserved slot will be released for another donor.'
          : 'Your active participation will be cancelled. Screening history is kept.',
      confirmLabel: 'Withdraw',
      destructive: true,
    );
    if (!yes) return;
    setState(() => _busyId = acceptance.id);
    try {
      await ref
          .read(screeningRepositoryProvider)
          .suspendedWithdraw(acceptance.id);
      await _refresh();
      if (mounted) {
        showSnack(
          context,
          'You have withdrawn from this donation.',
          type: SnackType.success,
        );
      }
    } catch (e) {
      if (ApiError.from(e).isConflict) ref.invalidate(activeWithdrawalProvider);
      if (mounted) {
        showErrorSnack(context, e, title: 'Withdrawal not completed');
      }
    } finally {
      if (mounted) setState(() => _busyId = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(activeWithdrawalProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Active donations')),
      body: RefreshableScroll(
        onRefresh: _refresh,
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(activeWithdrawalProvider),
          loadingMessage: 'Loading active donations...',
          data: (items) => items.isEmpty
              ? const EmptyView(
                  icon: Icons.volunteer_activism_outlined,
                  message:
                      'You have no active donation participation to withdraw.',
                )
              : ContentWidth(
                  maxWidth: 720,
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        const InfoBanner(
                          'Your account remains suspended. This page only allows withdrawal from existing active donations.',
                          icon: Icons.lock_outline,
                        ),
                        const SizedBox(height: 12),
                        for (final a in items)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 12),
                            child: SectionCard(
                              title: a.requestDeleted
                                  ? 'Blood request closed'
                                  : a.hospitalName,
                              icon: Icons.bloodtype_outlined,
                              trailing: StatusBadge(
                                a.status,
                                variant: BadgeVariant.info,
                              ),
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.stretch,
                                children: [
                                  Wrap(
                                    spacing: 16,
                                    runSpacing: 8,
                                    children: [
                                      BloodGroupBadge(a.requestBloodGroup),
                                      Text(
                                        'Accepted ${Fmt.sriLankaDateTime(a.acceptedAt)}',
                                      ),
                                    ],
                                  ),
                                  const SizedBox(height: 12),
                                  BusyButton(
                                    label: 'Withdraw participation',
                                    icon: Icons.cancel_outlined,
                                    busy: _busyId == a.id,
                                    onPressed: _busyId == null
                                        ? () => _withdraw(a)
                                        : null,
                                  ),
                                ],
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
        ),
      ),
    );
  }
}
