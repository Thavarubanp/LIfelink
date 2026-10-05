import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/api/api_client.dart';
import '../../../core/api/json.dart';
import '../../../core/attachments/attachment.dart';
import '../../../core/providers.dart';
import '../../../core/routing/routes.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/badges.dart';
import '../../../core/widgets/common.dart';
import '../../../core/widgets/reply_sheet.dart';
import '../../../core/widgets/state_views.dart';
import '../../../core/widgets/thread_view.dart';
import '../attention_controller.dart';

/// ComplaintAuditLogDto: a reply or a status change in a complaint's history.
class ComplaintLog {
  const ComplaintLog({
    required this.id,
    required this.fromAdmin,
    required this.adminEmail,
    required this.previousStatus,
    required this.newStatus,
    required this.notes,
    required this.attachment,
    required this.isReply,
    required this.createdAt,
  });

  factory ComplaintLog.fromJson(Map<String, dynamic> j) => ComplaintLog(
        id: str(j['auditId']),
        fromAdmin: boolOf(j['fromAdmin']),
        adminEmail: j['adminEmail']?.toString(),
        previousStatus: str(j['previousStatus']),
        newStatus: str(j['newStatus']),
        notes: j['notes']?.toString(),
        attachment: Attachment.fromApi(j['attachmentUrl']?.toString(), j['attachmentName']?.toString()),
        isReply: boolOf(j['isReply']),
        createdAt: parseDate(j['createdAt']),
      );

  final String id;
  final bool fromAdmin;
  final String? adminEmail;
  final String previousStatus;
  final String newStatus;
  final String? notes;
  final Attachment? attachment;
  final bool isReply;
  final DateTime? createdAt;
}

/// ActivityReportResponseDto: a hospital activity report linked to a complaint (read-only here).
class ActivityReport {
  const ActivityReport({required this.id, required this.hospitalName, required this.title, required this.description, required this.submittedAt});

  factory ActivityReport.fromJson(Map<String, dynamic> j) => ActivityReport(
        id: str(j['reportId']),
        hospitalName: str(j['hospitalName']),
        title: str(j['title']),
        description: str(j['description']),
        submittedAt: parseDate(j['submittedAt']),
      );

  final String id;
  final String hospitalName;
  final String title;
  final String description;
  final DateTime? submittedAt;
}

enum ComplaintTarget { all, hospital, user, general }

/// ComplaintResponseDto (admin view).
class Complaint {
  const Complaint({
    required this.id,
    required this.userEmail,
    required this.hospitalId,
    required this.hospitalName,
    required this.targetUserId,
    required this.targetUserName,
    required this.complaintType,
    required this.subject,
    required this.description,
    required this.status,
    required this.createdAt,
    required this.logs,
    required this.reports,
    required this.awaitingAdminReply,
  });

  factory Complaint.fromJson(Map<String, dynamic> j) => Complaint(
        id: str(j['complaintId']),
        userEmail: j['userEmail']?.toString(),
        hospitalId: j['hospitalId']?.toString(),
        hospitalName: j['hospitalName']?.toString(),
        targetUserId: j['targetUserId']?.toString(),
        targetUserName: j['targetUserName']?.toString(),
        complaintType: str(j['complaintType']),
        subject: str(j['subject']),
        description: str(j['description']),
        status: str(j['status'], 'OPEN').toUpperCase(),
        createdAt: parseDate(j['createdAt']),
        logs: j['auditLogs'] is List
            ? (j['auditLogs'] as List).whereType<Map>().map((e) => ComplaintLog.fromJson(Map<String, dynamic>.from(e))).toList()
            : const [],
        reports: j['activityReports'] is List
            ? (j['activityReports'] as List).whereType<Map>().map((e) => ActivityReport.fromJson(Map<String, dynamic>.from(e))).toList()
            : const [],
        awaitingAdminReply: boolOf(j['awaitingAdminReply']),
      );

  final String id;
  final String? userEmail;
  final String? hospitalId;
  final String? hospitalName;
  final String? targetUserId;
  final String? targetUserName;
  final String complaintType;
  final String subject;
  final String description;
  final String status; // OPEN | RESOLVED | CANCELLED (REJECTED for old rows)
  final DateTime? createdAt;
  final List<ComplaintLog> logs;
  final List<ActivityReport> reports;
  final bool awaitingAdminReply;

  bool get isOpen => !const {'RESOLVED', 'REJECTED', 'CANCELLED'}.contains(status);

  /// Without a target the complaint is a general question to the admin.
  ComplaintTarget get target => (hospitalId?.isNotEmpty ?? false)
      ? ComplaintTarget.hospital
      : (targetUserId?.isNotEmpty ?? false)
          ? ComplaintTarget.user
          : ComplaintTarget.general;

  String get targetLabel => switch (target) {
        ComplaintTarget.hospital => 'Against ${hospitalName ?? 'a hospital'}',
        ComplaintTarget.user => 'Against ${targetUserName ?? 'a user'}',
        _ => 'General question (no target)',
      };

  /// The complaint as a conversation: the description first, then every reply (status changes as notes).
  List<ThreadMessage> thread() => [
        ThreadMessage(author: userEmail ?? 'Complainant', fromAdmin: false, at: createdAt, title: 'Complaint', text: description),
        for (final l in logs)
          ThreadMessage(
            author: l.fromAdmin ? 'Administrator' : (userEmail ?? 'Complainant'),
            fromAdmin: l.fromAdmin,
            at: l.createdAt,
            title: l.isReply || l.previousStatus == l.newStatus ? null : 'Status ${l.previousStatus} → ${l.newStatus}',
            text: l.notes,
            attachment: l.attachment,
          ),
      ];
}

/// Same status filter as the web page: OPEN = anything not resolved, rejected or cancelled.
bool complaintMatches(Complaint c, {required String status, required ComplaintTarget target, String query = ''}) {
  final s = c.status;
  if (status == 'OPEN' && !c.isOpen) return false;
  if (status == 'RESOLVED' && s != 'RESOLVED') return false;
  if (status == 'CANCELLED' && s != 'CANCELLED') return false;
  if (target != ComplaintTarget.all && c.target != target) return false;
  final q = query.trim().toLowerCase();
  if (q.isEmpty) return true;
  return [c.subject, c.description, c.complaintType, c.userEmail ?? '', c.hospitalName ?? '']
      .any((v) => v.toLowerCase().contains(q));
}

BadgeVariant complaintVariant(String status) => switch (status) {
      'RESOLVED' => BadgeVariant.success,
      'CANCELLED' || 'REJECTED' => BadgeVariant.neutral,
      _ => BadgeVariant.warning,
    };

/// /api/Admin/complaints (same calls as the web's AdminComplaintsPage).
class AdminComplaintsRepository {
  AdminComplaintsRepository(this._api);

  final ApiClient _api;

  Future<List<Complaint>> list() async => unwrapList(await _api.get('/Admin/complaints')).map(Complaint.fromJson).toList();

  Future<Complaint> byId(String id) async => Complaint.fromJson(unwrapMap(await _api.get('/Admin/complaints/$id')));

  /// The admin's reply (the only admin complaint action); replies alternate with the complaint creator.
  Future<void> reply(String id, String notes, Attachment? a) =>
      _api.put('/Admin/complaints/$id/review', body: {'notes': notes, 'attachmentUrl': a?.url, 'attachmentName': a?.name});
}

final adminComplaintsRepositoryProvider =
    Provider<AdminComplaintsRepository>((ref) => AdminComplaintsRepository(ref.watch(apiClientProvider)));

final adminComplaintsProvider = FutureProvider.autoDispose<List<Complaint>>((ref) async {
  final list = await ref.watch(adminComplaintsRepositoryProvider).list();
  list.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
  return list;
});

final adminComplaintProvider =
    FutureProvider.autoDispose.family<Complaint, String>((ref, id) => ref.watch(adminComplaintsRepositoryProvider).byId(id));

/// Admin complaints: status and target filters (incl. "General question"), search; each opens its thread.
class AdminComplaintsScreen extends ConsumerStatefulWidget {
  const AdminComplaintsScreen({super.key});

  @override
  ConsumerState<AdminComplaintsScreen> createState() => _AdminComplaintsScreenState();
}

class _AdminComplaintsScreenState extends ConsumerState<AdminComplaintsScreen> {
  String _status = 'OPEN';
  ComplaintTarget _target = ComplaintTarget.all;
  String _query = '';

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(adminComplaintsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Complaints')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminComplaintsProvider);
          await ref.read(adminComplaintsProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SearchField(hint: 'Search subject, type, complainant, hospital', onChanged: (v) => setState(() => _query = v)),
                const SizedBox(height: 10),
                FilterChips<String>(
                  options: const [('OPEN', 'Open'), ('RESOLVED', 'Resolved'), ('CANCELLED', 'Cancelled'), ('ALL', 'All')],
                  selected: _status,
                  onSelected: (s) => setState(() => _status = s),
                ),
                const SizedBox(height: 8),
                FilterChips<ComplaintTarget>(
                  options: const [
                    (ComplaintTarget.all, 'Any target'),
                    (ComplaintTarget.hospital, 'Against a hospital'),
                    (ComplaintTarget.user, 'Against a user'),
                    (ComplaintTarget.general, 'General question'),
                  ],
                  selected: _target,
                  onSelected: (t) => setState(() => _target = t),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(adminComplaintsProvider),
                  loadingMessage: 'Loading complaints...',
                  data: (all) {
                    final list = all.where((c) => complaintMatches(c, status: _status, target: _target, query: _query)).toList();
                    if (list.isEmpty) return const EmptyView(icon: Icons.report_outlined, message: 'No complaints match these filters.');
                    return Column(
                      children: [
                        for (final c in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: Card(
                              child: ListTile(
                                key: Key('complaint-${c.id}'),
                                onTap: () => context.push('${AppRoutes.adminComplaints}/${c.id}'),
                                title: Text(c.subject, style: const TextStyle(fontWeight: FontWeight.w700)),
                                subtitle: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text('${c.userEmail ?? 'Unknown'} · ${Fmt.sriLankaDateTime(c.createdAt)}'),
                                    Text(c.targetLabel, style: const TextStyle(fontSize: 12)),
                                    const SizedBox(height: 4),
                                    Wrap(spacing: 6, runSpacing: 4, children: [
                                      StatusBadge(c.status, variant: complaintVariant(c.status)),
                                      if (c.complaintType.isNotEmpty) StatusBadge(c.complaintType),
                                      if (c.awaitingAdminReply && c.isOpen) const StatusBadge('Your turn', variant: BadgeVariant.info),
                                    ]),
                                  ],
                                ),
                                trailing: const Icon(Icons.chevron_right),
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

/// One complaint: details, the thread, linked hospital activity reports (read-only) and the admin reply.
class AdminComplaintDetailScreen extends ConsumerWidget {
  const AdminComplaintDetailScreen({super.key, required this.complaintId});

  final String complaintId;

  Future<void> _reply(BuildContext context, WidgetRef ref, Complaint c) async {
    final sent = await showReplySheet(
      context,
      title: 'Reply to complaint',
      subtitle: c.subject,
      maxLength: 1000,
      requiredMessage: 'A reply message is required.',
      onSubmit: (text, file) => ref.read(adminComplaintsRepositoryProvider).reply(c.id, text, file),
    );
    if (sent) {
      ref.invalidate(adminComplaintProvider(complaintId));
      ref.invalidate(adminComplaintsProvider);
      ref.read(adminAttentionProvider.notifier).refresh();
      if (context.mounted) showSnack(context, 'The complainant has been notified.', type: SnackType.success, title: 'Reply sent');
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(adminComplaintProvider(complaintId));
    final c = value.value;
    return Scaffold(
      appBar: AppBar(title: const Text('Complaint')),
      floatingActionButton: c != null && c.isOpen && c.awaitingAdminReply
          ? FloatingActionButton.extended(
              key: const Key('complaint-reply'),
              backgroundColor: AppColors.red600,
              foregroundColor: Colors.white,
              onPressed: () => _reply(context, ref, c),
              icon: const Icon(Icons.reply),
              label: const Text('Reply'),
            )
          : null,
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminComplaintProvider(complaintId));
          await ref.read(adminComplaintProvider(complaintId).future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(adminComplaintProvider(complaintId)),
          data: (c) => ContentWidth(
            maxWidth: 720,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SectionCard(
                    title: c.subject,
                    icon: Icons.report_outlined,
                    trailing: StatusBadge(c.status, variant: complaintVariant(c.status)),
                    child: Wrap(
                      spacing: 20,
                      runSpacing: 12,
                      children: [
                        LabeledValue('Filed by', c.userEmail ?? 'Unknown'),
                        LabeledValue('Target', c.targetLabel),
                        LabeledValue('Type', c.complaintType.isEmpty ? '-' : c.complaintType),
                        LabeledValue('Filed', Fmt.sriLankaDateTime(c.createdAt)),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),
                  SectionCard(title: 'Conversation', icon: Icons.forum_outlined, child: ThreadView(messages: c.thread())),
                  const SizedBox(height: 12),
                  if (!c.isOpen)
                    const InfoBanner('This complaint is closed.', color: AppColors.slate500)
                  else if (!c.awaitingAdminReply)
                    const InfoBanner('Waiting for the complainant to reply (replies alternate).', color: AppColors.blue600),
                  if (c.reports.isNotEmpty) ...[
                    const SizedBox(height: 12),
                    SectionCard(
                      title: 'Hospital activity reports (${c.reports.length})',
                      icon: Icons.assignment_outlined,
                      child: Column(
                        children: [
                          for (final r in c.reports)
                            ListTile(
                              contentPadding: EdgeInsets.zero,
                              title: Text(r.title, style: const TextStyle(fontWeight: FontWeight.w600)),
                              subtitle: Text('${r.description}\n${r.hospitalName} · ${Fmt.sriLankaDateTime(r.submittedAt)}'),
                              isThreeLine: true,
                            ),
                        ],
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
