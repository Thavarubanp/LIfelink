import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../profile/profile_repository.dart';
import 'group_form_sheet.dart';
import 'inventory_models.dart';
import 'inventory_repository.dart';

enum StockFilter { all, low, expiring }

/// Hooks for the packet actions (add / issue / edit), plugged in by the packets part.
class PacketActions {
  const PacketActions({this.addPackets, this.issue, this.edit});

  final Future<bool> Function(BuildContext context)? addPackets;
  final Future<bool> Function(BuildContext context, InventoryItem group, List<String> preselected)? issue;
  final Future<bool> Function(BuildContext context, BloodPacket packet)? edit;
}

/// Packet-level blood inventory of the signed-in hospital: one card per blood group (units, threshold, capacity,
/// expiring soon, health), search by group or tracking number, stock filter, and each group expanding to its
/// packets with a status filter. `?group=A%2B` opens that group.
class InventoryScreen extends ConsumerStatefulWidget {
  const InventoryScreen({super.key, this.initialGroup, this.actions = const PacketActions()});

  final String? initialGroup;
  final PacketActions actions;

  @override
  ConsumerState<InventoryScreen> createState() => _InventoryScreenState();
}

class _InventoryScreenState extends ConsumerState<InventoryScreen> {
  late final Set<String> _expanded = {?widget.initialGroup};
  final Map<String, GlobalKey> _keys = {};
  String _search = '';
  StockFilter _filter = StockFilter.all;
  String? _status = 'Available';
  String? _scrolledTo;

  @override
  void didUpdateWidget(covariant InventoryScreen old) {
    super.didUpdateWidget(old);
    final group = widget.initialGroup;
    if (group != null && group != old.initialGroup) {
      setState(() {
        _expanded.add(group);
        _scrolledTo = null;
      });
    }
  }

  Future<void> _refresh() async {
    ref.invalidate(hospitalInventoryProvider);
    ref.invalidate(packetsProvider(_status));
    await Future.wait([
      ref.read(hospitalInventoryProvider.future).catchError((_) => <InventoryItem>[]),
      ref.read(packetsProvider(_status).future).catchError((_) => <BloodPacket>[]),
    ]);
  }

  void _reload() {
    ref.invalidate(hospitalInventoryProvider);
    ref.invalidate(packetsProvider);
  }

  void _scrollToRequested() {
    final group = widget.initialGroup;
    if (group == null || _scrolledTo == group) return;
    final ctx = _keys[group]?.currentContext;
    if (ctx == null) return;
    _scrolledTo = group;
    Scrollable.ensureVisible(ctx, duration: const Duration(milliseconds: 300), alignment: 0.05);
  }

  @override
  Widget build(BuildContext context) {
    final inventory = ref.watch(hospitalInventoryProvider);
    final packets = ref.watch(packetsProvider(_status));
    final hospital = ref.watch(myHospitalProvider).value;
    final term = _search.trim().toLowerCase();
    final packetList = packets.value ?? const <BloodPacket>[];

    if (inventory.hasValue) WidgetsBinding.instance.addPostFrameCallback((_) => _scrollToRequested());

    return Scaffold(
      appBar: AppBar(
        title: const Text('Blood inventory'),
        actions: [
          IconButton(
            tooltip: 'Add blood group',
            icon: const Icon(Icons.add_circle_outline),
            onPressed: () async {
              final groups = inventory.value?.map((i) => i.bloodGroup).toList() ?? const <String>[];
              if (await showGroupFormSheet(context, existingGroups: groups)) _reload();
            },
          ),
          IconButton(
            tooltip: 'Scan packet',
            icon: const Icon(Icons.qr_code_scanner),
            onPressed: () => context.push(AppRoutes.hospitalScan),
          ),
        ],
      ),
      floatingActionButton: widget.actions.addPackets == null
          ? null
          : FloatingActionButton.extended(
              key: const Key('fab-add-packets'),
              backgroundColor: AppColors.red600,
              foregroundColor: Colors.white,
              onPressed: () async {
                if (await widget.actions.addPackets!(context)) _reload();
              },
              icon: const Icon(Icons.add),
              label: const Text('Add packets'),
            ),
      body: RefreshableScroll(
        onRefresh: _refresh,
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (hospital != null)
                  Text(
                    'Every unit is a 440 ml packet with its own tracking number. Shelf life ${hospital.packetShelfLifeDays} days, '
                    'expiry alert window ${hospital.expiryAlertDays} days (changed in the hospital profile on the web).',
                    style: const TextStyle(fontSize: 12, color: AppColors.slate500),
                  ),
                const SizedBox(height: 12),
                if ((inventory.value ?? const []).any((i) => i.isLowStock || i.expiringSoonUnits > 0)) ...[
                  const InfoBanner(
                    'Some blood groups are below threshold or have packets expiring soon. The inventory analysis alerts '
                    'the hospitals holding that exact blood group; see your notifications or run the analysis from Home.',
                    icon: Icons.warning_amber_rounded,
                  ),
                  const SizedBox(height: 12),
                ],
                SearchField(hint: 'Search blood group or tracking number', onChanged: (v) => setState(() => _search = v)),
                const SizedBox(height: 10),
                FilterChips<StockFilter>(
                  options: const [
                    (StockFilter.all, 'All groups'),
                    (StockFilter.low, 'Below threshold'),
                    (StockFilter.expiring, 'Expiring soon'),
                  ],
                  selected: _filter,
                  onSelected: (v) => setState(() => _filter = v),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: inventory,
                  onRetry: _reload,
                  loadingMessage: 'Loading inventory...',
                  data: (items) {
                    final visible = filterGroups(items, packetList, term: term, filter: _filter);
                    if (visible.isEmpty) {
                      return EmptyView(
                        icon: Icons.inventory_2_outlined,
                        message: items.isEmpty
                            ? 'No blood groups yet. Add packets or a blood group to start.'
                            : 'No blood group matches these filters.',
                      );
                    }
                    return Column(
                      children: [
                        for (final item in visible)
                          Padding(
                            key: _keys.putIfAbsent(item.bloodGroup, GlobalKey.new),
                            padding: const EdgeInsets.only(bottom: 10),
                            child: _GroupCard(
                              item: item,
                              expanded: _expanded.contains(item.bloodGroup),
                              onToggle: () => setState(() {
                                _expanded.contains(item.bloodGroup)
                                    ? _expanded.remove(item.bloodGroup)
                                    : _expanded.add(item.bloodGroup);
                              }),
                              onThresholds: () async {
                                if (await showGroupFormSheet(context, edit: item)) _reload();
                              },
                              onIssue: widget.actions.issue == null || item.unitsAvailable == 0
                                  ? null
                                  : () async {
                                      if (await widget.actions.issue!(context, item, const [])) _reload();
                                    },
                              packets: _GroupPackets(
                                group: item,
                                packets: packets,
                                term: term,
                                status: _status,
                                onStatus: (s) => setState(() => _status = s),
                                onRetry: () => ref.invalidate(packetsProvider(_status)),
                                onIssue: widget.actions.issue == null
                                    ? null
                                    : (p) async {
                                        if (await widget.actions.issue!(context, item, [p.packetId])) _reload();
                                      },
                                onEdit: widget.actions.edit == null
                                    ? null
                                    : (p) async {
                                        if (await widget.actions.edit!(context, p)) _reload();
                                      },
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

/// Search and stock filter (same rules as the web page): a group matches the search by its name or by any of its
/// packets' tracking numbers; "Below threshold" uses the API's isLowStock, "Expiring soon" any expiring packet.
List<InventoryItem> filterGroups(List<InventoryItem> items, List<BloodPacket> packets,
    {String term = '', StockFilter filter = StockFilter.all}) {
  final t = term.trim().toLowerCase();
  return items
      .where((i) => switch (filter) {
            StockFilter.all => true,
            StockFilter.low => i.isLowStock,
            StockFilter.expiring => i.expiringSoonUnits > 0,
          })
      .where((i) =>
          t.isEmpty ||
          i.bloodGroup.toLowerCase().contains(t) ||
          packets.any((p) => p.bloodGroup == i.bloodGroup && p.trackingNumber.toLowerCase().contains(t)))
      .toList();
}

BadgeVariant packetStatusVariant(String status) => switch (status) {
      'Available' => BadgeVariant.success,
      'Reserved' => BadgeVariant.info,
      'Donated' => BadgeVariant.primary,
      'Expired' => BadgeVariant.warning,
      _ => BadgeVariant.neutral,
    };

class _HealthBadge extends StatelessWidget {
  const _HealthBadge(this.item);

  final InventoryItem item;

  @override
  Widget build(BuildContext context) => item.isLowStock
      ? const StatusBadge('Below threshold', variant: BadgeVariant.warning)
      : item.isSurplus
          ? const StatusBadge('Surplus', variant: BadgeVariant.info)
          : const StatusBadge('Healthy', variant: BadgeVariant.success);
}

class _GroupCard extends StatelessWidget {
  const _GroupCard({
    required this.item,
    required this.expanded,
    required this.onToggle,
    required this.onThresholds,
    required this.onIssue,
    required this.packets,
  });

  final InventoryItem item;
  final bool expanded;
  final VoidCallback onToggle;
  final VoidCallback onThresholds;
  final VoidCallback? onIssue;
  final Widget packets;

  @override
  Widget build(BuildContext context) => Card(
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            InkWell(
              onTap: onToggle,
              child: Padding(
                padding: const EdgeInsets.all(14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        BloodGroupBadge(item.bloodGroup, large: true),
                        const SizedBox(width: 12),
                        Expanded(
                          child: Text('${item.unitsAvailable} unit(s)',
                              style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800)),
                        ),
                        _HealthBadge(item),
                        Icon(expanded ? Icons.expand_less : Icons.expand_more, semanticLabel: expanded ? 'Hide packets' : 'Show packets'),
                      ],
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Threshold ${item.minimumThreshold} · capacity ${item.maximumCapacity}'
                      '${item.expiringSoonUnits > 0 ? ' · ${item.expiringSoonUnits} expiring within ${item.expiryAlertDays}d' : ''}'
                      '${item.nextExpiryDate != null ? ' · next expiry ${Fmt.date(item.nextExpiryDate)}' : ''}',
                      style: const TextStyle(fontSize: 12, color: AppColors.slate500),
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      children: [
                        OutlinedButton.icon(
                          style: OutlinedButton.styleFrom(minimumSize: const Size(0, 36), visualDensity: VisualDensity.compact),
                          onPressed: onThresholds,
                          icon: const Icon(Icons.tune, size: 16),
                          label: const Text('Thresholds'),
                        ),
                        if (onIssue != null)
                          OutlinedButton.icon(
                            style: OutlinedButton.styleFrom(minimumSize: const Size(0, 36), visualDensity: VisualDensity.compact),
                            onPressed: onIssue,
                            icon: const Icon(Icons.send_outlined, size: 16),
                            label: const Text('Issue'),
                          ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
            if (expanded) ...[const Divider(), packets],
          ],
        ),
      );
}

class _GroupPackets extends StatelessWidget {
  const _GroupPackets({
    required this.group,
    required this.packets,
    required this.term,
    required this.status,
    required this.onStatus,
    required this.onRetry,
    required this.onIssue,
    required this.onEdit,
  });

  final InventoryItem group;
  final AsyncValue<List<BloodPacket>> packets;
  final String term;
  final String? status;
  final ValueChanged<String?> onStatus;
  final VoidCallback onRetry;
  final void Function(BloodPacket)? onIssue;
  final void Function(BloodPacket)? onEdit;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            FilterChips<String?>(options: packetStatusOptions, selected: status, onSelected: onStatus),
            const SizedBox(height: 8),
            packets.when(
              loading: () => const Padding(padding: EdgeInsets.all(16), child: Center(child: CircularProgressIndicator())),
              error: (e, _) => ErrorView(error: e, onRetry: onRetry),
              data: (all) {
                final list = all
                    .where((p) => p.bloodGroup == group.bloodGroup)
                    .where((p) => term.isEmpty || group.bloodGroup.toLowerCase().contains(term) || p.trackingNumber.toLowerCase().contains(term))
                    .toList();
                if (list.isEmpty) {
                  return Padding(
                    padding: const EdgeInsets.all(16),
                    child: Text('No ${group.bloodGroup} packets with this status.',
                        textAlign: TextAlign.center, style: const TextStyle(color: AppColors.slate500)),
                  );
                }
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text('${group.bloodGroup} packets (${list.length})',
                        style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
                    ShowMoreList<BloodPacket>(
                      items: list,
                      itemBuilder: (context, p) => PacketTile(packet: p, onIssue: onIssue, onEdit: onEdit),
                    ),
                  ],
                );
              },
            ),
          ],
        ),
      );
}

/// One packet row: tracking number, dates, creator, status; tap opens its detail and history.
class PacketTile extends StatelessWidget {
  const PacketTile({super.key, required this.packet, this.onIssue, this.onEdit});

  final BloodPacket packet;
  final void Function(BloodPacket)? onIssue;
  final void Function(BloodPacket)? onEdit;

  @override
  Widget build(BuildContext context) {
    final p = packet;
    return InkWell(
      onTap: () => context.push(AppRoutes.hospitalPacket(p.packetId)),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(p.trackingNumber, style: const TextStyle(fontFamily: 'monospace', fontWeight: FontWeight.w700)),
                ),
                if (p.isExpiringSoon) ...[const StatusBadge('Soon', variant: BadgeVariant.warning), const SizedBox(width: 6)],
                StatusBadge(p.status, variant: packetStatusVariant(p.status)),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              'Collected ${Fmt.date(p.collectionDate)} · expires ${Fmt.date(p.expiryDate)}\n'
              '${p.createdHere ? 'Your hospital' : p.createdByHospitalName} · ${p.source}',
              style: const TextStyle(fontSize: 12, color: AppColors.slate500),
            ),
            if ((p.canEdit && onEdit != null) || (p.status == 'Available' && onIssue != null))
              Wrap(
                spacing: 4,
                children: [
                  if (p.canEdit && onEdit != null)
                    TextButton.icon(onPressed: () => onEdit!(p), icon: const Icon(Icons.edit_outlined, size: 16), label: const Text('Edit')),
                  if (p.status == 'Available' && onIssue != null)
                    TextButton.icon(onPressed: () => onIssue!(p), icon: const Icon(Icons.send_outlined, size: 16), label: const Text('Issue')),
                ],
              ),
          ],
        ),
      ),
    );
  }
}
