import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/attachments/attachment.dart';
import '../../core/attachments/attachment_widgets.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/reply_sheet.dart';
import '../../core/widgets/state_views.dart';
import '../../core/widgets/thread_view.dart';
import '../appeals/appeal_models.dart';
import '../auth/auth_widgets.dart';
import 'governance_repository.dart';

/// Governance status for every role (web: SuspendedGovernancePage): the suspension and its reason, the appeal threads
/// with replies when it is the appellant's turn, and a new appeal when allowed. Suspended accounts can only reach this.
class GovernanceStatusScreen extends ConsumerStatefulWidget {
  const GovernanceStatusScreen({super.key});

  @override
  ConsumerState<GovernanceStatusScreen> createState() => _GovernanceStatusScreenState();
}

class _GovernanceStatusScreenState extends ConsumerState<GovernanceStatusScreen> {
  final _reason = TextEditingController();
  Attachment? _file;
  bool _submitting = false;
  String? _error;

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  Future<void> _refresh() async {
    ref.invalidate(governanceStatusProvider);
    await ref.read(governanceStatusProvider.future).then((_) {}, onError: (_) {});
    // An approved appeal reinstates the account: the router then leaves this screen
    try {
      await ref.read(authControllerProvider.notifier).refreshUser();
    } catch (_) {
      // Shown by the status view
    }
  }

  Future<void> _submit() async {
    final problem = appealReasonError(_reason.text);
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      await ref.read(governanceRepositoryProvider).submitAppeal(_reason.text.trim(), _file);
      if (!mounted) return;
      showSnack(context, 'The administrator will review it. You will see the answer here and in your notifications.',
          type: SnackType.success, title: 'Appeal submitted');
      _reason.clear();
      setState(() => _file = null);
      ref.invalidate(governanceStatusProvider);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      setState(() => _error = api.message);
      if (api.isConflict) ref.invalidate(governanceStatusProvider);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  Future<void> _reply(Appeal a) async {
    final sent = await showReplySheet(
      context,
      title: 'Reply to the administrator',
      maxLength: 1000,
      requiredMessage: 'A reply message is required.',
      onSubmit: (text, file) => ref.read(governanceRepositoryProvider).reply(a.id, text, file),
    );
    ref.invalidate(governanceStatusProvider);
    if (sent && mounted) showSnack(context, 'The administrator has been notified.', type: SnackType.success, title: 'Reply sent');
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(governanceStatusProvider);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Account status'),
        actions: [
          IconButton(
            tooltip: 'All my appeals',
            icon: const Icon(Icons.history),
            onPressed: () => context.push(AppRoutes.myAppeals),
          ),
        ],
      ),
      body: RefreshableScroll(
        onRefresh: _refresh,
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(governanceStatusProvider),
          loadingMessage: 'Loading your account status...',
          data: (s) => ContentWidth(maxWidth: 640, child: _body(s)),
        ),
      ),
    );
  }

  Widget _body(GovernanceStatus s) {
    final appeals = s.newestFirst;
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionCard(
            title: s.isPermanentlyBlocked
                ? 'Account permanently blocked'
                : s.isSuspended
                    ? (s.isHospital ? 'Your hospital is suspended' : 'Your account is suspended')
                    : 'Your account is active',
            icon: s.isSuspended || s.isPermanentlyBlocked ? Icons.gpp_maybe_outlined : Icons.verified_user_outlined,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text([s.profileName, s.profileEmail, s.profileRole, ?s.hospitalName].where((x) => x.isNotEmpty).join(' · '),
                    style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                if (s.suspensionReason?.isNotEmpty == true) ...[
                  const SizedBox(height: 10),
                  InfoBanner('Reason: ${s.suspensionReason}', color: AppColors.rose600, icon: Icons.info_outline),
                ],
                if (s.suspendedUntil != null) ...[
                  const SizedBox(height: 6),
                  Text('Suspended until: ${Fmt.sriLankaDateTime(s.suspendedUntil)}', style: const TextStyle(color: AppColors.amber600)),
                ],
                if (s.isSuspended) ...[
                  const SizedBox(height: 8),
                  const Text('While suspended you can only read this page and appeal.', style: TextStyle(fontSize: 12)),
                ],
                if (s.isReadOnlyViewer) ...[
                  const SizedBox(height: 8),
                  const Text('Doctors can read the hospital\'s appeal threads; hospital staff send the appeal and replies.',
                      style: TextStyle(fontSize: 12)),
                ],
              ],
            ),
          ),
          const SizedBox(height: 12),
          for (final a in appeals) ...[
            SectionCard(
              title: 'Appeal · ${Fmt.date(a.submittedAt)}',
              icon: Icons.gavel_outlined,
              trailing: StatusBadge(a.status, variant: appealVariant(a.status)),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ThreadView(messages: a.thread(appellantLabel: s.isHospital ? 'Hospital' : 'You')),
                  if (a.awaitingAdminReply && !a.isClosed)
                    const Text('Waiting for the administrator.', style: TextStyle(fontSize: 12, color: AppColors.slate500)),
                  if (!s.isReadOnlyViewer && a.canAppellantReply)
                    Align(
                      alignment: Alignment.centerLeft,
                      child: OutlinedButton.icon(
                        key: Key('appeal-reply-${a.id}'),
                        onPressed: () => _reply(a),
                        icon: const Icon(Icons.reply),
                        label: const Text('Reply'),
                      ),
                    ),
                ],
              ),
            ),
            const SizedBox(height: 12),
          ],
          if (s.canAppeal && !s.isReadOnlyViewer)
            SectionCard(
              title: 'Submit an appeal',
              icon: Icons.edit_note,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  TextField(
                    key: const Key('appeal-reason'),
                    controller: _reason,
                    maxLines: 5,
                    maxLength: 2000,
                    decoration: const InputDecoration(labelText: 'Why should the suspension be lifted? (at least 10 characters)'),
                  ),
                  AttachmentPickerField(value: _file, enabled: !_submitting, onChanged: (a) => setState(() => _file = a)),
                  const SizedBox(height: 12),
                  if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
                  BusyButton(key: const Key('appeal-submit'), label: 'Submit appeal', busy: _submitting, onPressed: _submit),
                ],
              ),
            ),
          if (appeals.isEmpty && !s.canAppeal)
            const EmptyView(icon: Icons.gavel_outlined, message: 'There are no appeals to show.'),
          const SizedBox(height: 16),
          OutlinedButton.icon(
            onPressed: () => ref.read(authControllerProvider.notifier).logout(),
            icon: const Icon(Icons.logout),
            label: const Text('Sign out'),
          ),
        ],
      ),
    );
  }
}

/// "My appeals" for any role (web: MyAppealsPage): every appeal the account has sent, with its thread.
class MyAppealsScreen extends ConsumerWidget {
  const MyAppealsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(myAppealsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('My appeals')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(myAppealsProvider);
          await ref.read(myAppealsProvider.future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(myAppealsProvider),
          data: (list) => list.isEmpty
              ? const EmptyView(icon: Icons.gavel_outlined, message: 'You have not sent any appeals.')
              : ContentWidth(
                  maxWidth: 640,
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      children: [
                        for (final a in list) ...[
                          SectionCard(
                            title: 'Appeal · ${Fmt.date(a.submittedAt)}',
                            icon: Icons.gavel_outlined,
                            trailing: StatusBadge(a.status, variant: appealVariant(a.status)),
                            child: ThreadView(messages: a.thread(appellantLabel: 'You')),
                          ),
                          const SizedBox(height: 12),
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
