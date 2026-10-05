import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/api_error.dart';
import '../../../core/attachments/attachment.dart';
import '../../../core/attachments/attachment_widgets.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/badges.dart';
import '../../../core/widgets/common.dart';
import '../../../core/widgets/state_views.dart';
import '../../../core/widgets/thread_view.dart';
import '../../auth/auth_widgets.dart';
import '../attention_controller.dart';
import 'registration_repository.dart';
import 'registrations_screen.dart' show registrationVariant;

/// One registration: details, documents, the conversation with the hospital, and the admin's decision
/// (approve / reject with a reason and optional report / comment), with the web page's rules.
class RegistrationDetailScreen extends ConsumerStatefulWidget {
  const RegistrationDetailScreen({super.key, required this.hospitalId});

  final String hospitalId;

  @override
  ConsumerState<RegistrationDetailScreen> createState() => _RegistrationDetailScreenState();
}

class _RegistrationDetailScreenState extends ConsumerState<RegistrationDetailScreen> {
  final _text = TextEditingController();
  Attachment? _file;
  String? _busy; // approve | reject | comment
  String? _error;

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  void _reload() {
    ref.invalidate(adminHospitalsProvider);
    ref.read(adminAttentionProvider.notifier).refresh();
  }

  String? _draft(int minimum, String message) {
    final t = _text.text.trim();
    if (t.length < minimum) {
      setState(() => _error = message);
      return null;
    }
    return t;
  }

  Future<void> _run(String action, Future<void> Function() call, String title, String message) async {
    setState(() {
      _busy = action;
      _error = null;
    });
    try {
      await call();
      if (!mounted) return;
      showSnack(context, message, type: SnackType.success, title: title);
      _text.clear();
      setState(() => _file = null);
      _reload();
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      setState(() => _error = api.message);
      // Already decided, or the hospital replied meanwhile: show the current conversation
      if (api.isConflict) _reload();
    } finally {
      if (mounted) setState(() => _busy = null);
    }
  }

  Future<void> _approve(AdminHospital h) async {
    final ok = await confirmDialog(context, title: 'Approve hospital', message: 'Approve ${h.name}? Its staff can then use LifeLink.', confirmLabel: 'Approve');
    if (!ok) return;
    await _run('approve', () => ref.read(registrationRepositoryProvider).approve(h), 'Hospital approved', '${h.name} can now use LifeLink.');
  }

  Future<void> _reject(AdminHospital h) async {
    final reason = _draft(3, 'Please give a rejection reason (at least 3 characters).');
    if (reason == null) return;
    await _run('reject', () => ref.read(registrationRepositoryProvider).reject(h, reason, _file), 'Registration rejected',
        'The hospital can now reply and correct its details in the same conversation.');
  }

  Future<void> _comment(AdminHospital h) async {
    final message = _draft(3, 'Please write a comment (at least 3 characters).');
    if (message == null) return;
    await _run('comment', () => ref.read(registrationRepositoryProvider).comment(h, message, _file), 'Comment sent', 'The hospital has been notified.');
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(adminHospitalsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Registration')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminHospitalsProvider);
          await ref.read(adminHospitalsProvider.future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(adminHospitalsProvider),
          data: (all) {
            final h = all.where((x) => x.hospitalId == widget.hospitalId).firstOrNull;
            if (h == null) return const EmptyView(icon: Icons.search_off, message: 'This hospital registration was not found.');
            return ContentWidth(maxWidth: 720, child: _body(h));
          },
        ),
      ),
    );
  }

  Widget _body(AdminHospital h) => Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            SectionCard(
              title: h.name,
              icon: Icons.local_hospital_outlined,
              trailing: StatusBadge(h.statusLabel, variant: registrationVariant(h.approvalStatus)),
              child: Wrap(
                spacing: 20,
                runSpacing: 12,
                children: [
                  LabeledValue('Registration no.', h.registrationNumber ?? h.licenseNumber),
                  LabeledValue('Address', [h.address, h.city].whereType<String>().where((s) => s.isNotEmpty).join(', ')),
                  LabeledValue('Contact', h.contactNumber),
                  LabeledValue('Email', h.email),
                  LabeledValue('Authorised person', [h.contactPersonName, h.contactPersonPhone].whereType<String>().join(' · ')),
                  LabeledValue('Submitted', Fmt.sriLankaDateTime(h.createdAt)),
                ],
              ),
            ),
            const SizedBox(height: 12),
            SectionCard(
              title: 'Documents',
              icon: Icons.folder_open_outlined,
              child: h.licenseDocument == null && h.accreditationDocument == null
                  ? const Text('No documents were uploaded.', style: TextStyle(color: AppColors.slate500))
                  : Wrap(
                      spacing: 12,
                      runSpacing: 12,
                      children: [
                        if (h.licenseDocument != null) AttachmentView(attachment: h.licenseDocument!, label: 'License'),
                        if (h.accreditationDocument != null) AttachmentView(attachment: h.accreditationDocument!, label: 'Accreditation'),
                      ],
                    ),
            ),
            const SizedBox(height: 12),
            SectionCard(
              title: 'Conversation',
              icon: Icons.forum_outlined,
              child: ThreadView(
                messages: [
                  for (final e in h.history)
                    ThreadMessage(
                      title: e.title,
                      author: e.fromAdmin ? (e.adminName ?? 'Administrator') : h.name,
                      fromAdmin: e.fromAdmin,
                      at: e.timestamp,
                      text: e.message,
                      detail: e.changedFields,
                      attachment: e.attachment,
                    ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            _actions(h),
          ],
        ),
      );

  Widget _actions(AdminHospital h) {
    if (!h.canApprove && !h.canComment) {
      return InfoBanner(
        h.waitingForHospital ? 'Waiting for the hospital to reply.' : 'No action is needed on this registration.',
        color: AppColors.blue600,
      );
    }
    final busy = _busy != null;
    return SectionCard(
      title: 'Your decision',
      icon: Icons.gavel_outlined,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            h.canReject
                ? 'Approve the registration, or reject it with a reason. The hospital then replies in this conversation.'
                : 'The hospital must reply next. You can add a comment to the request.',
            style: const TextStyle(fontSize: 12),
          ),
          const SizedBox(height: 10),
          TextField(
            key: const Key('registration-text'),
            controller: _text,
            maxLines: 4,
            maxLength: h.canReject ? 500 : 1000,
            decoration: InputDecoration(labelText: h.canReject ? 'Reason (required to reject) or comment' : 'Comment'),
          ),
          AttachmentPickerField(
            value: _file,
            enabled: !busy,
            label: h.canReject ? 'Review report or attachment (optional)' : 'Attachment (optional)',
            onChanged: (a) => setState(() => _file = a),
          ),
          const SizedBox(height: 12),
          if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              if (h.canApprove)
                FilledButton.icon(
                  key: const Key('registration-approve'),
                  style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600),
                  onPressed: busy ? null : () => _approve(h),
                  icon: _busy == 'approve' ? _spinner : const Icon(Icons.check),
                  label: const Text('Approve'),
                ),
              if (h.canReject)
                FilledButton.icon(
                  key: const Key('registration-reject'),
                  style: FilledButton.styleFrom(backgroundColor: AppColors.rose600),
                  onPressed: busy ? null : () => _reject(h),
                  icon: _busy == 'reject' ? _spinner : const Icon(Icons.close),
                  label: const Text('Reject'),
                ),
              if (h.canComment)
                OutlinedButton.icon(
                  key: const Key('registration-comment'),
                  onPressed: busy ? null : () => _comment(h),
                  icon: _busy == 'comment' ? _spinner : const Icon(Icons.comment_outlined),
                  label: const Text('Comment'),
                ),
            ],
          ),
        ],
      ),
    );
  }

  static const _spinner = SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2));
}
