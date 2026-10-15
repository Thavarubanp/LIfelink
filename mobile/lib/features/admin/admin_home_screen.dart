import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../home/shell_page_header.dart';
import 'admin_repository.dart';
import 'attention_controller.dart';

String? adminStatisticDestination(String label) => switch (label) {
  'Donors / patients' => AppRoutes.adminUsers,
  'Hospitals' => AppRoutes.adminHospitals,
  'Doctors' => AppRoutes.adminDoctors,
  'Active requests' => '${AppRoutes.adminActivity}?tab=blood-requests',
  'Pending complaints' => AppRoutes.adminComplaints,
  'Pending registrations' => AppRoutes.adminRegistrations,
  'Pending appeals' => AppRoutes.adminAppeals,
  'Suspended users' => '${AppRoutes.adminUsers}?filter=suspended',
  'Suspended hospitals' => '${AppRoutes.adminHospitals}?filter=suspended',
  _ => null,
};

/// Admin home: the nine dashboard numbers.
class AdminHomeScreen extends ConsumerWidget {
  const AdminHomeScreen({super.key});

  Future<void> _refresh(WidgetRef ref) async {
    ref.invalidate(adminStatsProvider);
    await Future.wait([
      ref.read(adminStatsProvider.future).then((_) {}, onError: (_) {}),
      ref.read(adminAttentionProvider.notifier).refresh(),
    ]);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final stats = ref.watch(adminStatsProvider);
    return Scaffold(
      body: RefreshableScroll(
        onRefresh: () => _refresh(ref),
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const ShellPageHeader(title: 'Administration'),
                const SizedBox(height: 16),
                SectionCard(
                  title: 'Platform statistics',
                  icon: Icons.insights,
                  child: AsyncView(
                    value: stats,
                    onRetry: () => ref.invalidate(adminStatsProvider),
                    loadingMessage: 'Loading statistics...',
                    data: (s) => StatGrid(
                      actions: {
                        for (final label in const [
                          'Donors / patients',
                          'Hospitals',
                          'Doctors',
                          'Active requests',
                          'Pending complaints',
                          'Pending registrations',
                          'Pending appeals',
                          'Suspended users',
                          'Suspended hospitals',
                        ])
                          label: () {
                            final destination = adminStatisticDestination(
                              label,
                            )!;
                            if (destination.startsWith(
                              AppRoutes.adminActivity,
                            )) {
                              context.go(destination);
                            } else {
                              context.push(destination);
                            }
                          },
                      },
                      stats: [
                        (
                          'Donors / patients',
                          s.totalDonorPatients,
                          Icons.people_outline,
                          AppColors.blue600,
                        ),
                        (
                          'Hospitals',
                          s.totalHospitals,
                          Icons.local_hospital_outlined,
                          AppColors.emerald600,
                        ),
                        (
                          'Doctors',
                          s.totalDoctors,
                          Icons.medical_services_outlined,
                          AppColors.violet600,
                        ),
                        (
                          'Active requests',
                          s.activeRequests,
                          Icons.bloodtype_outlined,
                          AppColors.red600,
                        ),
                        (
                          'Pending complaints',
                          s.pendingComplaints,
                          Icons.report_outlined,
                          AppColors.amber600,
                        ),
                        (
                          'Pending registrations',
                          s.pendingHospitalApprovals,
                          Icons.domain_add_outlined,
                          AppColors.amber600,
                        ),
                        (
                          'Pending appeals',
                          s.pendingAppeals,
                          Icons.gavel_outlined,
                          AppColors.amber600,
                        ),
                        (
                          'Suspended users',
                          s.activeSuspendedUsers,
                          Icons.person_off_outlined,
                          AppColors.rose600,
                        ),
                        (
                          'Suspended hospitals',
                          s.activeSuspendedHospitals,
                          Icons.domain_disabled_outlined,
                          AppColors.rose600,
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
    );
  }
}

/// Equal-size number tiles (2 columns on phones, 3 on tablets).
class StatGrid extends StatelessWidget {
  const StatGrid({super.key, required this.stats, this.actions = const {}});

  final List<(String, int, IconData, Color)> stats;
  final Map<String, VoidCallback> actions;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final columns = c.maxWidth >= 600 ? 3 : 2;
      const gap = 10.0;
      final dark = Theme.of(context).brightness == Brightness.dark;
      final textScale = MediaQuery.textScalerOf(context).scale(1);
      final tileHeight = (104 + (textScale - 1) * 40).clamp(104.0, 152.0);
      return GridView.builder(
        shrinkWrap: true,
        physics: const NeverScrollableScrollPhysics(),
        gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
          crossAxisCount: columns,
          crossAxisSpacing: gap,
          mainAxisSpacing: gap,
          mainAxisExtent: tileHeight,
        ),
        itemCount: stats.length,
        itemBuilder: (context, index) {
          final (label, value, icon, color) = stats[index];
          return Material(
            color: dark ? AppColors.slate800 : AppColors.slate50,
            borderRadius: BorderRadius.circular(12),
            child: InkWell(
              key: actions[label] == null
                  ? null
                  : Key('admin-directory-$label'),
              borderRadius: BorderRadius.circular(12),
              onTap: actions[label],
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Row(
                  children: [
                    Icon(icon, color: color),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        mainAxisAlignment: MainAxisAlignment.center,
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          SizedBox(
                            width: double.infinity,
                            child: FittedBox(
                              fit: BoxFit.scaleDown,
                              alignment: Alignment.centerLeft,
                              child: Text(
                                '$value',
                                maxLines: 1,
                                style: const TextStyle(
                                  fontSize: 20,
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                            ),
                          ),
                          Text(
                            label,
                            style: const TextStyle(
                              fontSize: 12,
                              color: AppColors.slate500,
                            ),
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ],
                      ),
                    ),
                    if (actions[label] != null)
                      const Icon(
                        Icons.chevron_right,
                        size: 18,
                        color: AppColors.slate500,
                      ),
                  ],
                ),
              ),
            ),
          );
        },
      );
    },
  );
}

/// What waits for the admin, each opening its screen; counts come from /Admin/attention-counts.
class AttentionCards extends ConsumerWidget {
  const AttentionCards({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final a = ref.watch(adminAttentionProvider);
    final items = [
      (
        'Hospital registrations',
        'Pending or awaiting your review',
        a?.pendingRegistrations,
        Icons.domain_add_outlined,
        AppRoutes.adminRegistrations,
      ),
      (
        'Appeals',
        'Pending appeals',
        a?.pendingAppeals,
        Icons.gavel_outlined,
        AppRoutes.adminAppeals,
      ),
      (
        'Complaints',
        'Waiting for your reply',
        a?.pendingComplaints,
        Icons.report_outlined,
        AppRoutes.adminComplaints,
      ),
    ];
    return SectionCard(
      title: 'Needs attention',
      icon: Icons.flag_outlined,
      child: Column(
        children: [
          for (final (title, subtitle, count, icon, path) in items)
            ListTile(
              key: Key('attention-$title'),
              contentPadding: EdgeInsets.zero,
              leading: Icon(icon, color: AppColors.red600),
              title: Text(title),
              subtitle: Text(subtitle),
              trailing: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (count == null)
                    const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  else if (badgeText(count) != null)
                    Badge(
                      label: Text(badgeText(count)!),
                      backgroundColor: AppColors.red600,
                    )
                  else
                    const Text(
                      '0',
                      style: TextStyle(color: AppColors.slate500),
                    ),
                  const Icon(Icons.chevron_right),
                ],
              ),
              onTap: () => context.push(path),
            ),
        ],
      ),
    );
  }
}

/// The Attention tab: the same cards on their own, with pull to refresh.
class AttentionScreen extends ConsumerWidget {
  const AttentionScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => Scaffold(
    body: RefreshableScroll(
      onRefresh: () => ref.read(adminAttentionProvider.notifier).refresh(),
      child: const ContentWidth(
        child: Padding(padding: EdgeInsets.all(16), child: AttentionCards()),
      ),
    ),
  );
}
