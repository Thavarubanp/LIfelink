import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/config/constants.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import 'blood_request_models.dart';
import 'blood_request_repository.dart';

class PublicRequestsScreen extends ConsumerStatefulWidget {
  const PublicRequestsScreen({super.key});

  @override
  ConsumerState<PublicRequestsScreen> createState() => _PublicRequestsScreenState();
}

class _PublicRequestsScreenState extends ConsumerState<PublicRequestsScreen> {
  String? _group;
  String _query = '';
  bool _expiringSoon = false;

  // The API already exposes this React client parameter. The mobile chip uses the common 24-hour window.
  PublicRequestFilter get _filter => (bloodGroup: _group, expiringWithinHours: _expiringSoon ? 24 : null);

  Future<void> _refresh() async {
    ref.invalidate(publicBloodRequestsProvider(_filter));
    await ref.read(publicBloodRequestsProvider(_filter).future).then((_) {}, onError: (_) {});
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(publicBloodRequestsProvider(_filter));
    final user = ref.watch(authControllerProvider).user;
    final mayCreate = user?.primaryRole == Roles.user;
    final term = _query.trim().toLowerCase();
    return Scaffold(
      appBar: AppBar(
        title: const Text('Available requests'),
        actions: [
          if (mayCreate)
            IconButton(
              tooltip: 'Create blood request',
              onPressed: () => context.push(AppRoutes.createRequest),
              icon: const Icon(Icons.add_circle_outline),
            ),
        ],
      ),
      body: RefreshableScroll(
        onRefresh: _refresh,
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SearchField(hint: 'Search hospital, blood group or reason', onChanged: (v) => setState(() => _query = v)),
                const SizedBox(height: 10),
                FilterChips<String?>(
                  options: [(null, 'All groups'), for (final group in AppConstants.bloodGroups) (group, group)],
                  selected: _group,
                  onSelected: (value) => setState(() => _group = value),
                ),
                const SizedBox(height: 8),
                Align(
                  alignment: Alignment.centerLeft,
                  child: FilterChip(
                    label: const Text('Expiring soon'),
                    selected: _expiringSoon,
                    onSelected: (value) => setState(() => _expiringSoon = value),
                  ),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(publicBloodRequestsProvider(_filter)),
                  loadingMessage: 'Loading available blood requests...',
                  data: (all) {
                    final list = all
                        .where((r) => term.isEmpty ||
                            r.hospitalName.toLowerCase().contains(term) ||
                            r.bloodGroup.toLowerCase().contains(term) ||
                            r.reason.toLowerCase().contains(term))
                        .toList();
                    if (list.isEmpty) {
                      return const EmptyView(icon: Icons.bloodtype_outlined, message: 'No available requests match these filters.');
                    }
                    return Column(
                      children: [
                        for (final request in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: _PublicRequestCard(request: request),
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

class _PublicRequestCard extends StatelessWidget {
  const _PublicRequestCard({required this.request});
  final BloodRequest request;

  @override
  Widget build(BuildContext context) => Card(
        child: InkWell(
          borderRadius: BorderRadius.circular(16),
          onTap: () => context.push(AppRoutes.requestDetail(request.id)),
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    BloodGroupBadge(request.bloodGroup, large: true),
                    const SizedBox(width: 10),
                    Expanded(child: Text(request.hospitalName, style: const TextStyle(fontWeight: FontWeight.w700))),
                    StatusBadge(request.priority,
                        variant: request.isCritical ? BadgeVariant.danger : BadgeVariant.warning),
                  ],
                ),
                const SizedBox(height: 10),
                Text('${request.remainingUnits} of ${request.unitsRequired} still needed'),
                Text('${request.fulfilledUnits} donated, ${request.reservedUnits} reserved',
                    style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                if (request.reason.isNotEmpty) ...[
                  const SizedBox(height: 4),
                  Text(request.reason, maxLines: 2, overflow: TextOverflow.ellipsis),
                ],
                const SizedBox(height: 6),
                Row(
                  children: [
                    Expanded(
                      child: Text('Open until ${Fmt.date(request.expiryDate)}',
                          style: const TextStyle(fontSize: 11, color: AppColors.slate500)),
                    ),
                    if (!request.isAcceptingDonors) const StatusBadge('All slots reserved', variant: BadgeVariant.info),
                    const Icon(Icons.chevron_right),
                  ],
                ),
              ],
            ),
          ),
        ),
      );
}
