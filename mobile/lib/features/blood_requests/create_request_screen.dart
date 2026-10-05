import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/api/idempotency.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import 'blood_request_models.dart';
import 'blood_request_repository.dart';
import 'donor_home_screen.dart';

class CreateRequestScreen extends ConsumerStatefulWidget {
  const CreateRequestScreen({super.key, this.initialPriority, this.initialBloodGroup});
  final String? initialPriority;
  final String? initialBloodGroup;

  @override
  ConsumerState<CreateRequestScreen> createState() => _CreateRequestScreenState();
}

class _CreateRequestScreenState extends ConsumerState<CreateRequestScreen> {
  final _formKey = GlobalKey<FormState>();
  final _units = TextEditingController(text: '2');
  final _reason = TextEditingController();
  late String _group;
  late String _priority;
  String? _hospitalId;
  String? _doctorId;
  String _submitKey = newIdempotencyKey();
  bool _saving = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _group = AppConstants.bloodGroups.contains(widget.initialBloodGroup) ? widget.initialBloodGroup! : 'O+';
    _priority = const {'Normal', 'High', 'Critical'}.contains(widget.initialPriority) ? widget.initialPriority! : 'High';
  }

  @override
  void dispose() {
    _units.dispose();
    _reason.dispose();
    super.dispose();
  }

  Future<void> _submit(bool hospitalStaff) async {
    if (!_formKey.currentState!.validate()) return;
    if (_hospitalId == null) {
      setState(() => _error = 'Please select a mandatory target hospital facility before submitting.');
      return;
    }
    if (hospitalStaff && _doctorId == null) {
      setState(() => _error = 'Select the doctor who will approve this request.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(bloodRequestRepositoryProvider).create(
            hospitalId: _hospitalId!,
            bloodGroup: _group,
            units: int.parse(_units.text),
            priority: _priority,
            reason: _reason.text.trim(),
            doctorId: hospitalStaff ? _doctorId : null,
            idempotencyKey: _submitKey,
          );
      if (!mounted) return;
      showSnack(context, hospitalStaff ? 'Sent to the selected doctor for approval.' : 'Submitted for hospital verification.',
          title: 'Blood request created', type: SnackType.success);
      _reason.clear();
      _units.text = '2';
      setState(() {
        _group = 'O+';
        _priority = 'High';
        _doctorId = null;
        if (!hospitalStaff) _hospitalId = null;
        _submitKey = newIdempotencyKey();
      });
      _reload();
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      setState(() => _error = api.message);
      if (api.isConflict) {
        _submitKey = newIdempotencyKey();
        _reload();
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  void _reload() {
    ref.invalidate(myBloodRequestsProvider);
    ref.invalidate(donorHomeProvider);
  }

  @override
  Widget build(BuildContext context) {
    final user = ref.watch(authControllerProvider).user;
    final hospitalStaff = user?.hasRole(Roles.hospitalStaff) == true;
    final hospitalsValue = ref.watch(hospitalChoicesProvider);
    final doctorsValue = hospitalStaff ? ref.watch(doctorChoicesProvider) : null;
    return Scaffold(
      appBar: AppBar(title: const Text('Create blood request')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(hospitalChoicesProvider);
          if (hospitalStaff) ref.invalidate(doctorChoicesProvider);
          _reload();
          await ref.read(myBloodRequestsProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          maxWidth: 900,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                AsyncView(
                  value: hospitalsValue,
                  onRetry: () => ref.invalidate(hospitalChoicesProvider),
                  loadingMessage: 'Loading active hospitals...',
                  data: (hospitals) {
                    if (hospitals.isEmpty) {
                      return const EmptyView(icon: Icons.local_hospital_outlined, message: 'No active hospitals are available.');
                    }
                    if (hospitalStaff && _hospitalId == null) {
                      final email = user?.email.toLowerCase();
                      for (final h in hospitals) {
                        if (h.email.toLowerCase() == email) { _hospitalId = h.id; break; }
                      }
                    }
                    return _form(hospitalStaff, hospitals, doctorsValue);
                  },
                ),
                const SizedBox(height: 20),
                const MyRequestsSection(),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _form(bool hospitalStaff, List<HospitalChoice> hospitals, AsyncValue<List<DoctorChoice>>? doctorsValue) {
    return SectionCard(
      title: 'New blood request',
      icon: Icons.add_circle_outline,
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            DropdownButtonFormField<String>(
              initialValue: _hospitalId,
              decoration: InputDecoration(labelText: hospitalStaff ? 'Your hospital' : 'Hospital *'),
              items: [for (final h in hospitals) DropdownMenuItem(value: h.id, child: Text(h.name))],
              onChanged: hospitalStaff || _saving ? null : (value) => setState(() => _hospitalId = value),
              validator: (value) => value == null ? 'Select a hospital.' : null,
            ),
            if (hospitalStaff) ...[
              const SizedBox(height: 14),
              if (doctorsValue == null)
                const SizedBox.shrink()
              else
                doctorsValue.when(
                  loading: () => const LinearProgressIndicator(),
                  error: (e, _) => ErrorView(error: e, onRetry: () => ref.invalidate(doctorChoicesProvider)),
                  data: (doctors) => DropdownButtonFormField<String>(
                    initialValue: _doctorId,
                    decoration: const InputDecoration(labelText: 'Approving doctor *'),
                    items: [
                      for (final d in doctors)
                        DropdownMenuItem(value: d.id, child: Text('${d.name}${d.specialization.isEmpty ? '' : ' - ${d.specialization}'}')),
                    ],
                    onChanged: _saving ? null : (value) => setState(() => _doctorId = value),
                    validator: (value) => value == null ? 'Select the approving doctor.' : null,
                  ),
                ),
            ],
            const SizedBox(height: 14),
            Row(children: [
              Expanded(
                child: DropdownButtonFormField<String>(
                  initialValue: _group,
                  decoration: const InputDecoration(labelText: 'Blood group'),
                  items: [for (final g in AppConstants.bloodGroups) DropdownMenuItem(value: g, child: Text(g))],
                  onChanged: _saving ? null : (value) => setState(() => _group = value ?? _group),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: TextFormField(
                  controller: _units,
                  keyboardType: TextInputType.number,
                  inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                  decoration: const InputDecoration(labelText: 'Units (1-10)'),
                  validator: (value) => Validators.intRange(value, 'Units', min: 1, max: 10),
                ),
              ),
            ]),
            const SizedBox(height: 14),
            DropdownButtonFormField<String>(
              initialValue: _priority,
              decoration: const InputDecoration(labelText: 'Priority'),
              items: const [
                DropdownMenuItem(value: 'Normal', child: Text('Normal (scheduled procedure)')),
                DropdownMenuItem(value: 'High', child: Text('High (urgent transfusion)')),
                DropdownMenuItem(value: 'Critical', child: Text('Critical (immediately needed)')),
              ],
              onChanged: _saving ? null : (value) => setState(() => _priority = value ?? _priority),
            ),
            const SizedBox(height: 14),
            TextFormField(
              controller: _reason,
              maxLines: 4,
              maxLength: 2000,
              decoration: const InputDecoration(labelText: 'Clinical reason / diagnosis'),
              validator: (value) => Validators.required(value, 'Reason'),
            ),
            if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
            BusyButton(label: 'Submit blood request', icon: Icons.send, busy: _saving, onPressed: () => _submit(hospitalStaff)),
          ],
        ),
      ),
    );
  }
}

class MyRequestsSection extends ConsumerStatefulWidget {
  const MyRequestsSection({super.key});

  @override
  ConsumerState<MyRequestsSection> createState() => _MyRequestsSectionState();
}

class _MyRequestsSectionState extends ConsumerState<MyRequestsSection> {
  String _status = 'All';
  String? _busy;

  void _reload() {
    ref.invalidate(myBloodRequestsProvider);
    ref.invalidate(donorHomeProvider);
  }

  Future<void> _action(String id, String success, Future<void> Function() action) async {
    setState(() => _busy = id);
    try {
      await action();
      if (mounted) showSnack(context, success, type: SnackType.success);
      _reload();
    } catch (e) {
      final api = ApiError.from(e);
      if (mounted) showErrorSnack(context, api);
      if (api.isConflict) _reload();
    } finally {
      if (mounted) setState(() => _busy = null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(myBloodRequestsProvider);
    final user = ref.watch(authControllerProvider).user;
    return SectionCard(
      title: 'My requests',
      icon: Icons.assignment_outlined,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          FilterChips<String>(
            options: const [
              ('All', 'All'), ('Pending', 'Pending'), ('Verified', 'Verified'), ('Approved', 'Approved'),
              ('Completed', 'Completed'), ('Rejected', 'Rejected'), ('Cancelled', 'Cancelled'),
            ],
            selected: _status,
            onSelected: (value) => setState(() => _status = value),
          ),
          const SizedBox(height: 12),
          AsyncView(
            value: value,
            onRetry: _reload,
            loadingMessage: 'Loading your requests...',
            data: (all) {
              final list = all.where((r) => _status == 'All' || r.status == _status).toList();
              if (list.isEmpty) return const EmptyView(icon: Icons.assignment_outlined, message: 'No requests match this status.');
              return Column(children: [for (final r in list) _requestCard(r, user?.userId)]);
            },
          ),
        ],
      ),
    );
  }

  Widget _requestCard(BloodRequest r, String? userId) {
    final patient = ref.read(authControllerProvider).user?.hasRole(Roles.user) == true;
    final own = r.patientUserId == userId;
    final canEdit = patient && own && !r.isSuspended && r.status == 'Pending' && (r.expiryDate?.isAfter(DateTime.now()) ?? false);
    final canDelete = own;
    final canCancel = own && !r.isSuspended && r.hasScreenedDonors && r.status != 'Cancelled' && r.status != 'Completed' && r.fulfilledUnits == 0;
    return Card(
      margin: const EdgeInsets.only(bottom: 10),
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            BloodGroupBadge(r.bloodGroup), const SizedBox(width: 8),
            Expanded(child: Text(r.hospitalName, style: const TextStyle(fontWeight: FontWeight.w700))),
            StatusBadge(r.status, variant: requestStatusVariant(r.status)),
          ]),
          const SizedBox(height: 6),
          Text('${r.fulfilledUnits}/${r.unitsRequired} donated${r.reservedUnits > 0 ? ', ${r.reservedUnits} reserved' : ''}'),
          Text('${r.priority} · ${r.reason}', style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
          if (r.status == 'Rejected' && r.rejectionReason?.isNotEmpty == true)
            Text('Reason: ${r.rejectionReason}', style: const TextStyle(fontSize: 12, color: AppColors.rose600)),
          if (r.isSuspended) ...[const SizedBox(height: 5), SuspendedBadge(reason: r.suspensionReason)],
          if (canEdit || canCancel || canDelete) ...[
            const SizedBox(height: 8),
            Wrap(spacing: 8, runSpacing: 8, children: [
              if (canEdit)
                OutlinedButton.icon(onPressed: _busy == null ? () => _edit(r) : null, icon: const Icon(Icons.edit, size: 17), label: const Text('Edit')),
              if (canCancel)
                OutlinedButton.icon(onPressed: _busy == null ? () => _cancel(r) : null, icon: const Icon(Icons.block, size: 17), label: const Text('Cancel')),
              if (canDelete)
                OutlinedButton.icon(onPressed: _busy == null ? () => _delete(r) : null, icon: const Icon(Icons.delete_outline, size: 17), label: const Text('Delete')),
            ]),
          ],
        ]),
      ),
    );
  }

  Future<void> _edit(BloodRequest request) async {
    final changed = await showModalBottomSheet<({String group, int units})>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _EditRequestSheet(request: request),
    );
    if (changed != null) {
      await _action(request.id, 'Request updated.', () => ref.read(bloodRequestRepositoryProvider).update(
            request.id, bloodGroup: changed.group, units: changed.units));
    }
  }

  Future<void> _cancel(BloodRequest request) async {
    final ok = await confirmDialog(context,
        title: 'Cancel blood request',
        message: 'Cancel this request? Active donors will be released and screening history will be kept.',
        confirmLabel: 'Cancel request', destructive: true);
    if (ok) await _action(request.id, 'Request cancelled.', () => ref.read(bloodRequestRepositoryProvider).cancel(request.id));
  }

  Future<void> _delete(BloodRequest request) async {
    final ok = await confirmDialog(context,
        title: 'Delete blood request',
        message: 'Delete this request permanently from active lists? Donors are released and screening history is kept. This cannot be undone.',
        confirmLabel: 'Delete request', destructive: true);
    if (ok) await _action(request.id, 'Request deleted.', () => ref.read(bloodRequestRepositoryProvider).delete(request.id));
  }
}

class _EditRequestSheet extends StatefulWidget {
  const _EditRequestSheet({required this.request});
  final BloodRequest request;

  @override
  State<_EditRequestSheet> createState() => _EditRequestSheetState();
}

class _EditRequestSheetState extends State<_EditRequestSheet> {
  late String _group = widget.request.bloodGroup;
  late final TextEditingController _units = TextEditingController(text: '${widget.request.unitsRequired}');
  final _key = GlobalKey<FormState>();

  @override
  void dispose() {
    _units.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
        child: Form(
          key: _key,
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            const Text('Edit blood request', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
            const SizedBox(height: 14),
            DropdownButtonFormField<String>(
              initialValue: _group,
              decoration: const InputDecoration(labelText: 'Blood group'),
              items: [for (final g in AppConstants.bloodGroups) DropdownMenuItem(value: g, child: Text(g))],
              onChanged: (value) => setState(() => _group = value ?? _group),
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _units,
              keyboardType: TextInputType.number,
              inputFormatters: [FilteringTextInputFormatter.digitsOnly],
              decoration: const InputDecoration(labelText: 'Units (1-10)'),
              validator: (value) => Validators.intRange(value, 'Units', min: 1, max: 10),
            ),
            const SizedBox(height: 14),
            FilledButton(
              onPressed: () {
                if (_key.currentState!.validate()) Navigator.pop(context, (group: _group, units: int.parse(_units.text)));
              },
              child: const Text('Save changes'),
            ),
          ]),
        ),
      );
}
