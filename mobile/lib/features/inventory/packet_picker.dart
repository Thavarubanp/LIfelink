import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/state_views.dart';
import 'inventory_models.dart';
import 'inventory_repository.dart';

/// The signed-in hospital's Available, unexpired packets of one blood group, earliest expiry first.
final availablePacketsProvider = FutureProvider.autoDispose.family<List<BloodPacket>, String>((ref, group) async {
  final list = await ref.watch(inventoryRepositoryProvider).packets(status: 'Available', bloodGroup: group);
  final now = DateTime.now().toUtc();
  final usable = list.where((p) => p.usableAt(now)).toList()
    ..sort((a, b) => (a.expiryDate ?? DateTime(9999)).compareTo(b.expiryDate ?? DateTime(9999)));
  return usable;
});

/// Choose the exact packets to issue, transfer or donate (the web app's PacketPicker). [exact] requires that many
/// packets; [max] caps the selection. The API re-checks every rule.
class PacketPicker extends ConsumerWidget {
  const PacketPicker({super.key, required this.bloodGroup, required this.selected, required this.onChanged, this.exact, this.max});

  final String bloodGroup;
  final List<String> selected;
  final ValueChanged<List<String>> onChanged;
  final int? exact;
  final int? max;

  int? get _limit => exact ?? max;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(availablePacketsProvider(bloodGroup));
    return value.when(
      loading: () => const Padding(padding: EdgeInsets.all(16), child: LoadingView(message: 'Loading packets...')),
      error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(availablePacketsProvider(bloodGroup))),
      data: (packets) {
        if (packets.isEmpty) {
          return Container(
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(12),
              border: Border.all(color: AppColors.slate300),
            ),
            child: Text('No available $bloodGroup packets in your inventory.', style: const TextStyle(color: AppColors.slate500)),
          );
        }
        final limit = _limit;
        final warn = exact != null && selected.length != exact;
        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    '${selected.length} selected${exact != null ? ' of $exact required' : max != null ? ' (up to $max)' : ''} · ${packets.length} available',
                    style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: warn ? AppColors.amber600 : null),
                  ),
                ),
                TextButton(
                  onPressed: () => onChanged(packets.take(limit ?? packets.length).map((p) => p.packetId).toList()),
                  child: const Text('Select earliest'),
                ),
              ],
            ),
            Container(
              constraints: const BoxConstraints(maxHeight: 280),
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: Theme.of(context).colorScheme.outline),
              ),
              child: ListView.separated(
                shrinkWrap: true,
                itemCount: packets.length,
                separatorBuilder: (_, _) => const Divider(height: 1),
                itemBuilder: (context, i) {
                  final p = packets[i];
                  final checked = selected.contains(p.packetId);
                  final disabled = !checked && limit != null && selected.length >= limit;
                  return CheckboxListTile(
                    key: ValueKey('pick-${p.packetId}'),
                    dense: true,
                    value: checked,
                    activeColor: AppColors.red600,
                    onChanged: disabled
                        ? null
                        : (_) => onChanged(checked ? (List.of(selected)..remove(p.packetId)) : [...selected, p.packetId]),
                    title: Text(p.trackingNumber, style: const TextStyle(fontFamily: 'monospace', fontWeight: FontWeight.w700)),
                    subtitle: Row(
                      children: [
                        Text('Expires ${Fmt.date(p.expiryDate)}'),
                        if (p.isExpiringSoon) ...[const SizedBox(width: 6), const StatusBadge('Soon', variant: BadgeVariant.warning)],
                      ],
                    ),
                  );
                },
              ),
            ),
          ],
        );
      },
    );
  }
}
