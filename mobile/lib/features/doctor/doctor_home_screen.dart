import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/reply_sheet.dart';
import '../../core/widgets/state_views.dart';
import '../home/shell_page_header.dart';
import '../attention/tab_attention_controller.dart';
import '../verification/verification_models.dart';
import '../verification/verification_repository.dart';
import '../verification/verify_requests_screen.dart' show RequestCard;
import 'screening_models.dart';

class DoctorDashboardData {
  const DoctorDashboardData({
    required this.assignedDecisions,
    required this.screenedDonors,
    required this.pendingReviews,
    required this.awaitingDonation,
  });

  final int assignedDecisions;
  final int screenedDonors;
  final int pendingReviews;
  final int awaitingDonation;

  bool get isEmpty =>
      assignedDecisions == 0 &&
      screenedDonors == 0 &&
      pendingReviews == 0 &&
      awaitingDonation == 0;
}

/// Both shell tabs watch these same providers, so their successful responses
/// remain shared while the indexed doctor shell is alive.
final doctorDashboardProvider = FutureProvider.autoDispose<DoctorDashboardData>(
  (ref) async {
    final requestsFuture = ref.watch(assignedRequestsProvider.future);
    final reportsFuture = ref.watch(screeningReportsProvider.future);
    final requests = await requestsFuture;
    final reports = await reportsFuture;
    final grouped = groupReports(reports);
    return DoctorDashboardData(
      assignedDecisions: requests.where((r) => r.canDoctorDecide).length,
      screenedDonors: grouped[ReportTab.all]!.length,
      pendingReviews: grouped[ReportTab.review]!.length,
      awaitingDonation: grouped[ReportTab.awaiting]!.length,
    );
  },
);

class DoctorDashboardScreen extends ConsumerWidget {
  const DoctorDashboardScreen({super.key});

  Future<void> _reload(WidgetRef ref) async {
    ref.invalidate(assignedRequestsProvider);
    ref.invalidate(screeningReportsProvider);
    ref.read(tabAttentionProvider.notifier).refresh();
    await ref
        .read(doctorDashboardProvider.future)
        .then((_) {}, onError: (_) {});
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(doctorDashboardProvider);
    return Scaffold(
      body: RefreshableScroll(
        onRefresh: () => _reload(ref),
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const ShellPageHeader(title: 'Doctor dashboard'),
                const SizedBox(height: 6),
                const Text(
                  'Review assigned requests and complete clinical donor screening.',
                  style: TextStyle(fontSize: 12, color: AppColors.slate500),
                ),
                const SizedBox(height: 16),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(doctorDashboardProvider),
                  loadingMessage: 'Loading your dashboard...',
                  data: (data) => Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _DoctorStatGrid(data: data),
                      if (data.isEmpty) ...[
                        const SizedBox(height: 16),
                        const Text(
                          'No work has been assigned yet. New requests and screening reports will appear here.',
                          key: Key('doctor-dashboard-empty'),
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            fontSize: 12,
                            color: AppColors.slate500,
                          ),
                        ),
                      ],
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

class _DoctorStatGrid extends StatelessWidget {
  const _DoctorStatGrid({required this.data});

  final DoctorDashboardData data;

  @override
  Widget build(BuildContext context) {
    final cards = [
      _DoctorStat(
        key: const Key('doctor-stat-assigned'),
        label: 'Assigned decisions',
        value: data.assignedDecisions,
        detail: 'Awaiting your decision',
        icon: Icons.assignment_outlined,
        color: AppColors.blue600,
        onTap: () => context.go('${AppRoutes.doctorRequests}?status=Verified'),
      ),
      _DoctorStat(
        key: const Key('doctor-stat-screened'),
        label: 'Screened donors',
        value: data.screenedDonors,
        detail: 'Latest screening cases',
        icon: Icons.people_outline,
        color: AppColors.violet600,
        onTap: () => context.go('${AppRoutes.doctorReports}?view=all'),
      ),
      _DoctorStat(
        key: const Key('doctor-stat-pending'),
        label: 'Pending reviews',
        value: data.pendingReviews,
        detail: 'Awaiting clinical review',
        icon: Icons.fact_check_outlined,
        color: AppColors.amber600,
        onTap: () => context.go('${AppRoutes.doctorReports}?view=review'),
      ),
      _DoctorStat(
        key: const Key('doctor-stat-awaiting'),
        label: 'Awaiting donation',
        value: data.awaitingDonation,
        detail: 'Donation slots reserved',
        icon: Icons.volunteer_activism_outlined,
        color: AppColors.emerald600,
        onTap: () => context.go('${AppRoutes.doctorReports}?view=awaiting'),
      ),
    ];
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = constraints.maxWidth >= 700 ? 4 : 2;
        final scale = MediaQuery.textScalerOf(context).scale(1);
        final height = (142 + (scale - 1) * 42).clamp(142.0, 184.0);
        return GridView.builder(
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: columns,
            crossAxisSpacing: 10,
            mainAxisSpacing: 10,
            mainAxisExtent: height,
          ),
          itemCount: cards.length,
          itemBuilder: (_, index) => cards[index],
        );
      },
    );
  }
}

class _DoctorStat extends StatelessWidget {
  const _DoctorStat({
    super.key,
    required this.label,
    required this.value,
    required this.detail,
    required this.icon,
    required this.color,
    required this.onTap,
  });

  final String label;
  final int value;
  final String detail;
  final IconData icon;
  final Color color;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    return Material(
      color: dark ? AppColors.slate800 : AppColors.slate50,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(icon, color: color, size: 22),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      label,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontSize: 12,
                        color: AppColors.slate500,
                      ),
                    ),
                  ),
                  const Icon(
                    Icons.chevron_right,
                    size: 18,
                    color: AppColors.slate500,
                  ),
                ],
              ),
              const Spacer(),
              FittedBox(
                fit: BoxFit.scaleDown,
                alignment: Alignment.centerLeft,
                child: Text(
                  '$value',
                  maxLines: 1,
                  style: const TextStyle(
                    fontSize: 24,
                    fontWeight: FontWeight.w800,
                  ),
                ),
              ),
              Text(
                detail,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 11, color: AppColors.slate500),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Doctor requests: blood requests assigned to the signed-in doctor. Verified ones wait for the doctor's decision:
/// approve (optional notes, the request opens to donors) or reject (a message the creator sees).
class DoctorRequestsScreen extends ConsumerStatefulWidget {
  const DoctorRequestsScreen({super.key, this.initialStatus = 'Verified'});

  final String? initialStatus;

  @override
  ConsumerState<DoctorRequestsScreen> createState() =>
      _DoctorRequestsScreenState();
}

class _DoctorRequestsScreenState extends ConsumerState<DoctorRequestsScreen> {
  late String? _status = widget.initialStatus;
  String _search = '';

  @override
  void didUpdateWidget(covariant DoctorRequestsScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.initialStatus != widget.initialStatus) {
      _status = widget.initialStatus;
    }
  }

  void _selectStatus(String? status) {
    setState(() => _status = status);
    final value = status ?? 'all';
    context.go(
      '${AppRoutes.doctorRequests}?status=${Uri.encodeQueryComponent(value)}',
    );
  }

  void _reload() {
    ref.invalidate(assignedRequestsProvider);
    ref.read(tabAttentionProvider.notifier).refresh();
  }

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
      onSubmit: (text, _) =>
          ref.read(verificationRepositoryProvider).approve(r.id, text),
    );
    _reload();
    if (sent && mounted)
      showSnack(
        context,
        'The request is now open to eligible donors.',
        type: SnackType.success,
        title: 'Request approved',
      );
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
      onSubmit: (text, _) =>
          ref.read(verificationRepositoryProvider).reject(r.id, text),
    );
    _reload();
    if (sent && mounted)
      showSnack(
        context,
        'The creator will see your rejection message.',
        title: 'Request rejected',
      );
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(assignedRequestsProvider);
    return Scaffold(
      body: RefreshableScroll(
        onRefresh: () async {
          _reload();
          await ref
              .read(assignedRequestsProvider.future)
              .then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const ShellPageHeader(title: 'Assigned requests'),
                const SizedBox(height: 16),
                SearchField(
                  hint: 'Search ID, blood group, reason, hospital',
                  onChanged: (v) => setState(() => _search = v),
                ),
                const SizedBox(height: 10),
                FilterChips<String?>(
                  options: [
                    for (final (s, l) in requestStatusFilters)
                      (s, s == 'Verified' ? 'Awaiting you' : l),
                  ],
                  selected: _status,
                  onSelected: _selectStatus,
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: _reload,
                  loadingMessage: 'Loading your requests...',
                  data: (all) {
                    final waiting = all
                        .where((r) => r.status == 'Verified')
                        .length;
                    final donations = all
                        .where((r) => r.pendingHospitalDonations > 0)
                        .length;
                    final list = all
                        .where(
                          (r) => requestMatches(
                            r,
                            status: _status,
                            query: _search,
                          ),
                        )
                        .toList();
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Text(
                          '$waiting request(s) waiting for your decision'
                          '${donations > 0 ? ' · $donations with hospital donations to review' : ''}',
                          style: const TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: 8),
                        if (list.isEmpty)
                          const EmptyView(
                            icon: Icons.assignment_outlined,
                            message: 'No assigned requests here.',
                          )
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
                                      style: FilledButton.styleFrom(
                                        backgroundColor: AppColors.emerald600,
                                        minimumSize: const Size(0, 40),
                                      ),
                                      onPressed: () => _approve(r),
                                      icon: const Icon(Icons.check, size: 18),
                                      label: const Text('Approve'),
                                    ),
                                    OutlinedButton.icon(
                                      key: Key('reject-${r.id}'),
                                      style: OutlinedButton.styleFrom(
                                        foregroundColor: AppColors.rose600,
                                        minimumSize: const Size(0, 40),
                                      ),
                                      onPressed: () => _reject(r),
                                      icon: const Icon(Icons.close, size: 18),
                                      label: const Text('Reject'),
                                    ),
                                  ],
                                  if (!r.isSuspended &&
                                      const {
                                        'Approved',
                                        'Completed',
                                      }.contains(r.status))
                                    OutlinedButton.icon(
                                      key: Key('donors-${r.id}'),
                                      style: OutlinedButton.styleFrom(
                                        minimumSize: const Size(0, 40),
                                      ),
                                      onPressed: () => context.push(
                                        AppRoutes.doctorRequestDonors(r.id),
                                      ),
                                      icon: Icon(
                                        r.pendingHospitalDonations > 0
                                            ? Icons.volunteer_activism_outlined
                                            : Icons.people_outline,
                                        size: 18,
                                      ),
                                      label: Text(
                                        r.pendingHospitalDonations > 0
                                            ? 'Review hospital donations'
                                            : 'Donors',
                                      ),
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
          await ref
              .read(assignedRequestsProvider.future)
              .then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'Hospitals donated packets to these requests. Approving counts them as donated at once; rejecting returns them '
                  'to the hospital. No AI screening applies to hospital blood.',
                  style: TextStyle(fontSize: 12, color: AppColors.slate500),
                ),
                const SizedBox(height: 10),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(assignedRequestsProvider),
                  data: (all) {
                    final list = all
                        .where(
                          (r) =>
                              r.pendingHospitalDonations > 0 && !r.isSuspended,
                        )
                        .toList();
                    if (list.isEmpty) {
                      return const EmptyView(
                        icon: Icons.volunteer_activism_outlined,
                        message: 'No hospital donation is waiting for your decision.',
                      );
                    }
                    return Column(
                      children: [
                        for (final r in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: RequestCard(
                              request: r,
                              showHospital: true,
                              actions: [
                                FilledButton.icon(
                                  style: FilledButton.styleFrom(
                                    minimumSize: const Size(0, 40),
                                  ),
                                  onPressed: () => context.push(
                                    AppRoutes.doctorRequestDonors(r.id),
                                  ),
                                  icon: const Icon(
                                    Icons.volunteer_activism_outlined,
                                    size: 18,
                                  ),
                                  label: Text(
                                    'Review ${r.pendingHospitalDonations} donation(s)',
                                  ),
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
