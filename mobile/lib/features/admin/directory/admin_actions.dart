import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/api_client.dart';
import '../../../core/api/api_error.dart';
import '../../../core/providers.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/common.dart';
import '../../auth/auth_widgets.dart';
import '../attention_controller.dart';
import '../registrations/registration_repository.dart';
import 'directory.dart';

/// Admin account actions (same calls as the web): one-way messages, suspend with a reason, reinstate.
class AdminAccountRepository {
  AdminAccountRepository(this._api);

  final ApiClient _api;

  /// "Message from Administrator" to one user or one hospital (doctors via their hospital). No replies.
  Future<void> message({String? userId, String? hospitalId, required String subject, required String message}) =>
      _api.post('/Admin/messages', body: {'userId': userId, 'hospitalId': hospitalId, 'subject': subject, 'message': message});

  Future<void> suspend(String kind, String id, String reason, DateTime? until) => _api.put('/Admin/$kind/$id/suspend', body: {
        'reason': reason,
        // End of the chosen Sri Lanka day, as UTC
        'suspendedUntil': until == null
            ? null
            : DateTime.utc(until.year, until.month, until.day, 23, 59, 59).subtract(const Duration(hours: 5, minutes: 30)).toIso8601String(),
      });

  Future<void> reinstate(String kind, String id) => _api.put('/Admin/$kind/$id/reinstate');
}

final adminAccountRepositoryProvider = Provider<AdminAccountRepository>((ref) => AdminAccountRepository(ref.watch(apiClientProvider)));

/// Message rules (web AdminMessageButton): subject 3–120, message 5–2000 characters.
String? adminMessageError(String subject, String message) {
  final s = subject.trim();
  final m = message.trim();
  if (s.length < 3) return 'The subject needs at least 3 characters.';
  if (s.length > 120) return 'The subject cannot exceed 120 characters.';
  if (m.length < 5) return 'The message needs at least 5 characters.';
  if (m.length > 2000) return 'The message cannot exceed 2000 characters.';
  return null;
}

/// Suspension reason rules (SuspendUserDto / SuspendHospitalDto): 3–500 characters.
String? suspendReasonError(String reason) {
  final r = reason.trim();
  if (r.length < 3) return 'Suspension reason must be at least 3 characters.';
  if (r.length > 500) return 'Suspension reason cannot exceed 500 characters.';
  return null;
}

/// Profile buttons for a user (donor/patient accounts only can be suspended; Block and Promote stay on the web).
List<Widget> userAdminActions(BuildContext context, WidgetRef ref, AdminUser u) => [
      FilledButton.icon(
        key: const Key('send-message'),
        onPressed: () => showAdminMessageSheet(context, userId: u.userId, recipient: u.name.isEmpty ? u.email : u.name),
        icon: const Icon(Icons.send, size: 18),
        label: const Text('Send message'),
      ),
      if (u.isDonorPatient && !u.isPermanentlyBlocked)
        u.isSuspended
            ? _ReinstateButton(kind: 'users', id: u.userId, name: u.name, onDone: () => ref.invalidate(adminUsersProvider))
            : OutlinedButton.icon(
                key: const Key('suspend-account'),
                style: OutlinedButton.styleFrom(foregroundColor: AppColors.rose600),
                onPressed: () => _suspend(context, ref, 'users', u.userId, u.name, () => ref.invalidate(adminUsersProvider)),
                icon: const Icon(Icons.pause_circle_outline, size: 18),
                label: const Text('Suspend'),
              ),
    ];

/// Profile buttons for an approved hospital.
List<Widget> hospitalAdminActions(BuildContext context, WidgetRef ref, AdminHospital h) => [
      FilledButton.icon(
        key: const Key('send-message'),
        onPressed: () => showAdminMessageSheet(context, hospitalId: h.hospitalId, recipient: h.name),
        icon: const Icon(Icons.send, size: 18),
        label: const Text('Send message'),
      ),
      if (h.approvalStatus == 'Approved')
        h.isSuspended
            ? _ReinstateButton(kind: 'hospitals', id: h.hospitalId, name: h.name, onDone: () => ref.invalidate(adminHospitalsProvider))
            : OutlinedButton.icon(
                key: const Key('suspend-account'),
                style: OutlinedButton.styleFrom(foregroundColor: AppColors.rose600),
                onPressed: () => _suspend(context, ref, 'hospitals', h.hospitalId, h.name, () => ref.invalidate(adminHospitalsProvider)),
                icon: const Icon(Icons.pause_circle_outline, size: 18),
                label: const Text('Suspend'),
              ),
    ];

Future<void> _suspend(BuildContext context, WidgetRef ref, String kind, String id, String name, VoidCallback onDone) async {
  final done = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    builder: (_) => SuspendSheet(kind: kind, id: id, name: name),
  );
  if (done == true) {
    onDone();
    ref.read(adminAttentionProvider.notifier).refresh();
    if (context.mounted) showSnack(context, '$name is suspended and has been notified.', type: SnackType.success, title: 'Suspended');
  }
}

class _ReinstateButton extends ConsumerStatefulWidget {
  const _ReinstateButton({required this.kind, required this.id, required this.name, required this.onDone});

  final String kind;
  final String id;
  final String name;
  final VoidCallback onDone;

  @override
  ConsumerState<_ReinstateButton> createState() => _ReinstateButtonState();
}

class _ReinstateButtonState extends ConsumerState<_ReinstateButton> {
  bool _busy = false;

  Future<void> _run() async {
    final ok = await confirmDialog(context,
        title: 'Reinstate', message: 'Reinstate ${widget.name}? An open appeal is approved at the same time.', confirmLabel: 'Reinstate');
    if (!ok) return;
    setState(() => _busy = true);
    try {
      await ref.read(adminAccountRepositoryProvider).reinstate(widget.kind, widget.id);
      if (mounted) showSnack(context, '${widget.name} has been reinstated.', type: SnackType.success);
    } catch (e) {
      if (mounted) showErrorSnack(context, e, title: 'Not reinstated');
    } finally {
      widget.onDone();
      ref.read(adminAttentionProvider.notifier).refresh();
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => OutlinedButton.icon(
        key: const Key('reinstate-account'),
        style: OutlinedButton.styleFrom(foregroundColor: AppColors.emerald600),
        onPressed: _busy ? null : _run,
        icon: _busy ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.play_circle_outline, size: 18),
        label: const Text('Reinstate'),
      );
}

/// Suspend with a required reason and an optional end date.
class SuspendSheet extends ConsumerStatefulWidget {
  const SuspendSheet({super.key, required this.kind, required this.id, required this.name});

  final String kind; // users | hospitals
  final String id;
  final String name;

  @override
  ConsumerState<SuspendSheet> createState() => _SuspendSheetState();
}

class _SuspendSheetState extends ConsumerState<SuspendSheet> {
  final _reason = TextEditingController();
  DateTime? _until;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final problem = suspendReasonError(_reason.text);
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(adminAccountRepositoryProvider).suspend(widget.kind, widget.id, _reason.text.trim(), _until);
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Suspend ${widget.name}', style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
              const SizedBox(height: 6),
              Text(
                widget.kind == 'hospitals'
                    ? 'Its staff and doctors can only see the governance page and appeal until you reinstate it.'
                    : 'They can only see the governance page and appeal until you reinstate them.',
                style: const TextStyle(fontSize: 12),
              ),
              const SizedBox(height: 14),
              TextField(
                key: const Key('suspend-reason'),
                controller: _reason,
                maxLines: 3,
                maxLength: 500,
                decoration: const InputDecoration(labelText: 'Reason (shown to them)'),
              ),
              InputChip(
                avatar: const Icon(Icons.event, size: 16),
                label: Text(_until == null ? 'Until lifted (optional end date)' : 'Until ${Fmt.apiDate(_until!)}'),
                onPressed: () async {
                  final today = Fmt.sriLankaToday();
                  final picked = await showDatePicker(
                    context: context,
                    initialDate: _until ?? today.add(const Duration(days: 7)),
                    firstDate: today,
                    lastDate: today.add(const Duration(days: 3650)),
                  );
                  if (picked != null) setState(() => _until = picked);
                },
                onDeleted: _until == null ? null : () => setState(() => _until = null),
              ),
              const SizedBox(height: 14),
              if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
              BusyButton(key: const Key('suspend-submit'), label: 'Suspend', busy: _busy, onPressed: _save),
            ],
          ),
        ),
      );
}

/// One-way "Message from Administrator" sheet.
Future<void> showAdminMessageSheet(BuildContext context, {String? userId, String? hospitalId, required String recipient}) async {
  final sent = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    builder: (_) => AdminMessageSheet(userId: userId, hospitalId: hospitalId, recipient: recipient),
  );
  if (sent == true && context.mounted) {
    showSnack(context, '$recipient will see it in their notifications.', type: SnackType.success, title: 'Message sent');
  }
}

class AdminMessageSheet extends ConsumerStatefulWidget {
  const AdminMessageSheet({super.key, this.userId, this.hospitalId, required this.recipient});

  final String? userId;
  final String? hospitalId;
  final String recipient;

  @override
  ConsumerState<AdminMessageSheet> createState() => _AdminMessageSheetState();
}

class _AdminMessageSheetState extends ConsumerState<AdminMessageSheet> {
  final _subject = TextEditingController();
  final _message = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _subject.dispose();
    _message.dispose();
    super.dispose();
  }

  Future<void> _send() async {
    final problem = adminMessageError(_subject.text, _message.text);
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(adminAccountRepositoryProvider).message(
            userId: widget.userId,
            hospitalId: widget.hospitalId,
            subject: _subject.text.trim(),
            message: _message.text.trim(),
          );
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Message to ${widget.recipient}', style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
              const SizedBox(height: 6),
              const Text('A one-way message shown in their notifications. They cannot reply; to contact you they file a complaint.',
                  style: TextStyle(fontSize: 12)),
              const SizedBox(height: 14),
              TextField(key: const Key('message-subject'), controller: _subject, maxLength: 120, decoration: const InputDecoration(labelText: 'Subject')),
              TextField(
                key: const Key('message-body'),
                controller: _message,
                maxLines: 5,
                maxLength: 2000,
                decoration: const InputDecoration(labelText: 'Message'),
              ),
              const SizedBox(height: 8),
              if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
              BusyButton(key: const Key('message-send'), label: 'Send message', icon: Icons.send, busy: _busy, onPressed: _send),
            ],
          ),
        ),
      );
}
