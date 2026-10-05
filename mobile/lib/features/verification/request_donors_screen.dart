import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/config/constants.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/contact_launcher.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/reply_sheet.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import 'verification_models.dart';
import 'verification_repository.dart';

/// The donors and donating hospitals of one request, with contact buttons (call / email). The doctor decides hospital
/// donations; the doctor or the hospital's staff record a donation (tested blood group) or release a reserved slot.
class RequestDonorsScreen extends ConsumerWidget {
  const RequestDonorsScreen({super.key, required this.requestId, required this.isDoctor});

  final String requestId;

  /// Only the request's assigned doctor approves or rejects hospital donations.
  final bool isDoctor;

  void _reload(WidgetRef ref) {
    ref.invalidate(requestAcceptancesProvider(requestId));
    ref.invalidate(assignedRequestsProvider);
    ref.invalidate(hospitalRequestsProvider);
  }

  Future<void> _hospitalDecision(BuildContext context, WidgetRef ref, RequestAcceptance a, bool approve) async {
    final repo = ref.read(verificationRepositoryProvider);
    final sent = await showReplySheet(
      context,
      title: approve ? 'Approve hospital donation' : 'Reject hospital donation',
      subtitle: approve
          ? '${a.packets.length} packet(s) from ${a.donorName} count as donated at once.'
          : 'The packets return to ${a.donorName}. Your reason is shown to the hospital.',
      label: approve ? 'Clinical notes (optional)' : 'Reason for rejecting',
      submitLabel: approve ? 'Confirm approval' : 'Confirm rejection',
      minLength: approve ? 0 : 1,
      maxLength: 500,
      requiredMessage: 'A reason is required to reject.',
      allowAttachment: false,
      destructive: !approve,
      onSubmit: (text, _) => approve ? repo.approveHospitalDonation(a.id, text) : repo.rejectHospitalDonation(a.id, text),
    );
    _reload(ref);
    if (sent && context.mounted) {
      showSnack(context, approve ? 'The packets count as donated.' : 'The packets were returned to the hospital.',
          type: approve ? SnackType.success : SnackType.info, title: approve ? 'Donation approved' : 'Donation rejected');
    }
  }

  Future<void> _release(BuildContext context, WidgetRef ref, RequestAcceptance a) async {
    final sent = await showReplySheet(
      context,
      title: 'Release reservation · ${a.donorName}',
      subtitle: 'For a no-show or a donor who cannot proceed. The slot becomes available to other donors.',
      label: 'Reason (shown to the donor)',
      submitLabel: 'Release slot',
      maxLength: 500,
      requiredMessage: 'A reason is required.',
      allowAttachment: false,
      destructive: true,
      onSubmit: (text, _) => ref.read(verificationRepositoryProvider).release(a.id, text),
    );
    _reload(ref);
    if (sent && context.mounted) showSnack(context, 'The slot is available to other donors again.', title: 'Reservation released');
  }

  Future<void> _record(BuildContext context, WidgetRef ref, RequestAcceptance a) async {
    final done = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => RecordDonationSheet(requestId: requestId, acceptanceId: a.id, donorName: a.donorName),
    );
    _reload(ref);
    if (done == true && context.mounted) {
      showSnack(context, 'The donated unit now counts towards the request.', type: SnackType.success, title: 'Donation recorded');
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(requestAcceptancesProvider(requestId));
    return Scaffold(
      appBar: AppBar(title: Text('Donors · #${requestId.length >= 8 ? requestId.substring(0, 8) : requestId}')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(requestAcceptancesProvider(requestId));
          await ref.read(requestAcceptancesProvider(requestId).future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(requestAcceptancesProvider(requestId)),
          loadingMessage: 'Loading donors...',
          data: (list) => list.isEmpty
              ? const EmptyView(icon: Icons.people_outline, message: 'No donor or hospital has accepted this request yet.')
              : ContentWidth(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        for (final a in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: _AcceptanceCard(
                              a: a,
                              isDoctor: isDoctor,
                              onApprove: () => _hospitalDecision(context, ref, a, true),
                              onReject: () => _hospitalDecision(context, ref, a, false),
                              onRecord: () => _record(context, ref, a),
                              onRelease: () => _release(context, ref, a),
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

class _AcceptanceCard extends StatelessWidget {
  const _AcceptanceCard({
    required this.a,
    required this.isDoctor,
    required this.onApprove,
    required this.onReject,
    required this.onRecord,
    required this.onRelease,
  });

  final RequestAcceptance a;
  final bool isDoctor;
  final VoidCallback onApprove;
  final VoidCallback onReject;
  final VoidCallback onRecord;
  final VoidCallback onRelease;

  @override
  Widget build(BuildContext context) {
    final compact = OutlinedButton.styleFrom(minimumSize: const Size(0, 40));
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(children: [
              Icon(a.isHospitalDonation ? Icons.local_hospital_outlined : Icons.person_outline, color: AppColors.red600),
              const SizedBox(width: 8),
              Expanded(child: Text(a.donorName, style: const TextStyle(fontWeight: FontWeight.w700))),
              StatusBadge(a.statusLabel, variant: acceptanceVariant(a.status)),
            ]),
            const SizedBox(height: 4),
            Text('${a.donorPhone.isEmpty ? '' : '${a.donorPhone} · '}${a.donorEmail}', style: const TextStyle(fontSize: 12)),
            Text('Accepted ${Fmt.sriLankaDateTime(a.acceptedAt)}', style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
            if (a.rejectionReason?.isNotEmpty == true && a.status != 'Matched')
              Text('Reason: ${a.rejectionReason}', style: const TextStyle(fontSize: 12, color: AppColors.rose600)),
            if (a.packets.isNotEmpty) ...[
              const SizedBox(height: 6),
              for (final p in a.packets)
                Text('${p.trackingNumber} · ${p.bloodGroup} · expires ${Fmt.date(p.expiryDate)}',
                    style: const TextStyle(fontFamily: 'monospace', fontSize: 12)),
            ],
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                // Device feature: contact the donor
                OutlinedButton.icon(
                  key: Key('call-${a.id}'),
                  style: compact,
                  onPressed: a.donorPhone.isEmpty ? null : () => ContactLauncher.call(context, a.donorPhone),
                  icon: const Icon(Icons.call_outlined, size: 18),
                  label: const Text('Call'),
                ),
                OutlinedButton.icon(
                  key: Key('email-${a.id}'),
                  style: compact,
                  onPressed: a.donorEmail.isEmpty ? null : () => ContactLauncher.email(context, a.donorEmail, subject: 'LifeLink blood donation'),
                  icon: const Icon(Icons.mail_outline, size: 18),
                  label: const Text('Email'),
                ),
                if (isDoctor && a.awaitsHospitalDecision) ...[
                  FilledButton.icon(
                    key: Key('hospital-approve-${a.id}'),
                    style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600, minimumSize: const Size(0, 40)),
                    onPressed: onApprove,
                    icon: const Icon(Icons.check, size: 18),
                    label: const Text('Approve'),
                  ),
                  OutlinedButton.icon(
                    key: Key('hospital-reject-${a.id}'),
                    style: OutlinedButton.styleFrom(foregroundColor: AppColors.rose600, minimumSize: const Size(0, 40)),
                    onPressed: onReject,
                    icon: const Icon(Icons.close, size: 18),
                    label: const Text('Reject'),
                  ),
                ],
                if (a.isReserved) ...[
                  FilledButton.icon(
                    key: Key('record-${a.id}'),
                    style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600, minimumSize: const Size(0, 40)),
                    onPressed: onRecord,
                    icon: const Icon(Icons.assignment_turned_in_outlined, size: 18),
                    label: const Text('Record donation'),
                  ),
                  OutlinedButton.icon(
                    key: Key('release-${a.id}'),
                    style: compact,
                    onPressed: onRelease,
                    icon: const Icon(Icons.undo, size: 18),
                    label: const Text('Release slot'),
                  ),
                ],
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// Record a donation with the blood group tested at donation (it becomes the donor's confirmed group).
class RecordDonationSheet extends ConsumerStatefulWidget {
  const RecordDonationSheet({super.key, required this.requestId, required this.acceptanceId, required this.donorName, this.initialGroup});

  final String requestId;
  final String acceptanceId;
  final String donorName;
  final String? initialGroup;

  @override
  ConsumerState<RecordDonationSheet> createState() => _RecordDonationSheetState();
}

class _RecordDonationSheetState extends ConsumerState<RecordDonationSheet> {
  late String? _group = widget.initialGroup;
  bool _busy = false;
  String? _error;

  Future<void> _save() async {
    if (_group == null) {
      setState(() => _error = 'Select the tested blood group.');
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(verificationRepositoryProvider).recordDonation(widget.requestId, widget.acceptanceId, _group!);
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      if (api.isConflict) {
        showSnack(context, api.message, type: SnackType.error, title: 'Not recorded');
        Navigator.of(context).pop(false);
      } else {
        setState(() => _error = api.message);
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Record donation · ${widget.donorName}', style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
            const SizedBox(height: 6),
            const Text('Confirm the blood group tested at donation. It becomes the donor\'s confirmed blood group.', style: TextStyle(fontSize: 12)),
            const SizedBox(height: 14),
            DropdownButtonFormField<String>(
              key: const Key('tested-group'),
              initialValue: _group,
              decoration: const InputDecoration(labelText: 'Tested blood group'),
              items: [for (final g in AppConstants.bloodGroups) DropdownMenuItem(value: g, child: Text(g))],
              onChanged: (v) => setState(() => _group = v),
            ),
            const SizedBox(height: 14),
            if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
            BusyButton(key: const Key('record-submit'), label: 'Record donation', busy: _busy, onPressed: _save),
          ],
        ),
      );
}
