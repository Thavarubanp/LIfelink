import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/reply_sheet.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import '../doctors/doctor_repository.dart';
import 'verification_models.dart';
import 'verification_repository.dart';

/// Hospital staff: requests sent to the hospital. Pending ones are verified (a doctor is assigned) or rejected with a
/// message the creator sees, as on the web's Verify Blood Requests page.
class VerifyRequestsScreen extends ConsumerStatefulWidget {
  const VerifyRequestsScreen({super.key});

  @override
  ConsumerState<VerifyRequestsScreen> createState() =>
      _VerifyRequestsScreenState();
}

class _VerifyRequestsScreenState extends ConsumerState<VerifyRequestsScreen> {
  String? _status = 'Pending';
  String _search = '';

  void _reload() => ref.invalidate(hospitalRequestsProvider);

  Future<void> _verify(ClinicalRequest r) async {
    final done = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => AssignDoctorSheet(request: r),
    );
    if (done == true) _reload();
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
    if (sent && mounted) {
      showSnack(
        context,
        'The creator will see your rejection message.',
        title: 'Request rejected',
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(hospitalRequestsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Verify blood requests')),
      body: RefreshableScroll(
        onRefresh: () async {
          _reload();
          await ref
              .read(hospitalRequestsProvider.future)
              .then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SearchField(
                  hint: 'Search ID, blood group, reason, creator',
                  onChanged: (v) => setState(() => _search = v),
                ),
                const SizedBox(height: 10),
                FilterChips<String?>(
                  options: requestStatusFilters,
                  selected: _status,
                  onSelected: (s) => setState(() => _status = s),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: _reload,
                  loadingMessage: 'Loading requests...',
                  data: (all) {
                    final pending = all
                        .where((r) => r.status == 'Pending')
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
                          '$pending request(s) waiting for verification',
                          style: const TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: 8),
                        if (list.isEmpty)
                          const EmptyView(
                            icon: Icons.fact_check_outlined,
                            message: 'No blood requests match.',
                          )
                        else
                          for (final r in list)
                            Padding(
                              padding: const EdgeInsets.only(bottom: 10),
                              child: RequestCard(
                                request: r,
                                actions: [
                                  if (r.canVerify) ...[
                                    FilledButton.icon(
                                      key: Key('verify-${r.id}'),
                                      style: FilledButton.styleFrom(
                                        backgroundColor: AppColors.emerald600,
                                        minimumSize: const Size(0, 40),
                                      ),
                                      onPressed: () => _verify(r),
                                      icon: const Icon(
                                        Icons.how_to_reg,
                                        size: 18,
                                      ),
                                      label: const Text('Verify'),
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
                                  if (const {
                                    'Approved',
                                    'Completed',
                                  }.contains(r.status))
                                    OutlinedButton.icon(
                                      style: OutlinedButton.styleFrom(
                                        minimumSize: const Size(0, 40),
                                      ),
                                      onPressed: () => context.push(
                                        AppRoutes.hospitalRequestDonors(r.id),
                                      ),
                                      icon: const Icon(
                                        Icons.people_outline,
                                        size: 18,
                                      ),
                                      label: const Text('Donors'),
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

/// A blood request card shared by the hospital and doctor screens.
class RequestCard extends StatelessWidget {
  const RequestCard({
    super.key,
    required this.request,
    this.actions = const [],
    this.showHospital = false,
  });

  final ClinicalRequest request;
  final List<Widget> actions;
  final bool showHospital;

  @override
  Widget build(BuildContext context) {
    final r = request;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                BloodGroupBadge(r.bloodGroup),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    '${r.unitsRequired} unit(s) · #${r.shortId}',
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ),
                StatusBadge(
                  r.priority,
                  variant: r.isCritical
                      ? BadgeVariant.danger
                      : BadgeVariant.warning,
                ),
              ],
            ),
            const SizedBox(height: 6),
            if (r.reason.isNotEmpty) Text(r.reason),
            Text(
              [
                if (showHospital && r.hospitalName != null) r.hospitalName!,
                if (r.createdByName != null) 'by ${r.createdByName}',
                Fmt.sriLankaDateTime(r.createdAt),
              ].join(' · '),
              style: const TextStyle(fontSize: 12, color: AppColors.slate500),
            ),
            Text(
              '${r.fulfilledUnits} donated, ${r.reservedUnits} reserved',
              style: const TextStyle(fontSize: 12, color: AppColors.slate500),
            ),
            if (r.assignedDoctorName != null)
              Text(
                'Doctor: ${r.assignedDoctorName}',
                style: const TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                ),
              ),
            const SizedBox(height: 6),
            Wrap(
              spacing: 6,
              runSpacing: 4,
              children: [
                StatusBadge(r.status, variant: requestStatusVariant(r.status)),
                if (r.isSuspended) SuspendedBadge(reason: r.suspensionReason),
                if (r.pendingHospitalDonations > 0)
                  StatusBadge(
                    '${r.pendingHospitalDonations} hospital donation(s) waiting',
                    variant: BadgeVariant.info,
                  ),
              ],
            ),
            if (r.isSuspended)
              const Padding(
                padding: EdgeInsets.only(top: 6),
                child: Text(
                  'Suspended by the administrator: nobody can act on it until the suspension is lifted.',
                  style: TextStyle(fontSize: 12, color: AppColors.rose600),
                ),
              ),
            if (r.status == 'Rejected' &&
                (r.rejectionReason?.isNotEmpty ?? false))
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text(
                  'Rejection message: ${r.rejectionReason}',
                  style: const TextStyle(
                    fontSize: 12,
                    color: AppColors.rose600,
                  ),
                ),
              ),
            if (actions.isNotEmpty) ...[
              const SizedBox(height: 10),
              Wrap(spacing: 8, runSpacing: 8, children: actions),
            ],
          ],
        ),
      ),
    );
  }
}

/// Assign one of the hospital's doctors. Doctors pending first login cannot be assigned (the API refuses them too).
class AssignDoctorSheet extends ConsumerStatefulWidget {
  const AssignDoctorSheet({super.key, required this.request});

  final ClinicalRequest request;

  @override
  ConsumerState<AssignDoctorSheet> createState() => _AssignDoctorSheetState();
}

class _AssignDoctorSheetState extends ConsumerState<AssignDoctorSheet> {
  String? _doctorId;
  bool _busy = false;
  String? _error;

  Future<void> _save() async {
    if (_doctorId == null) {
      setState(
        () => _error = 'Choose the doctor who will review this request.',
      );
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref
          .read(verificationRepositoryProvider)
          .verify(widget.request.id, _doctorId!);
      if (!mounted) return;
      showSnack(
        context,
        'The assigned doctor has been notified.',
        type: SnackType.success,
        title: 'Request verified',
      );
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      if (api.isConflict) {
        showSnack(
          context,
          api.message,
          type: SnackType.error,
          title: 'Not verified',
        );
        Navigator.of(context).pop(true);
      } else {
        setState(() => _error = api.message);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final doctors = ref.watch(hospitalDoctorsProvider);
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        MediaQuery.viewInsetsOf(context).bottom + 20,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Verify request #${widget.request.shortId}',
              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 6),
            const Text(
              'Assign the doctor who approves or rejects it. Only doctors who have completed their first sign-in can be assigned.',
              style: TextStyle(fontSize: 12),
            ),
            const SizedBox(height: 12),
            AsyncView(
              value: doctors,
              onRetry: () => ref.invalidate(hospitalDoctorsProvider),
              data: (all) {
                final eligible = all.where((d) => d.canBeAssigned).toList();
                final pendingFirstLogin = all
                    .where((d) => !d.canBeAssigned)
                    .length;
                if (eligible.isEmpty) {
                  return InfoBanner(
                    pendingFirstLogin > 0
                        ? 'No doctor can be assigned yet: $pendingFirstLogin doctor(s) have not completed their first sign-in.'
                        : 'Add a doctor first (More → Doctors).',
                  );
                }
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    RadioGroup<String>(
                      groupValue: _doctorId,
                      onChanged: (v) => setState(() => _doctorId = v),
                      child: Column(
                        children: [
                          for (final d in eligible)
                            RadioListTile<String>(
                              key: Key('doctor-${d.doctorId}'),
                              value: d.doctorId,
                              title: Text(d.name),
                              subtitle: Text(
                                [
                                  d.specialization,
                                  'SLMC ${d.licenseNumber}',
                                ].where((s) => s.isNotEmpty).join(' · '),
                              ),
                            ),
                        ],
                      ),
                    ),
                    if (pendingFirstLogin > 0)
                      Text(
                        '$pendingFirstLogin doctor(s) pending first login are not listed.',
                        style: const TextStyle(
                          fontSize: 12,
                          color: AppColors.slate500,
                        ),
                      ),
                  ],
                );
              },
            ),
            const SizedBox(height: 12),
            if (_error != null) ...[
              FormErrorBox(_error!),
              const SizedBox(height: 12),
            ],
            BusyButton(
              key: const Key('assign-submit'),
              label: 'Verify and assign',
              busy: _busy,
              onPressed: _save,
            ),
          ],
        ),
      ),
    );
  }
}
