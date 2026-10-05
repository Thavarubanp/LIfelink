import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/api/idempotency.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/reply_sheet.dart';
import '../../core/widgets/state_views.dart';
import '../../core/widgets/thread_view.dart';
import '../admin/complaints/admin_complaints.dart';
import '../blood_requests/blood_request_models.dart';
import 'complaints_repository.dart';

class ComplaintsScreen extends ConsumerStatefulWidget {
  const ComplaintsScreen({super.key});
  @override
  ConsumerState<ComplaintsScreen> createState() => _ComplaintsScreenState();
}

class _ComplaintsScreenState extends ConsumerState<ComplaintsScreen> {
  String _status = 'OPEN';
  String _search = '';
  @override
  Widget build(BuildContext context) {
    final value = ref.watch(myComplaintsProvider);
    return Scaffold(appBar: AppBar(title: const Text('Complaints'), actions: [
      IconButton(onPressed: () => _showCreate(context), icon: const Icon(Icons.add), tooltip: 'File complaint'),
    ]), body: RefreshableScroll(onRefresh: () async {
      ref.invalidate(myComplaintsProvider); await ref.read(myComplaintsProvider.future).then((_) {}, onError: (_) {});
    }, child: ContentWidth(child: Padding(padding: const EdgeInsets.all(16), child: Column(children: [
      SearchField(hint: 'Search complaints', onChanged: (v) => setState(() => _search = v)),
      const SizedBox(height: 8),
      FilterChips<String>(options: const [('OPEN', 'Open'), ('RESOLVED', 'Resolved'), ('ALL', 'All')],
        selected: _status, onSelected: (v) => setState(() => _status = v)),
      const SizedBox(height: 12),
      AsyncView(value: value, onRetry: () => ref.invalidate(myComplaintsProvider), loadingMessage: 'Loading complaints...',
        data: (all) {
          final q = _search.trim().toLowerCase();
          final items = all.where((c) => (_status == 'ALL' || (_status == 'OPEN' ? c.isOpen : c.status == _status)) &&
            (q.isEmpty || '${c.subject} ${c.description} ${c.complaintType}'.toLowerCase().contains(q))).toList();
          return items.isEmpty ? const EmptyView(icon: Icons.inbox_outlined, message: 'No complaints match these filters.')
            : Column(children: [for (final c in items) _ComplaintCard(complaint: c,
              onReply: () => _reply(context, c), onSolve: () => _solve(context, c), onDelete: () => _delete(context, c))]);
        }),
    ])))), floatingActionButton: FloatingActionButton.extended(onPressed: () => _showCreate(context),
      icon: const Icon(Icons.add), label: const Text('File complaint')));
  }

  Future<void> _showCreate(BuildContext context) async {
    final made = await showModalBottomSheet<bool>(context: context, isScrollControlled: true, useSafeArea: true,
      builder: (_) => const _CreateComplaintSheet());
    if (made == true) ref.invalidate(myComplaintsProvider);
  }

  Future<void> _reply(BuildContext context, Complaint c) async {
    final sent = await showReplySheet(context, title: 'Reply to administrator', subtitle: c.subject,
      requiredMessage: 'A reply is required.', onSubmit: (text, file) => ref.read(complaintsRepositoryProvider).reply(c.id, text, file));
    if (sent) ref.invalidate(myComplaintsProvider);
  }

  Future<void> _solve(BuildContext context, Complaint c) async {
    await showReplySheet(context, title: 'Mark complaint solved', subtitle: 'This permanently closes the complaint.',
      label: 'Resolution note (optional)', submitLabel: 'Mark solved', minLength: 0, allowAttachment: false,
      onSubmit: (text, _) async { await ref.read(complaintsRepositoryProvider).solve(c.id, text); ref.invalidate(myComplaintsProvider); });
  }

  Future<void> _delete(BuildContext context, Complaint c) async {
    final yes = await showDialog<bool>(context: context, builder: (_) => AlertDialog(title: const Text('Delete complaint?'),
      content: Text('Delete “${c.subject}” and its reply history?'), actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Keep')),
        FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Delete')),
      ]));
    if (yes != true) return;
    try { await ref.read(complaintsRepositoryProvider).delete(c.id); ref.invalidate(myComplaintsProvider); }
    catch (e) { if (ApiError.from(e).isConflict) ref.invalidate(myComplaintsProvider); if (context.mounted) showSnack(context, ApiError.from(e).message, type: SnackType.error); }
  }
}

class _ComplaintCard extends StatelessWidget {
  const _ComplaintCard({required this.complaint, required this.onReply, required this.onSolve, required this.onDelete});
  final Complaint complaint;
  final VoidCallback onReply, onSolve, onDelete;
  @override
  Widget build(BuildContext context) {
    final canReply = complaint.isOpen && complaint.canCreatorReply;
    return Card(child: ExpansionTile(title: Text(complaint.subject, style: const TextStyle(fontWeight: FontWeight.w700)),
      subtitle: Wrap(spacing: 6, children: [StatusBadge(complaint.status), StatusBadge(complaint.complaintType)]),
      childrenPadding: const EdgeInsets.fromLTRB(16, 0, 16, 16), children: [
        Align(alignment: Alignment.centerLeft, child: Text(complaint.targetLabel)), const SizedBox(height: 10),
        ThreadView(messages: complaint.thread()), const SizedBox(height: 10),
        if (complaint.isOpen && complaint.awaitingAdminReply) const InfoBanner('Waiting for the administrator.', color: Colors.blue),
        Wrap(spacing: 8, children: [
          if (canReply) FilledButton.icon(onPressed: onReply, icon: const Icon(Icons.reply), label: const Text('Reply')),
          if (complaint.isOpen) OutlinedButton(onPressed: onSolve, child: const Text('Mark solved')),
          TextButton(onPressed: onDelete, child: const Text('Delete')),
        ]),
      ]));
  }
}

class _CreateComplaintSheet extends ConsumerStatefulWidget {
  const _CreateComplaintSheet();
  @override
  ConsumerState<_CreateComplaintSheet> createState() => _CreateComplaintSheetState();
}

class _CreateComplaintSheetState extends ConsumerState<_CreateComplaintSheet> {
  final _form = GlobalKey<FormState>();
  final _subject = TextEditingController();
  final _description = TextEditingController();
  final _userSearch = TextEditingController();
  late String _type;
  String _targetKind = 'NONE';
  HospitalChoice? _hospital;
  ComplaintUserChoice? _targetUser;
  bool _busy = false;
  String? _error;
  String _key = newIdempotencyKey();
  List<String> get _categories => ref.read(authControllerProvider).user?.hasRole(Roles.hospitalStaff) == true
      ? const ['Donor Misconduct', 'Fake Blood Request', 'Policy Violation', 'Account Issue', 'Technical Issue', 'Other']
      : const ['Donation Process', 'Blood Request', 'Hospital Service', 'Account Issue', 'Technical Issue', 'Other'];
  @override
  void initState() { super.initState(); _type = _categories.first; }
  @override
  void dispose() { _subject.dispose(); _description.dispose(); _userSearch.dispose(); super.dispose(); }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    if (_targetKind == 'USER' && _userSearch.text.trim().isNotEmpty && _targetUser == null) {
      setState(() => _error = 'Choose a registered user from the results, or clear the target search.');
      return;
    }
    setState(() { _busy = true; _error = null; });
    try {
      final target = _hospital?.name ?? _targetUser?.name;
      await ref.read(complaintsRepositoryProvider).create(type: _type,
        subject: target == null ? _subject.text.trim() : '[Target: ${_targetKind == 'HOSPITAL' ? 'Registered Hospital' : 'Donor / Patient'} - $target] ${_subject.text.trim()}',
        description: _description.text.trim(), hospitalId: _hospital?.id, targetUserId: _targetUser?.id, idempotencyKey: _key);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      final api = ApiError.from(e); if (api.isConflict) _key = newIdempotencyKey();
      if (mounted) setState(() => _error = api.message);
    } finally { if (mounted) setState(() => _busy = false); }
  }

  @override
  Widget build(BuildContext context) {
    final hospitals = ref.watch(complaintHospitalsProvider).value ?? const <HospitalChoice>[];
    final ownId = ref.watch(authControllerProvider).user?.userId;
    final users = (ref.watch(complaintUsersProvider(_userSearch.text)).value ?? const <ComplaintUserChoice>[])
      .where((u) => u.id != ownId && u.extraInfo != 'Admin' && u.status != 'Permanently Blocked').toList();
    return Scaffold(appBar: AppBar(title: const Text('File a complaint'), actions: [IconButton(onPressed: () => Navigator.pop(context), icon: const Icon(Icons.close))]),
      body: Form(key: _form, child: ListView(padding: const EdgeInsets.all(16), children: [
        DropdownButtonFormField<String>(initialValue: _type, decoration: const InputDecoration(labelText: 'Complaint category'),
          items: [for (final c in _categories) DropdownMenuItem(value: c, child: Text(c))], onChanged: _busy ? null : (v) => setState(() => _type = v!)),
        const SizedBox(height: 12),
        SegmentedButton<String>(segments: const [ButtonSegment(value: 'NONE', label: Text('No target')),
          ButtonSegment(value: 'HOSPITAL', label: Text('Hospital')), ButtonSegment(value: 'USER', label: Text('User'))],
          selected: {_targetKind}, onSelectionChanged: (v) => setState(() { _targetKind = v.first; _hospital = null; _targetUser = null; })),
        const SizedBox(height: 12),
        if (_targetKind == 'HOSPITAL') DropdownButtonFormField<HospitalChoice>(initialValue: _hospital,
          decoration: const InputDecoration(labelText: 'Registered hospital (optional)'),
          items: [for (final h in hospitals) DropdownMenuItem(value: h, child: Text(h.name))], onChanged: (v) => setState(() => _hospital = v)),
        if (_targetKind == 'USER') ...[
          TextField(controller: _userSearch, decoration: const InputDecoration(labelText: 'Search registered users'),
            onChanged: (_) => setState(() { _targetUser = null; })),
          if (users.isNotEmpty) DropdownButtonFormField<ComplaintUserChoice>(initialValue: _targetUser,
            decoration: const InputDecoration(labelText: 'Choose a result'), items: [for (final u in users) DropdownMenuItem(value: u, child: Text(u.name))],
            onChanged: (v) => setState(() => _targetUser = v)),
        ],
        const SizedBox(height: 12),
        TextFormField(controller: _subject, maxLength: 200, decoration: const InputDecoration(labelText: 'Subject'),
          validator: (v) => (v?.trim().length ?? 0) < 5 ? 'Enter at least 5 characters.' : null),
        TextFormField(controller: _description, maxLength: 2000, minLines: 4, maxLines: 8, decoration: const InputDecoration(labelText: 'Description'),
          validator: (v) => (v?.trim().length ?? 0) < 10 ? 'Enter at least 10 characters.' : null),
        if (_error != null) Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
        const SizedBox(height: 12), FilledButton(onPressed: _busy ? null : _submit, child: Text(_busy ? 'Submitting...' : 'Submit complaint')),
      ])));
  }
}
