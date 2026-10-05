import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import 'blood_request_models.dart';
import 'blood_request_repository.dart';

BadgeVariant requestStatusVariant(String status) => switch (status) {
      'Pending' => BadgeVariant.warning,
      'Verified' => BadgeVariant.info,
      'Approved' || 'Completed' => BadgeVariant.success,
      'Rejected' => BadgeVariant.danger,
      _ => BadgeVariant.neutral,
    };

({String label, BadgeVariant variant, String next}) acceptanceStatusDisplay(String status) => switch (status) {
      'Accepted' => (label: 'Screening not started', variant: BadgeVariant.info, next: 'Start your health screening.'),
      'ScreeningPending' => (label: 'Screening in progress', variant: BadgeVariant.warning, next: 'Continue your health screening.'),
      'ScreeningCompleted' => (label: 'Waiting for doctor', variant: BadgeVariant.info, next: 'Your answers are waiting for review.'),
      'Verified' => (label: 'Approved - slot reserved', variant: BadgeVariant.success, next: 'Visit the hospital to donate.'),
      'Matched' => (label: 'Donation recorded', variant: BadgeVariant.success, next: 'Thank you for donating!'),
      'Rejected' => (label: 'Not approved', variant: BadgeVariant.danger, next: ''),
      'Cancelled' => (label: 'Withdrawn / closed', variant: BadgeVariant.neutral, next: ''),
      _ => (label: status, variant: BadgeVariant.neutral, next: ''),
    };

class DonorHomeScreen extends ConsumerWidget {
  const DonorHomeScreen({super.key});

  Future<void> _refresh(WidgetRef ref) async {
    ref.invalidate(donorHomeProvider);
    await ref.read(donorHomeProvider.future).then((_) {}, onError: (_) {});
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(donorHomeProvider);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Donor home'),
        actions: [
          IconButton(
            tooltip: 'Explore blood requests',
            onPressed: () => context.go(AppRoutes.donorRequests),
            icon: const Icon(Icons.bloodtype_outlined),
          ),
        ],
      ),
      body: RefreshableScroll(
        onRefresh: () => _refresh(ref),
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(donorHomeProvider),
          loadingMessage: 'Loading your donor dashboard...',
          data: (data) => ContentWidth(
            maxWidth: 960,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _DashboardHeader(profile: data.profile),
                  const SizedBox(height: 16),
                  _SummaryGrid(data: data),
                  const SizedBox(height: 16),
                  _ActiveRequests(requests: data.activeRequests),
                  const SizedBox(height: 16),
                  _ActiveAcceptances(acceptances: data.activeAcceptances),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _DashboardHeader extends StatelessWidget {
  const _DashboardHeader({required this.profile});

  final DonorProfileSummary profile;

  @override
  Widget build(BuildContext context) => Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Your donor dashboard', style: Theme.of(context).textTheme.titleLarge),
                const SizedBox(height: 3),
                const Text(
                  'Track your requests, donation commitments and eligibility.',
                  style: TextStyle(fontSize: 12, color: AppColors.slate500),
                ),
              ],
            ),
          ),
          if (profile.bloodGroup?.isNotEmpty == true) BloodGroupBadge(profile.bloodGroup!, large: true),
        ],
      );
}

class _SummaryGrid extends StatelessWidget {
  const _SummaryGrid({required this.data});

  final DonorHomeData data;

  @override
  Widget build(BuildContext context) {
    final eligible = data.profile.eligibleAt(DateTime.now());
    final cards = <Widget>[
      _SummaryCard(
        label: 'Donation status',
        value: eligible ? 'Eligible' : 'Resting',
        detail: eligible ? 'Ready to donate' : 'Eligible again ${Fmt.date(data.profile.nextEligibleDonationDate)}',
        icon: Icons.verified_user_outlined,
        color: eligible ? AppColors.emerald600 : AppColors.amber600,
      ),
      _SummaryCard(
        label: 'Active requests',
        value: '${data.activeRequests.length}',
        detail: 'Requests you created',
        icon: Icons.assignment_outlined,
        color: AppColors.blue600,
      ),
      _SummaryCard(
        label: 'Active acceptances',
        value: '${data.activeAcceptances.length}',
        detail: 'Donation commitments',
        icon: Icons.volunteer_activism_outlined,
        color: AppColors.red600,
      ),
      _SummaryCard(
        label: 'Donations completed',
        value: '${data.completedDonations}',
        detail: 'Recorded donations',
        icon: Icons.favorite_outline,
        color: AppColors.violet600,
      ),
    ];
    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth >= 600 ? (constraints.maxWidth - 12) / 2 : constraints.maxWidth;
        return Wrap(
          spacing: 12,
          runSpacing: 12,
          children: [for (final card in cards) SizedBox(width: width, child: card)],
        );
      },
    );
  }
}

class _SummaryCard extends StatelessWidget {
  const _SummaryCard({required this.label, required this.value, required this.detail, required this.icon, required this.color});

  final String label;
  final String value;
  final String detail;
  final IconData icon;
  final Color color;

  @override
  Widget build(BuildContext context) => Card(
        margin: EdgeInsets.zero,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: [
              Container(
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(12)),
                child: Icon(icon, color: color),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(label, style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                    Text(value, style: const TextStyle(fontSize: 21, fontWeight: FontWeight.w800)),
                    Text(detail, style: const TextStyle(fontSize: 11, color: AppColors.slate500)),
                  ],
                ),
              ),
            ],
          ),
        ),
      );
}

class _ActiveRequests extends StatelessWidget {
  const _ActiveRequests({required this.requests});

  final List<BloodRequest> requests;

  @override
  Widget build(BuildContext context) => SectionCard(
        title: 'My active requests',
        icon: Icons.assignment_outlined,
        trailing: TextButton(onPressed: () => context.push(AppRoutes.createRequest), child: const Text('View all')),
        child: requests.isEmpty
            ? const EmptyView(icon: Icons.assignment_turned_in_outlined, message: 'You have no active blood requests.')
            : ShowMoreList(
                items: requests,
                pageSize: 3,
                itemBuilder: (_, request) => _RequestRow(request: request),
              ),
      );
}

class _RequestRow extends StatelessWidget {
  const _RequestRow({required this.request});

  final BloodRequest request;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            BloodGroupBadge(request.bloodGroup),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(request.hospitalName, style: const TextStyle(fontWeight: FontWeight.w700)),
                  Text(
                    '${request.fulfilledUnits}/${request.unitsRequired} donated'
                    '${request.reservedUnits > 0 ? ', ${request.reservedUnits} reserved' : ''}',
                    style: const TextStyle(fontSize: 12, color: AppColors.slate500),
                  ),
                  if (request.reason.isNotEmpty)
                    Text(request.reason, maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(fontSize: 12)),
                ],
              ),
            ),
            const SizedBox(width: 8),
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                StatusBadge(request.status, variant: requestStatusVariant(request.status)),
                if (request.isSuspended) ...[
                  const SizedBox(height: 4),
                  SuspendedBadge(reason: request.suspensionReason),
                ],
              ],
            ),
          ],
        ),
      );
}

class _ActiveAcceptances extends StatelessWidget {
  const _ActiveAcceptances({required this.acceptances});

  final List<DonorAcceptance> acceptances;

  @override
  Widget build(BuildContext context) => SectionCard(
        title: 'My active acceptances',
        icon: Icons.volunteer_activism_outlined,
        trailing: TextButton(onPressed: () => context.go(AppRoutes.donorAcceptances), child: const Text('View all')),
        child: acceptances.isEmpty
            ? const EmptyView(icon: Icons.volunteer_activism_outlined, message: 'You have no active donation commitments.')
            : ShowMoreList(
                items: acceptances,
                pageSize: 3,
                itemBuilder: (_, acceptance) => _AcceptanceRow(acceptance: acceptance),
              ),
      );
}

class _AcceptanceRow extends StatelessWidget {
  const _AcceptanceRow({required this.acceptance});

  final DonorAcceptance acceptance;

  @override
  Widget build(BuildContext context) {
    final display = acceptanceStatusDisplay(acceptance.status);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          BloodGroupBadge(acceptance.requestBloodGroup.isEmpty ? '?' : acceptance.requestBloodGroup),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(acceptance.hospitalName, style: const TextStyle(fontWeight: FontWeight.w700)),
                Text(display.next, style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                Text('Accepted ${Fmt.sriLankaDateTime(acceptance.acceptedAt)}', style: const TextStyle(fontSize: 11)),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              StatusBadge(display.label, variant: display.variant),
              if (acceptance.requestSuspended) ...[
                const SizedBox(height: 4),
                const SuspendedBadge(),
              ],
            ],
          ),
        ],
      ),
    );
  }
}
