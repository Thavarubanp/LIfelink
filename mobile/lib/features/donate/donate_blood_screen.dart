import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/config/constants.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../inventory/inventory_repository.dart';
import '../inventory/packet_picker.dart';
import 'donate_repository.dart';

BadgeVariant donationVariant(HospitalDonation d) => d.requestDeleted
    ? BadgeVariant.neutral
    : switch (d.status) {
        'Accepted' => BadgeVariant.warning,
        'Matched' => BadgeVariant.success,
        'Rejected' => BadgeVariant.danger,
        _ => BadgeVariant.neutral,
      };

/// Hospital staff donate packets from their inventory to public blood requests of other hospitals; the doctor
/// assigned to the request approves (packets donated) or rejects (packets return). No AI screening is involved.
class DonateBloodScreen extends ConsumerStatefulWidget {
  const DonateBloodScreen({super.key});

  @override
  ConsumerState<DonateBloodScreen> createState() => _DonateBloodScreenState();
}

class _DonateBloodScreenState extends ConsumerState<DonateBloodScreen> {
  String? _group;
  String _search = '';
  String? _withdrawing;

  void _reload() {
    ref.invalidate(publicRequestsProvider);
    ref.invalidate(myDonationsProvider);
    ref.invalidate(availablePacketsProvider);
    ref.invalidate(hospitalInventoryProvider);
    ref.invalidate(packetsProvider);
  }

  Future<void> _donate(PublicRequest r) async {
    final done = await showModalBottomSheet<bool>(context: context, isScrollControlled: true, builder: (_) => _DonateSheet(request: r));
    if (done == true) _reload();
  }

  Future<void> _withdraw(HospitalDonation d) async {
    if (_withdrawing != null) return;
    final ok = await confirmDialog(context,
        title: 'Withdraw donation', message: 'Withdraw this donation? The packets return to your available stock.', confirmLabel: 'Withdraw');
    if (!ok) return;
    setState(() => _withdrawing = d.id);
    try {
      await ref.read(donateRepositoryProvider).withdraw(d.id);
      if (mounted) showSnack(context, 'The packets are back in your inventory.', title: 'Donation withdrawn');
      _reload();
    } catch (e) {
      final api = ApiError.from(e);
      if (mounted) showSnack(context, api.message, type: SnackType.error, title: 'Could not withdraw');
      // The doctor decided at the same moment: show the current status
      if (api.isConflict) _reload();
    } finally {
      if (mounted) setState(() => _withdrawing = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final requests = ref.watch(publicRequestsProvider(_group));
    final donations = ref.watch(myDonationsProvider);
    final term = _search.trim().toLowerCase();
    return Scaffold(
      appBar: AppBar(title: const Text('Donate blood')),
      body: RefreshableScroll(
        onRefresh: () async {
          _reload();
          await ref.read(publicRequestsProvider(_group).future).catchError((_) => <PublicRequest>[]);
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text('Public blood requests from other hospitals. Donate packets from your inventory; '
                    'the doctor assigned to the request approves the donation.',
                    style: TextStyle(fontSize: 12, color: AppColors.slate500)),
                const SizedBox(height: 12),
                SearchField(hint: 'Search hospital, blood group, reason', onChanged: (v) => setState(() => _search = v)),
                const SizedBox(height: 10),
                FilterChips<String?>(
                  options: [(null, 'All groups'), for (final g in AppConstants.bloodGroups) (g, g)],
                  selected: _group,
                  onSelected: (g) => setState(() => _group = g),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: requests,
                  onRetry: () => ref.invalidate(publicRequestsProvider(_group)),
                  loadingMessage: 'Loading blood requests...',
                  data: (all) {
                    final pending = donations.value ?? const <HospitalDonation>[];
                    final list = all
                        .where((r) =>
                            term.isEmpty ||
                            r.hospitalName.toLowerCase().contains(term) ||
                            r.bloodGroup.toLowerCase().contains(term) ||
                            r.reason.toLowerCase().contains(term))
                        .toList();
                    if (list.isEmpty) {
                      return const EmptyView(
                        icon: Icons.volunteer_activism_outlined,
                        message: 'No public blood requests from other hospitals match this filter.',
                      );
                    }
                    return Column(
                      children: [
                        for (final r in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: _RequestCard(
                              r: r,
                              awaitingDoctor: pending.any((d) => d.bloodRequestId == r.id && d.status == 'Accepted'),
                              onDonate: () => _donate(r),
                            ),
                          ),
                      ],
                    );
                  },
                ),
                const SizedBox(height: 20),
                const Text("My hospital's donations", style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                const SizedBox(height: 10),
                AsyncView(
                  value: donations,
                  onRetry: () => ref.invalidate(myDonationsProvider),
                  data: (list) => list.isEmpty
                      ? const EmptyView(icon: Icons.history, message: 'Your hospital has not donated to a blood request yet.')
                      : Column(
                          children: [
                            for (final d in list)
                              Padding(
                                padding: const EdgeInsets.only(bottom: 10),
                                child: _DonationCard(d: d, withdrawing: _withdrawing == d.id, onWithdraw: () => _withdraw(d)),
                              ),
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

class _RequestCard extends StatelessWidget {
  const _RequestCard({required this.r, required this.awaitingDoctor, required this.onDonate});

  final PublicRequest r;
  final bool awaitingDoctor;
  final VoidCallback onDonate;

  @override
  Widget build(BuildContext context) => Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  BloodGroupBadge(r.bloodGroup),
                  const SizedBox(width: 8),
                  Expanded(child: Text(r.hospitalName, style: const TextStyle(fontWeight: FontWeight.w700))),
                  StatusBadge(r.priority, variant: r.isCritical ? BadgeVariant.danger : BadgeVariant.warning),
                ],
              ),
              const SizedBox(height: 8),
              Text('${r.remainingUnits} of ${r.unitsRequired} still needed · ${r.fulfilledUnits} donated, ${r.reservedUnits} reserved'),
              if (r.reason.isNotEmpty) Text(r.reason, style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
              if (!r.isAcceptingDonors)
                const Padding(padding: EdgeInsets.only(top: 6), child: StatusBadge('All slots reserved', variant: BadgeVariant.info)),
              const SizedBox(height: 10),
              awaitingDoctor
                  ? const StatusBadge('Your donation awaits the doctor', variant: BadgeVariant.warning)
                  : FilledButton.icon(
                      onPressed: r.isAcceptingDonors && r.freeSlots > 0 ? onDonate : null,
                      icon: const Icon(Icons.volunteer_activism, size: 18),
                      label: const Text('Donate from inventory'),
                    ),
            ],
          ),
        ),
      );
}

class _DonationCard extends StatelessWidget {
  const _DonationCard({required this.d, required this.withdrawing, required this.onWithdraw});

  final HospitalDonation d;
  final bool withdrawing;
  final VoidCallback onWithdraw;

  @override
  Widget build(BuildContext context) => Card(
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  if (!d.requestDeleted) ...[BloodGroupBadge(d.requestBloodGroup), const SizedBox(width: 8)],
                  Expanded(
                    child: Text(
                      d.requestDeleted
                          ? '${d.trackingNumbers.length} packet(s) offered to a deleted request'
                          : '${d.trackingNumbers.length} packet(s) to ${d.hospitalName}',
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              StatusBadge(d.statusLabel, variant: donationVariant(d)),
              const SizedBox(height: 6),
              Text(
                d.requestDeleted
                    ? 'Offered ${Fmt.sriLankaDateTime(d.acceptedAt)} · closed: the request was deleted by its creator.'
                    : 'Offered ${Fmt.sriLankaDateTime(d.acceptedAt)}',
                style: const TextStyle(fontSize: 12, color: AppColors.slate500),
              ),
              if (d.trackingNumbers.isNotEmpty)
                Text(d.trackingNumbers.join(', '), style: const TextStyle(fontFamily: 'monospace', fontSize: 12)),
              if (!d.requestDeleted && d.status != 'Matched' && d.rejectionReason?.isNotEmpty == true)
                Text('Reason: ${d.rejectionReason}', style: const TextStyle(fontSize: 12, color: AppColors.rose600)),
              if (d.status == 'Accepted') ...[
                const SizedBox(height: 8),
                OutlinedButton.icon(
                  style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40)),
                  onPressed: withdrawing ? null : onWithdraw,
                  icon: const Icon(Icons.undo, size: 18),
                  label: const Text('Withdraw'),
                ),
              ],
            ],
          ),
        ),
      );
}

class _DonateSheet extends ConsumerStatefulWidget {
  const _DonateSheet({required this.request});

  final PublicRequest request;

  @override
  ConsumerState<_DonateSheet> createState() => _DonateSheetState();
}

class _DonateSheetState extends ConsumerState<_DonateSheet> {
  List<String> _ids = [];
  bool _saving = false;
  String? _error;

  Future<void> _submit() async {
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(donateRepositoryProvider).donate(widget.request.id, _ids);
      if (!mounted) return;
      showSnack(context, '${_ids.length} packet(s) are held for this request until the assigned doctor approves.',
          type: SnackType.success, title: 'Donation offered');
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      if (api.isConflict) {
        // The request or the selected packets changed at the same moment: show the current state
        showSnack(context, api.message, type: SnackType.error, title: 'Could not donate');
        Navigator.of(context).pop(true);
      } else {
        setState(() => _error = api.message);
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final r = widget.request;
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Donate ${r.bloodGroup} to ${r.hospitalName}', style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
            const SizedBox(height: 6),
            Text(
              'Choose up to ${r.freeSlots} packet(s). They are held for this request and leave your available stock. '
              'The doctor assigned to the request approves the donation; if it is rejected or you withdraw, the packets come back.',
              style: const TextStyle(fontSize: 12),
            ),
            const SizedBox(height: 10),
            PacketPicker(bloodGroup: r.bloodGroup, max: r.freeSlots, selected: _ids, onChanged: (ids) => setState(() => _ids = ids)),
            const SizedBox(height: 12),
            if (_error != null) ...[InfoBanner(_error!, color: AppColors.rose600), const SizedBox(height: 12)],
            BusyButton(
              label: 'Donate ${_ids.isEmpty ? '' : '${_ids.length} '}packet(s)',
              busy: _saving,
              onPressed: _ids.isEmpty ? null : _submit,
            ),
          ],
        ),
      ),
    );
  }
}
