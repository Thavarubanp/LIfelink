import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_error.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';

/// ActivityLogEntryDto.
class ActivityEntry {
  const ActivityEntry({required this.id, required this.occurredAt, required this.actorRole, required this.actorName, required this.action, required this.entityType, required this.summary});

  factory ActivityEntry.fromJson(Map<String, dynamic> j) => ActivityEntry(
        id: str(j['id']),
        occurredAt: parseDate(j['occurredAt']),
        actorRole: str(j['actorRole']),
        actorName: str(j['actorName']),
        action: str(j['action']),
        entityType: str(j['entityType']),
        summary: str(j['summary']),
      );

  final String id;
  final DateTime? occurredAt;
  final String actorRole;
  final String actorName;
  final String action;
  final String entityType;
  final String summary;
}

/// ActivityLogPageDto: one server-side page.
class ActivityPage {
  const ActivityPage({required this.items, required this.total, required this.page, required this.pageSize, required this.recordedFrom, required this.types});

  factory ActivityPage.fromJson(Map<String, dynamic> j) => ActivityPage(
        items: (j['items'] is List ? j['items'] as List : const [])
            .whereType<Map>()
            .map((e) => ActivityEntry.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
        total: intOf(j['total']),
        page: intOf(j['page'], 1),
        pageSize: intOf(j['pageSize'], ActivityQuery.pageSize),
        recordedFrom: parseDate(j['recordedFrom']),
        types: stringList(j['types']),
      );

  final List<ActivityEntry> items;
  final int total;
  final int page;
  final int pageSize;
  final DateTime? recordedFrom;
  final List<String> types;

  int get totalPages => total <= 0 ? 1 : (total / (pageSize <= 0 ? ActivityQuery.pageSize : pageSize)).ceil();
}

/// Paging and filters (type, Sri Lanka date range) sent to the API.
class ActivityQuery {
  const ActivityQuery({this.page = 1, this.type, this.from, this.to});

  static const pageSize = 10;

  final int page;
  final String? type;
  final DateTime? from;
  final DateTime? to;

  Map<String, dynamic> toParams() => {
        'page': page,
        'pageSize': pageSize,
        'type': type,
        'from': from == null ? null : Fmt.apiDate(from!),
        'to': to == null ? null : Fmt.apiDate(to!),
      };

  ActivityQuery copyWith({int? page, String? type, DateTime? from, DateTime? to, bool clearType = false, bool clearFrom = false, bool clearTo = false}) =>
      ActivityQuery(
        page: page ?? this.page,
        type: clearType ? null : (type ?? this.type),
        from: clearFrom ? null : (from ?? this.from),
        to: clearTo ? null : (to ?? this.to),
      );

  bool get hasFilters => type != null || from != null || to != null;
}

/// Readable names for the action types (ActivityLog.EntityType), as on the web.
const activityTypeLabels = {
  'Account': 'Account',
  'BloodRequest': 'Blood requests',
  'Donation': 'Donations',
  'Screening': 'Screening',
  'Inventory': 'Inventory',
  'Transfer': 'Transfers',
  'Emergency': 'Emergencies',
  'Doctor': 'Doctors',
  'Complaint': 'Complaints',
  'Appeal': 'Appeals',
  'Hospital': 'Hospital',
  'Governance': 'Governance',
};

/// The three activity log sources (same endpoints as the web's activityApi).
class ActivityRepository {
  ActivityRepository(this._api);

  final ApiClient _api;

  Future<ActivityPage> mine(ActivityQuery q) async => ActivityPage.fromJson(unwrapMap(await _api.get('/activity-logs/my', query: q.toParams())));

  Future<ActivityPage> ofUser(String userId, ActivityQuery q) async =>
      ActivityPage.fromJson(unwrapMap(await _api.get('/Admin/users/$userId/activity-log', query: q.toParams())));

  Future<ActivityPage> ofHospital(String hospitalId, ActivityQuery q) async =>
      ActivityPage.fromJson(unwrapMap(await _api.get('/Admin/hospitals/$hospitalId/activity-log', query: q.toParams())));
}

final activityRepositoryProvider = Provider<ActivityRepository>((ref) => ActivityRepository(ref.watch(apiClientProvider)));

/// A full screen with a paged activity log.
class ActivityLogScreen extends StatelessWidget {
  const ActivityLogScreen({super.key, required this.title, required this.load, this.description});

  final String title;
  final String? description;
  final Future<ActivityPage> Function(WidgetRef ref, ActivityQuery q) load;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(title)),
        body: ActivityLogView(load: load, description: description),
      );
}

/// Paged activity log with a type filter and a date range (Sri Lanka dates); previous / next pages from the API.
class ActivityLogView extends ConsumerStatefulWidget {
  const ActivityLogView({super.key, required this.load, this.description});

  final Future<ActivityPage> Function(WidgetRef ref, ActivityQuery q) load;
  final String? description;

  @override
  ConsumerState<ActivityLogView> createState() => _ActivityLogViewState();
}

class _ActivityLogViewState extends ConsumerState<ActivityLogView> {
  ActivityQuery _q = const ActivityQuery();
  ActivityPage? _page;
  Object? _error;
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _fetch();
  }

  Future<void> _fetch() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final page = await widget.load(ref, _q);
      if (mounted) setState(() => _page = page);
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _set(ActivityQuery q) {
    _q = q;
    _fetch();
  }

  Future<void> _pickDate({required bool from}) async {
    final today = Fmt.sriLankaToday();
    final picked = await showDatePicker(
      context: context,
      initialDate: (from ? _q.from : _q.to) ?? today,
      firstDate: from ? DateTime(2026) : (_q.from ?? DateTime(2026)),
      lastDate: from ? (_q.to ?? today) : today,
      helpText: from ? 'From (Sri Lanka date)' : 'To (Sri Lanka date)',
    );
    if (picked != null) _set(from ? _q.copyWith(from: picked, page: 1) : _q.copyWith(to: picked, page: 1));
  }

  @override
  Widget build(BuildContext context) {
    final page = _page;
    final types = page?.types.isNotEmpty == true ? page!.types : activityTypeLabels.keys.toList();
    return RefreshableScroll(
      onRefresh: _fetch,
      child: ContentWidth(
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (widget.description != null) ...[
                Text(widget.description!, style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                const SizedBox(height: 8),
              ],
              Text('Activity is recorded from ${Fmt.date(page?.recordedFrom ?? DateTime.utc(2026, 10, 3))}. Sign-ins are not logged.',
                  style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
              const SizedBox(height: 10),
              FilterChips<String?>(
                options: [(null, 'All types'), for (final t in types) (t, activityTypeLabels[t] ?? Fmt.humanize(t))],
                selected: _q.type,
                onSelected: (t) => _set(t == null ? _q.copyWith(clearType: true, page: 1) : _q.copyWith(type: t, page: 1)),
              ),
              const SizedBox(height: 8),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                crossAxisAlignment: WrapCrossAlignment.center,
                children: [
                  _DateChip(label: 'From', value: _q.from, onTap: () => _pickDate(from: true), onClear: () => _set(_q.copyWith(clearFrom: true, page: 1))),
                  _DateChip(label: 'To', value: _q.to, onTap: () => _pickDate(from: false), onClear: () => _set(_q.copyWith(clearTo: true, page: 1))),
                  if (_q.hasFilters) TextButton(onPressed: () => _set(const ActivityQuery()), child: const Text('Clear filters')),
                ],
              ),
              const SizedBox(height: 12),
              if (_loading && page == null)
                const LoadingView(message: 'Loading activity...')
              else if (_error != null && page == null)
                ErrorView(error: _error!, onRetry: _fetch)
              else if (page != null) ...[
                if (_error != null) ...[InfoBanner(ApiError.from(_error!).message, color: AppColors.rose600), const SizedBox(height: 8)],
                if (page.items.isEmpty)
                  const EmptyView(icon: Icons.history, message: 'No activity matches these filters.')
                else
                  Card(
                    child: Column(
                      children: [
                        for (final e in page.items)
                          ListTile(
                            leading: const Icon(Icons.circle, size: 10, color: AppColors.red600),
                            title: Text(e.summary.isEmpty ? Fmt.humanize(e.action) : e.summary),
                            subtitle: Text(
                              '${e.actorName.isEmpty ? e.actorRole : e.actorName}'
                              '${e.actorRole.isNotEmpty && e.actorName.isNotEmpty ? ' (${e.actorRole})' : ''}'
                              ' · ${activityTypeLabels[e.entityType] ?? e.entityType} · ${Fmt.sriLankaDateTime(e.occurredAt)}',
                              style: const TextStyle(fontSize: 12),
                            ),
                          ),
                      ],
                    ),
                  ),
                const SizedBox(height: 8),
                Row(
                  children: [
                    Expanded(child: Text('${page.total} entr${page.total == 1 ? 'y' : 'ies'} · page ${page.page} of ${page.totalPages}', style: const TextStyle(fontSize: 12))),
                    IconButton(
                      key: const Key('activity-prev'),
                      tooltip: 'Previous page',
                      onPressed: _loading || _q.page <= 1 ? null : () => _set(_q.copyWith(page: _q.page - 1)),
                      icon: const Icon(Icons.chevron_left),
                    ),
                    IconButton(
                      key: const Key('activity-next'),
                      tooltip: 'Next page',
                      onPressed: _loading || _q.page >= page.totalPages ? null : () => _set(_q.copyWith(page: _q.page + 1)),
                      icon: const Icon(Icons.chevron_right),
                    ),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _DateChip extends StatelessWidget {
  const _DateChip({required this.label, required this.value, required this.onTap, required this.onClear});

  final String label;
  final DateTime? value;
  final VoidCallback onTap;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) => InputChip(
        avatar: const Icon(Icons.calendar_month, size: 16),
        label: Text(value == null ? label : '$label ${Fmt.apiDate(value!)}'),
        onPressed: onTap,
        onDeleted: value == null ? null : onClear,
      );
}
