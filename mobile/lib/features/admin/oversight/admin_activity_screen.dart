import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/api/api_client.dart';
import '../../../core/api/json.dart';
import '../../../core/providers.dart';
import '../../../core/routing/routes.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/badges.dart';
import '../../../core/widgets/common.dart';
import '../../../core/widgets/reply_sheet.dart';
import '../../../core/widgets/state_views.dart';
import '../../transfers/transfer_repository.dart';
import '../admin_repository.dart';
import '../attention_controller.dart';

/// BloodRequestResponseDto as the admin's activity log shows it.
class AdminBloodRequest {
  const AdminBloodRequest({
    required this.id,
    required this.bloodGroup,
    required this.unitsRequired,
    required this.priority,
    required this.status,
    required this.hospitalName,
    required this.createdByName,
    required this.createdAt,
    required this.isSuspended,
    required this.suspensionReason,
  });

  factory AdminBloodRequest.fromJson(Map<String, dynamic> j) => AdminBloodRequest(
        id: str(j['bloodRequestId']),
        bloodGroup: str(j['bloodGroup']),
        unitsRequired: intOf(j['unitsRequired']),
        priority: str(j['priority']),
        status: str(j['status']),
        hospitalName: str(j['hospitalName']),
        createdByName: j['createdByName']?.toString(),
        createdAt: parseDate(j['createdAt']),
        isSuspended: boolOf(j['isSuspended']),
        suspensionReason: j['suspensionReason']?.toString(),
      );

  final String id;
  final String bloodGroup;
  final int unitsRequired;
  final String priority;
  final String status;
  final String hospitalName;
  final String? createdByName;
  final DateTime? createdAt;
  final bool isSuspended;
  final String? suspensionReason;

  /// Only open requests can be suspended (the API enforces the same rule).
  bool get canSuspend => const {'Pending', 'Verified', 'Approved'}.contains(status);
}

/// One row of either tab, so filters and the "New" rule are shared.
class OversightItem {
  const OversightItem({
    required this.id,
    required this.kind,
    required this.title,
    required this.subtitle,
    required this.bloodGroup,
    required this.status,
    required this.createdAt,
    required this.isSuspended,
    required this.suspensionReason,
    required this.canSuspend,
    this.transferType,
  });

  factory OversightItem.request(AdminBloodRequest r) => OversightItem(
        id: r.id,
        kind: OversightKind.request,
        title: '${r.unitsRequired} unit(s) · ${r.priority}',
        subtitle: '${r.hospitalName}${r.createdByName != null ? ' · by ${r.createdByName}' : ''}',
        bloodGroup: r.bloodGroup,
        status: r.status,
        createdAt: r.createdAt,
        isSuspended: r.isSuspended,
        suspensionReason: r.suspensionReason,
        canSuspend: r.canSuspend,
      );

  factory OversightItem.transfer(Transfer t) => OversightItem(
        id: t.id,
        kind: OversightKind.transfer,
        title: '${t.unitsRequested} unit(s) · ${t.transferType}',
        subtitle: '${t.senderHospitalName} → ${t.receiverHospitalName}',
        bloodGroup: t.bloodGroup,
        status: t.status,
        createdAt: t.createdAt ?? t.requestedAt,
        isSuspended: t.isSuspended,
        suspensionReason: t.suspensionReason,
        canSuspend: t.status == 'Pending',
        transferType: t.transferType,
      );

  final String id;
  final OversightKind kind;
  final String title;
  final String subtitle;
  final String bloodGroup;
  final String status;
  final DateTime? createdAt;
  final bool isSuspended;
  final String? suspensionReason;
  final bool canSuspend;
  final String? transferType;

  /// Created since the admin last opened this tab (no "seen" time yet: everything is new).
  bool isNewSince(DateTime? seenAt) => seenAt == null || (createdAt != null && createdAt!.isAfter(seenAt));
}

enum OversightKind {
  request('blood-requests', 'Blood requests', 'blood request'),
  transfer('transfers', 'Transfers', 'transfer');

  const OversightKind(this.area, this.label, this.noun);
  final String area;
  final String label;
  final String noun;
}

const suspendedFilter = '__suspended';

/// Filters of the web page: status (or "Suspended by admin"), transfer type, Sri Lanka date range.
bool oversightMatches(OversightItem i, {String? status, String? type, DateTime? from, DateTime? to}) {
  if (status != null && (status == suspendedFilter ? !i.isSuspended : i.status != status)) return false;
  if (type != null && i.transferType != type) return false;
  if (i.createdAt != null) {
    final day = Fmt.sriLankaToday(i.createdAt);
    if (from != null && day.isBefore(from)) return false;
    if (to != null && day.isAfter(to)) return false;
  }
  return true;
}

/// Oversight calls (same as the web's activityApi + transferApi).
class OversightRepository {
  OversightRepository(this._api);

  final ApiClient _api;

  Future<List<AdminBloodRequest>> bloodRequests() async =>
      unwrapList(await _api.get('/Admin/blood-requests')).map(AdminBloodRequest.fromJson).toList();

  Future<List<Transfer>> transfers() async => unwrapList(await _api.get('/transfers')).map(Transfer.fromJson).toList();

  /// The admin opened a tab: its "new" highlight and badge clear.
  Future<void> markSeen(OversightKind k) => _api.put('/Admin/attention/${k.area}/seen');

  Future<void> suspend(OversightKind k, String id, String reason) =>
      _api.put('/Admin/${k == OversightKind.request ? 'blood-requests' : 'transfers'}/$id/suspend', body: {'reason': reason});

  Future<void> lift(OversightKind k, String id) => _api.put('/Admin/${k == OversightKind.request ? 'blood-requests' : 'transfers'}/$id/lift');
}

final oversightRepositoryProvider = Provider<OversightRepository>((ref) => OversightRepository(ref.watch(apiClientProvider)));

/// One tab's rows plus the "last seen" time read BEFORE the tab is marked seen, so this visit still shows what was new.
final oversightTabProvider = FutureProvider.autoDispose.family<(List<OversightItem>, DateTime?), OversightKind>((ref, kind) async {
  final repo = ref.watch(oversightRepositoryProvider);
  final counts = await ref.watch(adminRepositoryProvider).attention();
  final items = kind == OversightKind.request
      ? (await repo.bloodRequests()).map(OversightItem.request).toList()
      : (await repo.transfers()).map(OversightItem.transfer).toList();
  await repo.markSeen(kind);
  Future.microtask(() => ref.read(adminAttentionProvider.notifier).refresh());
  items.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
  return (items, kind == OversightKind.request ? counts.bloodRequestsSeenAt : counts.transfersSeenAt);
});

/// Admin Activity tab: every blood request (any status, incl. deleted) and every transfer, "New" since last opened,
/// filters, and Suspend (reason) / Lift on open items. The admin never edits or deletes them.
class AdminActivityScreen extends ConsumerStatefulWidget {
  const AdminActivityScreen({super.key, this.initialTab});

  final String? initialTab;

  @override
  ConsumerState<AdminActivityScreen> createState() => _AdminActivityScreenState();
}

class _AdminActivityScreenState extends ConsumerState<AdminActivityScreen> {
  late OversightKind _kind = widget.initialTab == 'transfers' ? OversightKind.transfer : OversightKind.request;
  String? _status;
  String? _type;
  DateTime? _from;
  DateTime? _to;
  String? _busyId;

  // "Last seen" of each tab as first read on this visit: reloading after an action keeps the same "New" items
  final Map<OversightKind, DateTime?> _seen = {};

  @override
  void didUpdateWidget(covariant AdminActivityScreen old) {
    super.didUpdateWidget(old);
    if (widget.initialTab != old.initialTab && widget.initialTab == 'transfers') _select(OversightKind.transfer);
  }

  void _select(OversightKind k) {
    if (k == _kind) return;
    setState(() {
      _kind = k;
      _status = null;
      _type = null;
      _from = null;
      _to = null;
    });
  }

  Future<void> _act(OversightItem i, Future<void> Function() call, String done) async {
    setState(() => _busyId = i.id);
    try {
      await call();
      if (mounted) showSnack(context, done, type: SnackType.success);
    } catch (e) {
      if (mounted) showErrorSnack(context, e, title: 'Action failed');
    } finally {
      ref.invalidate(oversightTabProvider(_kind));
      if (mounted) setState(() => _busyId = null);
    }
  }

  Future<void> _suspend(OversightItem i) async {
    final repo = ref.read(oversightRepositoryProvider);
    final sent = await showReplySheet(
      context,
      title: 'Suspend ${i.kind.noun} #${i.id.substring(0, 8)}',
      subtitle: i.kind == OversightKind.request
          ? 'Nobody can act on this request until you lift the suspension, except a donor withdrawing and the creator deleting it. '
              'The creator, the hospital, the assigned doctor and active donors are notified.'
          : 'This transfer cannot be accepted, rejected or withdrawn until you lift the suspension. Both hospitals are notified.',
      label: 'Reason (shown to the people notified)',
      submitLabel: 'Suspend',
      maxLength: 500,
      requiredMessage: 'A reason is required.',
      allowAttachment: false,
      destructive: true,
      onSubmit: (reason, _) => repo.suspend(i.kind, i.id, reason),
    );
    if (sent) {
      ref.invalidate(oversightTabProvider(_kind));
      if (mounted) showSnack(context, i.kind == OversightKind.request ? 'Blood request suspended' : 'Transfer suspended', type: SnackType.success);
    }
  }

  Future<void> _pickDate(bool from) async {
    final today = Fmt.sriLankaToday();
    final picked = await showDatePicker(
      context: context,
      initialDate: (from ? _from : _to) ?? today,
      firstDate: from ? DateTime(2026) : (_from ?? DateTime(2026)),
      lastDate: from ? (_to ?? today) : today,
    );
    if (picked != null) setState(() => from ? _from = picked : _to = picked);
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(oversightTabProvider(_kind));
    final newCounts = ref.watch(adminAttentionProvider);
    final statuses = _kind == OversightKind.request
        ? const ['Pending', 'Verified', 'Approved', 'Rejected', 'Completed', 'Cancelled', 'Deleted']
        : const ['Pending', 'Approved', 'Completed', 'Rejected', 'Cancelled'];
    return Scaffold(
      appBar: AppBar(
        title: const Text('Activity log'),
        actions: [
          PopupMenuButton<String>(
            tooltip: 'Directories',
            icon: const Icon(Icons.people_alt_outlined),
            onSelected: (path) => context.push(path),
            itemBuilder: (_) => const [
              PopupMenuItem(value: AppRoutes.adminUsers, child: Text('Users')),
              PopupMenuItem(value: AppRoutes.adminHospitals, child: Text('Hospitals')),
            ],
          ),
        ],
      ),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(oversightTabProvider(_kind));
          await ref.read(oversightTabProvider(_kind).future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SegmentedButton<OversightKind>(
                  segments: [
                    for (final k in OversightKind.values)
                      ButtonSegment(
                        value: k,
                        label: Text(() {
                          final n = k == _kind
                              ? 0
                              : k == OversightKind.request
                                  ? (newCounts?.newBloodRequests ?? 0)
                                  : (newCounts?.newTransfers ?? 0);
                          return n > 0 ? '${k.label} (${badgeText(n)})' : k.label;
                        }()),
                      ),
                  ],
                  selected: {_kind},
                  onSelectionChanged: (s) => _select(s.first),
                ),
                const SizedBox(height: 10),
                FilterChips<String?>(
                  options: [(null, 'All statuses'), for (final s in statuses) (s, s), (suspendedFilter, 'Suspended by admin')],
                  selected: _status,
                  onSelected: (s) => setState(() => _status = s),
                ),
                if (_kind == OversightKind.transfer) ...[
                  const SizedBox(height: 8),
                  FilterChips<String?>(
                    options: const [(null, 'Requests and offers'), ('Request', 'Requests'), ('Offer', 'Offers')],
                    selected: _type,
                    onSelected: (t) => setState(() => _type = t),
                  ),
                ],
                const SizedBox(height: 8),
                Wrap(spacing: 8, children: [
                  InputChip(
                    avatar: const Icon(Icons.calendar_month, size: 16),
                    label: Text(_from == null ? 'Created from' : 'From ${Fmt.apiDate(_from!)}'),
                    onPressed: () => _pickDate(true),
                    onDeleted: _from == null ? null : () => setState(() => _from = null),
                  ),
                  InputChip(
                    avatar: const Icon(Icons.calendar_month, size: 16),
                    label: Text(_to == null ? 'Created to' : 'To ${Fmt.apiDate(_to!)}'),
                    onPressed: () => _pickDate(false),
                    onDeleted: _to == null ? null : () => setState(() => _to = null),
                  ),
                ]),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(oversightTabProvider(_kind)),
                  loadingMessage: 'Loading...',
                  data: (d) {
                    final (items, firstSeenAt) = d;
                    final seenAt = _seen.putIfAbsent(_kind, () => firstSeenAt);
                    final list = items.where((i) => oversightMatches(i, status: _status, type: _type, from: _from, to: _to)).toList();
                    final newHere = list.where((i) => i.isNewSince(seenAt)).length;
                    if (list.isEmpty) return const EmptyView(icon: Icons.history, message: 'Nothing matches these filters.');
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Text('${list.length} ${_kind.label.toLowerCase()}${newHere > 0 ? ' · $newHere new' : ''}',
                            style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
                        const SizedBox(height: 8),
                        for (final i in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 8),
                            child: _ItemCard(
                              item: i,
                              isNew: i.isNewSince(seenAt),
                              busy: _busyId == i.id,
                              onSuspend: () => _suspend(i),
                              onLift: () => _act(i, () => ref.read(oversightRepositoryProvider).lift(i.kind, i.id), 'Suspension lifted'),
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

class _ItemCard extends StatelessWidget {
  const _ItemCard({required this.item, required this.isNew, required this.busy, required this.onSuspend, required this.onLift});

  final OversightItem item;
  final bool isNew;
  final bool busy;
  final VoidCallback onSuspend;
  final VoidCallback onLift;

  @override
  Widget build(BuildContext context) => Card(
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(children: [
                if (isNew) ...[const StatusBadge('New', variant: BadgeVariant.danger), const SizedBox(width: 6)],
                Text('#${item.id.length >= 8 ? item.id.substring(0, 8) : item.id}',
                    style: const TextStyle(fontFamily: 'monospace', fontSize: 12, color: AppColors.slate500)),
                const SizedBox(width: 6),
                BloodGroupBadge(item.bloodGroup),
                const SizedBox(width: 6),
                Expanded(child: Text(item.title, style: const TextStyle(fontWeight: FontWeight.w600))),
              ]),
              const SizedBox(height: 4),
              Text(item.subtitle, style: const TextStyle(fontSize: 13)),
              Text(Fmt.sriLankaDateTime(item.createdAt), style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
              const SizedBox(height: 6),
              Row(
                children: [
                  Expanded(
                    child: Wrap(spacing: 6, runSpacing: 4, children: [
                      StatusBadge(item.status,
                          variant: item.status == 'Completed' ? BadgeVariant.success : item.status == 'Pending' ? BadgeVariant.warning : BadgeVariant.neutral),
                      if (item.isSuspended) SuspendedBadge(reason: item.suspensionReason),
                    ]),
                  ),
                  if (item.isSuspended)
                    OutlinedButton.icon(
                      style: OutlinedButton.styleFrom(minimumSize: const Size(0, 36), foregroundColor: AppColors.emerald600),
                      onPressed: busy ? null : onLift,
                      icon: busy ? const SizedBox(width: 14, height: 14, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.play_circle_outline, size: 18),
                      label: const Text('Lift'),
                    )
                  else if (item.canSuspend)
                    OutlinedButton.icon(
                      style: OutlinedButton.styleFrom(minimumSize: const Size(0, 36), foregroundColor: AppColors.rose600),
                      onPressed: busy ? null : onSuspend,
                      icon: const Icon(Icons.pause_circle_outline, size: 18),
                      label: const Text('Suspend'),
                    ),
                ],
              ),
              if (item.isSuspended && item.suspensionReason?.isNotEmpty == true)
                Text('Reason: ${item.suspensionReason}', style: const TextStyle(fontSize: 12, color: AppColors.rose600)),
            ],
          ),
        ),
      );
}
