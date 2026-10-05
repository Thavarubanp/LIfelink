import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import 'inventory_models.dart';
import 'inventory_repository.dart';
import 'inventory_screen.dart' show packetStatusVariant;
import 'packet_forms.dart';

/// One packet with its full history (GET /Inventory/packets?packetId=); null when not visible to this hospital.
final packetDetailProvider = FutureProvider.autoDispose.family<BloodPacket?, String>(
  (ref, packetId) => ref.watch(inventoryRepositoryProvider).packet(packetId),
);

/// Packet details, a QR code of its tracking number (scan it with "Scan packet") and its full history.
class PacketDetailScreen extends ConsumerWidget {
  const PacketDetailScreen({super.key, required this.packetId});

  final String packetId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(packetDetailProvider(packetId));
    final packet = value.value;
    return Scaffold(
      appBar: AppBar(
        title: Text(packet?.trackingNumber ?? 'Packet'),
        actions: [
          if (packet != null && packet.canEdit)
            IconButton(
              tooltip: 'Edit packet',
              icon: const Icon(Icons.edit_outlined),
              onPressed: () async {
                if (await showEditPacketSheet(context, packet)) {
                  ref.invalidate(packetDetailProvider(packetId));
                  ref.invalidate(hospitalInventoryProvider);
                  ref.invalidate(packetsProvider);
                }
              },
            ),
        ],
      ),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(packetDetailProvider(packetId));
          await ref.read(packetDetailProvider(packetId).future).catchError((_) => null);
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(packetDetailProvider(packetId)),
          loadingMessage: 'Loading packet...',
          data: (p) => p == null
              ? const EmptyView(
                  icon: Icons.search_off,
                  title: 'Packet not found',
                  message: 'This packet is not in your hospital\'s inventory. Packets sent to another hospital are listed on the transfer.',
                )
              : ContentWidth(maxWidth: 640, child: _PacketBody(packet: p)),
        ),
      ),
    );
  }
}

class _PacketBody extends StatelessWidget {
  const _PacketBody({required this.packet});

  final BloodPacket packet;

  @override
  Widget build(BuildContext context) {
    final p = packet;
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                children: [
                  // Always dark on white so it scans in dark mode too
                  Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(color: Colors.white, borderRadius: BorderRadius.circular(12)),
                    child: QrImageView(
                      key: const Key('packet-qr'),
                      data: p.trackingNumber,
                      size: 180,
                      backgroundColor: Colors.white,
                      semanticsLabel: 'QR code for ${p.trackingNumber}',
                    ),
                  ),
                  const SizedBox(height: 10),
                  SelectableText(p.trackingNumber,
                      style: const TextStyle(fontFamily: 'monospace', fontSize: 18, fontWeight: FontWeight.w800)),
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 8,
                    alignment: WrapAlignment.center,
                    children: [
                      BloodGroupBadge(p.bloodGroup),
                      StatusBadge(p.status, variant: packetStatusVariant(p.status)),
                      if (p.isExpiringSoon) const StatusBadge('Expiring soon', variant: BadgeVariant.warning),
                    ],
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 12),
          SectionCard(
            title: 'Details',
            icon: Icons.info_outline,
            child: LayoutBuilder(
              builder: (context, c) {
                final w = (c.maxWidth - 16) / 2;
                Widget cell(String l, String v) => SizedBox(width: w, child: LabeledValue(l, v));
                return Wrap(
                  spacing: 16,
                  runSpacing: 12,
                  children: [
                    cell('Current hospital', p.hospitalName),
                    cell('Created by', p.createdByHospitalName),
                    cell('Created', Fmt.date(p.createdAt)),
                    cell('Source', p.source),
                    cell('Collected', Fmt.date(p.collectionDate)),
                    cell('Expires', Fmt.date(p.expiryDate)),
                    cell('Volume', '${p.volumeMl} ml'),
                    cell('Can edit', p.canEdit ? 'Yes (created here, available)' : 'No'),
                  ],
                );
              },
            ),
          ),
          const SizedBox(height: 12),
          SectionCard(
            title: 'History',
            icon: Icons.history,
            child: p.history.isEmpty
                ? const Text('No history recorded.', style: TextStyle(color: AppColors.slate500))
                : Column(children: [for (final t in p.history) _HistoryEntry(t: t)]),
          ),
        ],
      ),
    );
  }
}

class _HistoryEntry extends StatelessWidget {
  const _HistoryEntry({required this.t});

  final InventoryTransaction t;

  @override
  Widget build(BuildContext context) => IntrinsicHeight(
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Column(
              children: [
                const SizedBox(height: 4),
                Container(
                  width: 12,
                  height: 12,
                  decoration: const BoxDecoration(color: AppColors.red600, shape: BoxShape.circle),
                ),
                Expanded(child: Container(width: 2, color: AppColors.slate300)),
              ],
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Padding(
                padding: const EdgeInsets.only(bottom: 14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(Fmt.humanize(t.transactionType), style: const TextStyle(fontWeight: FontWeight.w700)),
                    if (t.notes.isNotEmpty) Text(t.notes, style: const TextStyle(fontSize: 13)),
                    Text(Fmt.sriLankaDateTime(t.createdAt), style: const TextStyle(fontSize: 11, color: AppColors.slate500)),
                  ],
                ),
              ),
            ),
          ],
        ),
      );
}
