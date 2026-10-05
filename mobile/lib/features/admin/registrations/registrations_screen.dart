import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/routing/routes.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/badges.dart';
import '../../../core/widgets/common.dart';
import '../../../core/widgets/state_views.dart';
import 'registration_repository.dart';

enum RegistrationFilter { needsReview, waiting, all }

BadgeVariant registrationVariant(String status) => switch (status) {
      'Pending' => BadgeVariant.warning,
      'AwaitingAdminReview' => BadgeVariant.info,
      'Rejected' => BadgeVariant.danger,
      'Approved' => BadgeVariant.success,
      _ => BadgeVariant.neutral,
    };

/// Hospital registrations the admin decides: pending ones and hospital replies need review; rejected ones wait for
/// the hospital. Approved hospitals are managed in the Hospitals directory.
class RegistrationsScreen extends ConsumerStatefulWidget {
  const RegistrationsScreen({super.key});

  @override
  ConsumerState<RegistrationsScreen> createState() => _RegistrationsScreenState();
}

class _RegistrationsScreenState extends ConsumerState<RegistrationsScreen> {
  RegistrationFilter _filter = RegistrationFilter.needsReview;
  String _search = '';

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(adminHospitalsProvider);
    final term = _search.trim().toLowerCase();
    return Scaffold(
      appBar: AppBar(title: const Text('Hospital registrations')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminHospitalsProvider);
          await ref.read(adminHospitalsProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SearchField(hint: 'Search name, city, email, registration no.', onChanged: (v) => setState(() => _search = v)),
                const SizedBox(height: 10),
                FilterChips<RegistrationFilter>(
                  options: const [
                    (RegistrationFilter.needsReview, 'Needs review'),
                    (RegistrationFilter.waiting, 'Waiting for hospital'),
                    (RegistrationFilter.all, 'All registrations'),
                  ],
                  selected: _filter,
                  onSelected: (f) => setState(() => _filter = f),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(adminHospitalsProvider),
                  loadingMessage: 'Loading registrations...',
                  data: (all) {
                    final list = all
                        .where((h) => h.approvalStatus != 'Approved')
                        .where((h) => switch (_filter) {
                              RegistrationFilter.needsReview => h.needsReview,
                              RegistrationFilter.waiting => h.waitingForHospital,
                              RegistrationFilter.all => true,
                            })
                        .where((h) =>
                            term.isEmpty ||
                            [h.name, h.city ?? '', h.email, h.registrationNumber ?? '', h.licenseNumber].any((s) => s.toLowerCase().contains(term)))
                        .toList();
                    if (list.isEmpty) {
                      return const EmptyView(icon: Icons.domain_add_outlined, message: 'No hospital registrations here.');
                    }
                    return Column(
                      children: [
                        for (final h in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: Card(
                              child: ListTile(
                                key: Key('registration-${h.hospitalId}'),
                                onTap: () => context.push('${AppRoutes.adminRegistrations}/${h.hospitalId}'),
                                leading: const Icon(Icons.local_hospital_outlined, color: AppColors.red600),
                                title: Text(h.name, style: const TextStyle(fontWeight: FontWeight.w700)),
                                subtitle: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text('${h.city ?? h.address} · submitted ${Fmt.date(h.createdAt)}'),
                                    const SizedBox(height: 4),
                                    StatusBadge(h.statusLabel, variant: registrationVariant(h.approvalStatus)),
                                  ],
                                ),
                                trailing: const Icon(Icons.chevron_right),
                              ),
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
