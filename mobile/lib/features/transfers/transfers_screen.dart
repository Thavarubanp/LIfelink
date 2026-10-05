import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../inventory/inventory_repository.dart';
import '../inventory/packet_picker.dart';
import '../profile/profile_repository.dart';
import 'transfer_create_sheet.dart';
import 'transfer_repository.dart';

enum _Tab { incoming, outgoing, history }

BadgeVariant transferStatusVariant(String status) => switch (status) {
      'Pending' => BadgeVariant.warning,
      'Completed' => BadgeVariant.success,
      'Rejected' => BadgeVariant.danger,
      _ => BadgeVariant.neutral,
    };

/// Inter-hospital transfers: waiting for my response / sent by my hospital / history (with a status filter).
/// The other hospital accepts or rejects; accepted transfers move the packets straight away.
class TransfersScreen extends ConsumerStatefulWidget {
  const TransfersScreen({super.key});

  @override
  ConsumerState<TransfersScreen> createState() => _TransfersScreenState();
}

class _TransfersScreenState extends ConsumerState<TransfersScreen> {
  _Tab _tab = _Tab.incoming;
  String? _historyStatus;
  bool _acting = false;

  void _reload() {
    ref.invalidate(transfersProvider);
    ref.invalidate(hospitalInventoryProvider);
    ref.invalidate(packetsProvider);
    ref.invalidate(availablePacketsProvider);
  }

  Future<void> _refresh() async {
    _reload();
    await ref.read(transfersProvider.future).catchError((_) => <Transfer>[]);
  }

  Future<void> _act(Future<void> Function() action, String title, String message, {SnackType type = SnackType.success}) async {
    if (_acting) return;
    setState(() => _acting = true);
    try {
      await action();
      if (mounted) showSnack(context, message, type: type, title: title);
      _reload();
    } catch (e) {
      final api = ApiError.from(e);
      if (mounted) showSnack(context, api.message, type: SnackType.error, title: 'Action failed');
      // Accepted, rejected or withdrawn by the other side at the same moment: show the current state
      if (api.isConflict) _reload();
    } finally {
      if (mounted) setState(() => _acting = false);
    }
  }

  Future<void> _accept(Transfer t, String me) async {
    final repo = ref.read(transferRepositoryProvider);
    if (t.senderHospitalId == me) {
      // This hospital sends the blood: choose exactly the requested packets
      final ids = await showModalBottomSheet<List<String>>(
        context: context,
        isScrollControlled: true,
        builder: (_) => _SendPacketsSheet(transfer: t),
      );
      if (ids == null) return;
      await _act(() => repo.approve(t.id, ids), 'Transfer accepted', '${ids.length} packet(s) sent to ${t.receiverHospitalName}.');
    } else {
      if (!mounted) return;
      final ok = await confirmDialog(context,
          title: 'Accept transfer', message: 'Accept ${t.unitsRequested} x ${t.bloodGroup} from ${t.senderHospitalName}?', confirmLabel: 'Accept');
      if (ok) await _act(() => repo.approve(t.id), 'Transfer accepted', 'Packets moved and both inventories updated.');
    }
  }

  Future<void> _reject(Transfer t) async {
    final reason = await reasonDialog(context, title: 'Reject transfer', hint: 'Reason (shown to the other hospital)', confirmLabel: 'Reject');
    if (reason == null) return;
    await _act(() => ref.read(transferRepositoryProvider).reject(t.id, reason), 'Transfer rejected', 'The other hospital will see your reason.',
        type: SnackType.info);
  }

  Future<void> _cancel(Transfer t) async {
    final ok = await confirmDialog(context,
        title: 'Delete transfer', message: 'Delete this pending transfer? It stays in the history as Cancelled.', confirmLabel: 'Delete', destructive: true);
    if (ok) {
      await _act(() => ref.read(transferRepositoryProvider).cancel(t.id), 'Transfer deleted', 'The other hospital has been notified.',
          type: SnackType.info);
    }
  }

  Future<void> _showPackets(Transfer t) async {
    var detail = t;
    try {
      detail = await ref.read(transferRepositoryProvider).byId(t.id);
    } catch (_) {
      // Show what the list already has
    }
    if (!mounted) return;
    await showModalBottomSheet<void>(
      context: context,
      builder: (_) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('${detail.status == 'Completed' ? 'Packets moved' : 'Packets offered'} (${detail.packetTrackingNumbers.length})',
                  style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              const SizedBox(height: 6),
              Text(
                'From ${detail.senderHospitalName} to ${detail.receiverHospitalName}'
                '${detail.status == 'Completed' ? ', accepted ${Fmt.sriLankaDateTime(detail.approvedAt)}. Tracking numbers did not change.' : '. Held until the other hospital answers.'}',
                style: const TextStyle(fontSize: 13),
              ),
              const SizedBox(height: 12),
              Wrap(
                spacing: 12,
                runSpacing: 6,
                children: [
                  for (final n in detail.packetTrackingNumbers)
                    Text(n, style: const TextStyle(fontFamily: 'monospace', fontWeight: FontWeight.w600)),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(transfersProvider);
    final me = ref.watch(myHospitalProvider).value?.hospitalId;
    return Scaffold(
      appBar: AppBar(title: const Text('Transfers')),
      floatingActionButton: FloatingActionButton.extended(
        backgroundColor: AppColors.red600,
        foregroundColor: Colors.white,
        onPressed: () async {
          if (await showCreateTransferSheet(context)) {
            setState(() => _tab = _Tab.outgoing);
            _reload();
          }
        },
        icon: const Icon(Icons.add),
        label: const Text('New transfer'),
      ),
      body: RefreshableScroll(
        onRefresh: _refresh,
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
            child: AsyncView(
              value: value,
              onRetry: _reload,
              loadingMessage: 'Loading transfers...',
              data: (all) {
                if (me == null) return const LoadingView();
                final lists = TransferLists(all, me);
                final history = lists.history.where((t) => _historyStatus == null || t.status == _historyStatus).toList();
                final visible = switch (_tab) {
                  _Tab.incoming => lists.incoming,
                  _Tab.outgoing => lists.outgoing,
                  _Tab.history => history,
                };
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    FilterChips<_Tab>(
                      options: [
                        (_Tab.incoming, 'Waiting for me (${lists.incoming.length})'),
                        (_Tab.outgoing, 'Sent by us (${lists.outgoing.length})'),
                        (_Tab.history, 'History (${lists.history.length})'),
                      ],
                      selected: _tab,
                      onSelected: (t) => setState(() => _tab = t),
                    ),
                    if (_tab == _Tab.history) ...[
                      const SizedBox(height: 8),
                      FilterChips<String?>(
                        options: const [(null, 'All'), ('Completed', 'Completed'), ('Rejected', 'Rejected'), ('Cancelled', 'Cancelled')],
                        selected: _historyStatus,
                        onSelected: (s) => setState(() => _historyStatus = s),
                      ),
                    ],
                    const SizedBox(height: 12),
                    if (visible.isEmpty)
                      const EmptyView(icon: Icons.swap_horiz, message: 'No transfers here.')
                    else
                      for (final t in visible)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: _TransferCard(
                            t: t,
                            me: me,
                            busy: _acting,
                            onAccept: () => _accept(t, me),
                            onReject: () => _reject(t),
                            onCancel: () => _cancel(t),
                            onPackets: () => _showPackets(t),
                          ),
                        ),
                  ],
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}

class _TransferCard extends StatelessWidget {
  const _TransferCard({
    required this.t,
    required this.me,
    required this.busy,
    required this.onAccept,
    required this.onReject,
    required this.onCancel,
    required this.onPackets,
  });

  final Transfer t;
  final String me;
  final bool busy;
  final VoidCallback onAccept;
  final VoidCallback onReject;
  final VoidCallback onCancel;
  final VoidCallback onPackets;

  @override
  Widget build(BuildContext context) {
    final sends = t.senderHospitalId == me;
    final other = sends ? t.receiverHospitalName : t.senderHospitalName;
    final canAnswer = !t.isSuspended && t.isPending && t.counterpartHospitalId == me;
    final canCancel = !t.isSuspended && t.isPending && t.createdByHospitalId == me;
    final showPackets = t.status == 'Completed' || (t.isPending && t.isOffer);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(sends ? Icons.north_east : Icons.south_west, color: sends ? AppColors.rose600 : AppColors.emerald600),
                const SizedBox(width: 8),
                BloodGroupBadge(t.bloodGroup),
                const SizedBox(width: 8),
                Expanded(
                  child: Text('${t.unitsRequested} unit(s) ${sends ? 'to' : 'from'} $other',
                      style: const TextStyle(fontWeight: FontWeight.w700)),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                StatusBadge(t.transferType),
                StatusBadge(t.status, variant: transferStatusVariant(t.status)),
                if (t.isSuspended) SuspendedBadge(reason: t.suspensionReason),
              ],
            ),
            if (t.isSuspended) ...[
              const SizedBox(height: 8),
              const Text('Suspended by the administrator: it cannot be accepted, rejected or deleted until the suspension is lifted.',
                  style: TextStyle(fontSize: 12, color: AppColors.rose600)),
            ],
            const SizedBox(height: 6),
            Text('Created ${Fmt.sriLankaDateTime(t.requestedAt)}${t.notes.isNotEmpty ? ' · ${t.notes}' : ''}',
                style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
            if (t.rejectionReason?.isNotEmpty == true)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text('Rejection reason: ${t.rejectionReason}', style: const TextStyle(fontSize: 12, color: AppColors.rose600)),
              ),
            if (canAnswer || canCancel || showPackets) ...[
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  if (canAnswer) ...[
                    FilledButton.icon(
                      style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600, minimumSize: const Size(0, 40)),
                      onPressed: busy ? null : onAccept,
                      icon: const Icon(Icons.check, size: 18),
                      label: const Text('Accept'),
                    ),
                    FilledButton.icon(
                      style: FilledButton.styleFrom(backgroundColor: AppColors.rose600, minimumSize: const Size(0, 40)),
                      onPressed: busy ? null : onReject,
                      icon: const Icon(Icons.close, size: 18),
                      label: const Text('Reject'),
                    ),
                  ],
                  if (canCancel)
                    OutlinedButton.icon(
                      style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40)),
                      onPressed: busy ? null : onCancel,
                      icon: const Icon(Icons.delete_outline, size: 18),
                      label: const Text('Delete'),
                    ),
                  if (showPackets)
                    OutlinedButton(
                      style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40)),
                      onPressed: onPackets,
                      child: Text(t.status == 'Completed' ? 'Packets moved' : 'Packets offered'),
                    ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// The sending hospital accepts a request: exactly the requested number of packets.
class _SendPacketsSheet extends StatefulWidget {
  const _SendPacketsSheet({required this.transfer});

  final Transfer transfer;

  @override
  State<_SendPacketsSheet> createState() => _SendPacketsSheetState();
}

class _SendPacketsSheetState extends State<_SendPacketsSheet> {
  List<String> _ids = [];

  @override
  Widget build(BuildContext context) {
    final t = widget.transfer;
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Send ${t.unitsRequested} x ${t.bloodGroup} to ${t.receiverHospitalName}',
                style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
            const SizedBox(height: 6),
            Text('Select exactly ${t.unitsRequested} packet(s). They move to the other hospital with the same tracking numbers.',
                style: const TextStyle(fontSize: 13)),
            const SizedBox(height: 10),
            PacketPicker(bloodGroup: t.bloodGroup, exact: t.unitsRequested, selected: _ids, onChanged: (ids) => setState(() => _ids = ids)),
            const SizedBox(height: 14),
            FilledButton(
              style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600),
              onPressed: _ids.length == t.unitsRequested ? () => Navigator.of(context).pop(_ids) : null,
              child: const Text('Accept and send'),
            ),
          ],
        ),
      ),
    );
  }
}
