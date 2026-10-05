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
import '../../appeals/appeal_models.dart';
import '../attention_controller.dart';

/// /api/Admin/appeals (same calls as the web's AdminAppealsPage).
class AdminAppealsRepository {
  AdminAppealsRepository(this._api);

  final ApiClient _api;

  Future<List<Appeal>> list(String? status) async =>
      unwrapList(await _api.get('/Admin/appeals', query: {'status': status})).map(Appeal.fromJson).toList();

  /// Admin message in the thread (the appellant replies next); optional attachment.
  Future<void> reply(String id, String notes, Attachment? a) =>
      _api.put('/Admin/appeals/$id/reply', body: {'notes': notes, 'attachmentUrl': a?.url, 'attachmentName': a?.name});

  /// approve | reject | close | permanently-block, each with the admin's response (5–2000 characters).
  Future<void> decide(String id, String action, String response) => _api.put('/Admin/appeals/$id/$action', body: {'adminResponse': response});
}

final adminAppealsRepositoryProvider = Provider<AdminAppealsRepository>((ref) => AdminAppealsRepository(ref.watch(apiClientProvider)));

final adminAppealsProvider = FutureProvider.autoDispose.family<List<Appeal>, String?>((ref, status) async {
  final list = await ref.watch(adminAppealsRepositoryProvider).list(status);
  list.sort((a, b) => (b.submittedAt ?? DateTime(0)).compareTo(a.submittedAt ?? DateTime(0)));
  return list;
});

const appealFilters = <(String?, String)>[
  ('PENDING', 'Pending'),
  ('REJECTED', 'Rejected'),
  ('APPROVED', 'Approved'),
  ('CLOSED', 'Closed'),
  (null, 'All'),
];

/// The four decisions on an appeal, with the web page's wording.
enum AppealDecision {
  approve('approve', 'Approve appeal', 'Approve and reinstate the account. Your response is sent to the appellant.', 'Appeal approved'),
  reject('reject', 'Reject appeal', 'An appeal can be rejected only once. The appellant can still reply in the thread.', 'Appeal rejected'),
  close('close', 'Close appeal', 'The thread becomes read-only. The suspension itself is unchanged.', 'Appeal closed'),
  block('permanently-block', 'Permanently block', 'The account is blocked permanently and the appeal is closed. This cannot be undone.', 'Account permanently blocked');

  const AppealDecision(this.path, this.title, this.hint, this.done);
  final String path;
  final String title;
  final String hint;
  final String done;
}

/// Admin appeals: status filter chips, newest first; each opens its thread.
class AdminAppealsScreen extends ConsumerStatefulWidget {
  const AdminAppealsScreen({super.key});

  @override
  ConsumerState<AdminAppealsScreen> createState() => _AdminAppealsScreenState();
}

class _AdminAppealsScreenState extends ConsumerState<AdminAppealsScreen> {
  String? _status = 'PENDING';

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(adminAppealsProvider(_status));
    return Scaffold(
      appBar: AppBar(title: const Text('Appeals')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminAppealsProvider(_status));
          await ref.read(adminAppealsProvider(_status).future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                FilterChips<String?>(options: appealFilters, selected: _status, onSelected: (s) => setState(() => _status = s)),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(adminAppealsProvider(_status)),
                  loadingMessage: 'Loading appeals...',
                  data: (list) => list.isEmpty
                      ? EmptyView(icon: Icons.gavel_outlined, message: 'No ${_status?.toLowerCase() ?? ''} appeals found.')
                      : Column(
                          children: [
                            for (final a in list)
                              Padding(
                                padding: const EdgeInsets.only(bottom: 10),
                                child: Card(
                                  child: ListTile(
                                    key: Key('appeal-${a.id}'),
                                    onTap: () => context.push('${AppRoutes.adminAppeals}/${a.id}'),
                                    leading: Icon(a.isHospital ? Icons.local_hospital_outlined : Icons.person_outline, color: AppColors.red600),
                                    title: Text(a.who, style: const TextStyle(fontWeight: FontWeight.w700)),
                                    subtitle: Column(
                                      crossAxisAlignment: CrossAxisAlignment.start,
                                      children: [
                                        Text('${a.isHospital ? 'Hospital appeal' : 'User appeal'} · ${Fmt.sriLankaDateTime(a.submittedAt)}'),
                                        Text(a.reason, maxLines: 2, overflow: TextOverflow.ellipsis),
                                        const SizedBox(height: 4),
                                        Wrap(spacing: 6, children: [
                                          StatusBadge(a.status, variant: appealVariant(a.status)),
                                          if (a.awaitingAdminReply && !a.isClosed) const StatusBadge('Your turn', variant: BadgeVariant.info),
                                        ]),
                                      ],
                                    ),
                                    trailing: const Icon(Icons.chevron_right),
                                  ),
                                ),
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

/// One appeal: the thread and the admin's actions (reply, approve, reject once, close, permanently block).
class AdminAppealDetailScreen extends ConsumerWidget {
  const AdminAppealDetailScreen({super.key, required this.appealId});

  final String appealId;

  /// The full list (null = every status), so a decided appeal stays visible here after leaving the filtered list.
  static const String? status = null;

  void _reload(WidgetRef ref) {
    ref.invalidate(adminAppealsProvider);
    ref.read(adminAttentionProvider.notifier).refresh();
  }

  Future<void> _decide(BuildContext context, WidgetRef ref, Appeal a, AppealDecision d) async {
    if (d == AppealDecision.block &&
        !await confirmDialog(context,
            title: 'Permanently block?', message: 'Block ${a.who} permanently? This cannot be undone.', confirmLabel: 'Continue', destructive: true)) {
      return;
    }
    if (!context.mounted) return;
    final sent = await showReplySheet(
      context,
      title: d.title,
      subtitle: d.hint,
      label: 'Response to the appellant',
      submitLabel: d.title,
      minLength: 5,
      maxLength: 2000,
      requiredMessage: 'Please write a response of at least 5 characters.',
      allowAttachment: false,
      destructive: d == AppealDecision.block || d == AppealDecision.reject,
      onSubmit: (text, _) => ref.read(adminAppealsRepositoryProvider).decide(a.id, d.path, text),
    );
    if (sent && context.mounted) {
      showSnack(context, d.done, type: SnackType.success);
      _reload(ref);
    }
  }

  Future<void> _reply(BuildContext context, WidgetRef ref, Appeal a) async {
    final sent = await showReplySheet(
      context,
      title: 'Reply to appeal',
      subtitle: 'The appellant answers after your message.',
      maxLength: 1000,
      requiredMessage: 'A reply message is required.',
      onSubmit: (text, file) => ref.read(adminAppealsRepositoryProvider).reply(a.id, text, file),
    );
    if (sent && context.mounted) {
      showSnack(context, 'The appellant has been notified.', type: SnackType.success, title: 'Reply sent');
      _reload(ref);
    }
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(adminAppealsProvider(status));
    return Scaffold(
      appBar: AppBar(title: const Text('Appeal')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminAppealsProvider(status));
          await ref.read(adminAppealsProvider(status).future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(adminAppealsProvider(status)),
          data: (list) {
            final a = list.where((x) => x.id == appealId).firstOrNull;
            if (a == null) return const EmptyView(icon: Icons.search_off, message: 'This appeal was not found.');
            return ContentWidth(
              maxWidth: 720,
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SectionCard(
                      title: a.who,
                      icon: a.isHospital ? Icons.local_hospital_outlined : Icons.person_outline,
                      trailing: StatusBadge(a.status, variant: appealVariant(a.status)),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('${a.isHospital ? 'Hospital appeal' : 'User appeal'} · submitted ${Fmt.sriLankaDateTime(a.submittedAt)}',
                              style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                          const SizedBox(height: 8),
                          Text(a.reason),
                        ],
                      ),
                    ),
                    const SizedBox(height: 12),
                    SectionCard(
                      title: 'Conversation',
                      icon: Icons.forum_outlined,
                      child: ThreadView(messages: a.thread(appellantLabel: a.isHospital ? 'Hospital' : 'Appellant')),
                    ),
                    const SizedBox(height: 12),
                    if (a.isClosed)
                      const InfoBanner('This appeal is closed. The thread is read-only.', color: AppColors.slate500)
                    else
                      SectionCard(
                        title: 'Actions',
                        icon: Icons.gavel_outlined,
                        child: Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            // The admin may send several messages in a row (the appellant answers after an admin message)
                            OutlinedButton.icon(
                              key: const Key('appeal-reply'),
                              onPressed: () => _reply(context, ref, a),
                              icon: const Icon(Icons.reply),
                              label: const Text('Reply'),
                            ),
                            FilledButton.icon(
                              key: const Key('appeal-approve'),
                              style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600),
                              onPressed: () => _decide(context, ref, a, AppealDecision.approve),
                              icon: const Icon(Icons.check),
                              label: const Text('Approve'),
                            ),
                            // An appeal can be rejected only once (the API answers 409 to a second reject)
                            Tooltip(
                              message: a.canReject ? 'Reject' : 'This appeal has already been rejected.',
                              child: FilledButton.icon(
                                key: const Key('appeal-reject'),
                                style: FilledButton.styleFrom(backgroundColor: AppColors.rose600),
                                onPressed: a.canReject ? () => _decide(context, ref, a, AppealDecision.reject) : null,
                                icon: const Icon(Icons.close),
                                label: const Text('Reject'),
                              ),
                            ),
                            OutlinedButton.icon(
                              key: const Key('appeal-close'),
                              onPressed: () => _decide(context, ref, a, AppealDecision.close),
                              icon: const Icon(Icons.lock_outline),
                              label: const Text('Close'),
                            ),
                            OutlinedButton.icon(
                              key: const Key('appeal-block'),
                              style: OutlinedButton.styleFrom(foregroundColor: AppColors.rose600),
                              onPressed: () => _decide(context, ref, a, AppealDecision.block),
                              icon: const Icon(Icons.block),
                              label: const Text('Permanently block'),
                            ),
                          ],
                        ),
                      ),
                    if (!a.isClosed && !a.canReject && a.rejectedAt != null) ...[
                      const SizedBox(height: 8),
                      Text('Rejected on ${Fmt.sriLankaDateTime(a.rejectedAt)}; it cannot be rejected again.',
                          style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                    ],
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}
