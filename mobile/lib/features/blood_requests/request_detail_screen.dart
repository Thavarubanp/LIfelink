import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import 'blood_request_models.dart';
import 'blood_request_repository.dart';
import 'donor_home_screen.dart';

class RequestDetailScreen extends ConsumerStatefulWidget {
  const RequestDetailScreen({super.key, required this.requestId});
  final String requestId;

  @override
  ConsumerState<RequestDetailScreen> createState() =>
      _RequestDetailScreenState();
}

class _RequestDetailScreenState extends ConsumerState<RequestDetailScreen> {
  String _bloodGroup = '';
  bool _accepting = false;
  String? _error;

  void _reload() {
    ref.invalidate(bloodRequestProvider(widget.requestId));
    final userId = ref.read(authControllerProvider).user?.userId;
    if (userId != null) ref.invalidate(donorProfileProvider(userId));
    ref.invalidate(publicBloodRequestsProvider);
  }

  Future<void> _accept(DonorProfileSummary? profile) async {
    final group = profile?.bloodGroupConfirmed == true
        ? profile!.bloodGroup ?? ''
        : _bloodGroup;
    if (group.isEmpty) {
      setState(() => _error = 'Select your blood group before accepting.');
      return;
    }
    setState(() {
      _accepting = true;
      _error = null;
    });
    try {
      final acceptance = await ref
          .read(bloodRequestRepositoryProvider)
          .accept(widget.requestId, group);
      if (!mounted) return;
      showSnack(
        context,
        'Next, complete your health screening interview.',
        title: 'Thank you!',
        type: SnackType.success,
      );
      context.go(AppRoutes.screening(acceptance.id));
    } catch (e) {
      final api = ApiError.from(e);
      if (mounted) setState(() => _error = api.message);
      if (api.isConflict) _reload();
    } finally {
      if (mounted) setState(() => _accepting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final requestValue = ref.watch(bloodRequestProvider(widget.requestId));
    final user = ref.watch(authControllerProvider).user;
    final donor = user?.isDonorCapable == true;
    final profileValue = donor && user != null
        ? ref.watch(donorProfileProvider(user.userId))
        : null;
    return Scaffold(
      appBar: AppBar(title: const Text('Blood request')),
      body: RefreshableScroll(
        onRefresh: () async {
          _reload();
          await ref
              .read(bloodRequestProvider(widget.requestId).future)
              .then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: requestValue,
          onRetry: _reload,
          loadingMessage: 'Loading request details...',
          data: (request) => ContentWidth(
            maxWidth: 760,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _RequestSummary(request: request),
                  const SizedBox(height: 12),
                  if (donor)
                    profileValue!.when(
                      loading: () => const LoadingView(
                        message: 'Loading your eligibility...',
                      ),
                      error: (e, _) => ErrorView(error: e, onRetry: _reload),
                      data: (profile) => _acceptCard(request, profile),
                    )
                  else
                    const InfoBanner(
                      'This account is not eligible to participate as a donor.',
                      color: AppColors.blue600,
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _acceptCard(BloodRequest request, DonorProfileSummary profile) {
    if (_bloodGroup.isEmpty && profile.bloodGroup?.isNotEmpty == true) {
      _bloodGroup = profile.bloodGroup!;
    }
    final blocked = request.status != 'Approved'
        ? 'This request is ${request.status.toLowerCase()} and is not accepting donors.'
        : !request.isAcceptingDonors
        ? 'All remaining donation slots are reserved. New acceptances are paused until a slot is released.'
        : !profile.eligibleAt(DateTime.now())
        ? 'You can donate again from ${Fmt.date(profile.nextEligibleDonationDate)} (120 days after your last donation).'
        : null;
    return SectionCard(
      title: 'Accept and donate',
      icon: Icons.favorite_outline,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          DropdownButtonFormField<String>(
            initialValue: AppConstants.bloodGroups.contains(_bloodGroup)
                ? _bloodGroup
                : null,
            decoration: InputDecoration(
              labelText: profile.bloodGroupConfirmed
                  ? 'Your confirmed blood group'
                  : 'Your blood group',
            ),
            items: [
              for (final group in AppConstants.bloodGroups)
                DropdownMenuItem(value: group, child: Text(group)),
            ],
            onChanged: profile.bloodGroupConfirmed
                ? null
                : (value) => setState(() => _bloodGroup = value ?? ''),
          ),
          if (blocked != null) ...[
            const SizedBox(height: 10),
            InfoBanner(blocked, color: AppColors.amber600),
          ],
          if (_error != null) ...[
            const SizedBox(height: 10),
            FormErrorBox(_error!),
          ],
          const SizedBox(height: 12),
          BusyButton(
            label: 'Accept request',
            icon: Icons.volunteer_activism,
            busy: _accepting,
            onPressed: blocked == null ? () => _accept(profile) : null,
          ),
        ],
      ),
    );
  }
}

class _RequestSummary extends StatelessWidget {
  const _RequestSummary({required this.request});
  final BloodRequest request;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      SectionCard(
        title: request.hospitalName,
        icon: Icons.local_hospital_outlined,
        trailing: StatusBadge(
          request.priority,
          variant: request.isCritical
              ? BadgeVariant.danger
              : BadgeVariant.warning,
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                BloodGroupBadge(request.bloodGroup, large: true),
                const SizedBox(width: 10),
                StatusBadge(
                  request.status,
                  variant: requestStatusVariant(request.status),
                ),
                if (!request.isAcceptingDonors) ...[
                  const SizedBox(width: 6),
                  const StatusBadge(
                    'All slots reserved',
                    variant: BadgeVariant.info,
                  ),
                ],
              ],
            ),
            const SizedBox(height: 14),
            Wrap(
              spacing: 24,
              runSpacing: 12,
              children: [
                LabeledValue('Units required', '${request.unitsRequired}'),
                LabeledValue('Donated', '${request.fulfilledUnits}'),
                LabeledValue('Reserved', '${request.reservedUnits}'),
              ],
            ),
            if (request.reason.isNotEmpty) ...[
              const SizedBox(height: 14),
              LabeledValue('Clinical reason', request.reason),
            ],
          ],
        ),
      ),
      const SizedBox(height: 12),
      const InfoBanner(
        'After accepting, complete the health screening interview. The assigned doctor reviews your answers and decides whether you can donate.',
        color: AppColors.blue600,
        icon: Icons.medical_services_outlined,
      ),
    ],
  );
}
