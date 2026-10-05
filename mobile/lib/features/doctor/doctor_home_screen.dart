import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/reply_sheet.dart';
import '../../core/widgets/state_views.dart';
import '../verification/verification_models.dart';
import '../verification/verification_repository.dart';
import '../verification/verify_requests_screen.dart' show RequestCard;

/// Doctor home: the blood requests assigned to the signed-in doctor. Verified ones wait for the doctor's decision:
/// approve (optional notes, the request opens to donors) or reject (a message the creator sees).
class DoctorHomeScreen extends ConsumerStatefulWidget {
  const DoctorHomeScreen({super.key});

  @override
  ConsumerState<DoctorHomeScreen> createState() => _DoctorHomeScreenState();
}

class _DoctorHomeScreenState extends ConsumerState<DoctorHomeScreen> {
  String? _status = 'Verified';
  String _search = '';

  void _reload() => ref.invalidate(assignedRequestsProvider);

  Future<void> _approve(ClinicalRequest r) async {
    final sent = await showReplySheet(
      context,
      title: 'Approve request #${r.shortId}',
      subtitle: 'The request opens to eligible donors.',
      label: 'Notes (optional)',
      submitLabel: 'Approve request',
      minLength: 0,
      maxLength: 500,
      allowAttachment: false,
      onSubmit: (text, _) => ref.read(verificationRepositoryProvider).approve(r.id, text),
    );
    _reload();
    if (sent && mounted) showSnack(context, 'The request is now open to eligible donors.', type: SnackType.success, title: 'Request approved');
  }

  Future<void> _reject(ClinicalRequest r) async {
    final sent = await showReplySheet(
      context,
      title: 'Reject request #${r.shortId}',
      subtitle: 'The creator will see your message.',
      label: 'Rejection message',
      submitLabel: 'Reject request',
      maxLength: 500,
      requiredMessage: 'A rejection message is required.',
      allowAttachment: false,
      destructive: true,
      onSubmit: (text, _) => ref.read(verificationRepositoryProvider).reject(r.id, text),
    );
    _reload();
    if (sent && mounted) showSnack(context, 'The creator will see your rejection message.', title: 'Request rejected');
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(assignedRequestsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Assigned requests')),
      body: RefreshableScroll(
        onRefresh: () async {
          _reload();
          await ref.read(assignedRequestsProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SearchField(hint: 'Search ID, blood group, reason, hospital', onChanged: (v) => setState(() => _search = v)),
                const SizedBox(height: 10),
                FilterChips<String?>(
                  options: [for (final (s, l) in requestStatusFilters) (s, s == 'Verified' ? 'Awaiting you' : l)],
                  selected: _status,
                  onSelected: (s) => setState(() => _status = s),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: _reload,
                  loadingMessage: 'Loading your requests...',
                  data: (all) {
                    final waiting = all.where((r) => r.status == 'Verified').length;
                    final donations = all.where((r) => r.pendingHospitalDonations > 0).length;
                    final list = all.where((r) => requestMatches(r, status: _status, query: _search)).toList();
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Text('$waiting request(s) waiting for your decision'
                            '${donations > 0 ? ' · $donations with hospital donations to review' : ''}',
                            style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
                        const SizedBox(height: 8),
                        if (list.isEmpty)
                          const EmptyView(icon: Icons.assignment_outlined, message: 'No assigned requests here.')
                        else
                          for (final r in list)
                            Padding(
                              padding: const EdgeInsets.only(bottom: 10),
                              child: RequestCard(
                                request: r,
                                showHospital: true,
                                actions: [
                                  if (r.canDoctorDecide) ...[
                                    FilledButton.icon(
                                      key: Key('approve-${r.id}'),
                                      style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600, minimumSize: const Size(0, 40)),
                                      onPressed: () => _approve(r),
                                      icon: const Icon(Icons.check, size: 18),
                                      label: const Text('Approve'),
                                    ),
                                    OutlinedButton.icon(
                                      key: Key('reject-${r.id}'),
                                      style: OutlinedButton.styleFrom(foregroundColor: AppColors.rose600, minimumSize: const Size(0, 40)),
                                      onPressed: () => _reject(r),
                                      icon: const Icon(Icons.close, size: 18),
                                      label: const Text('Reject'),
                                    ),
                                  ],
                                  if (!r.isSuspended && const {'Approved', 'Completed'}.contains(r.status))
                                    OutlinedButton.icon(
                                      key: Key('donors-${r.id}'),
                                      style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40)),
                                      onPressed: () => context.push(AppRoutes.doctorRequestDonors(r.id)),
                                      icon: Icon(r.pendingHospitalDonations > 0 ? Icons.volunteer_activism_outlined : Icons.people_outline, size: 18),
                                      label: Text(r.pendingHospitalDonations > 0 ? 'Review hospital donations' : 'Donors'),
                                    ),
                                ],
                              ),
                            ),
                      ],
                    );
                  },
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Doctor: assigned requests with hospital donations waiting (More → Hospital donations).
class HospitalDonationsScreen extends ConsumerWidget {
  const HospitalDonationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(assignedRequestsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Hospital donations')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(assignedRequestsProvider);
          await ref.read(assignedRequestsProvider.future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(assignedRequestsProvider),
          data: (all) {
            final list = all.where((r) => r.pendingHospitalDonations > 0 && !r.isSuspended).toList();
            if (list.isEmpty) {
              return const EmptyView(icon: Icons.volunteer_activism_outlined, message: 'No hospital donation is waiting for your decision.');
            }
            return ContentWidth(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  children: [
                    const Text(
                      'Hospitals donated packets to these requests. Approving counts them as donated at once; rejecting returns them '
                      'to the hospital. No AI screening applies to hospital blood.',
                      style: TextStyle(fontSize: 12, color: AppColors.slate500),
                    ),
                    const SizedBox(height: 10),
                    for (final r in list)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: RequestCard(
                          request: r,
                          showHospital: true,
                          actions: [
                            FilledButton.icon(
                              style: FilledButton.styleFrom(minimumSize: const Size(0, 40)),
                              onPressed: () => context.push(AppRoutes.doctorRequestDonors(r.id)),
                              icon: const Icon(Icons.volunteer_activism_outlined, size: 18),
                              label: Text('Review ${r.pendingHospitalDonations} donation(s)'),
                            ),
                          ],
                        ),
                      ),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}
