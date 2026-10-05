import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/api/idempotency.dart';
import '../../core/config/constants.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import '../profile/profile_repository.dart';
import 'emergency_repository.dart';

final allEmergenciesProvider = FutureProvider.autoDispose<List<EmergencyRequest>>((ref) async {
  final list = await ref.watch(emergencyRepositoryProvider).all();
  list.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
  return list;
});

enum EmergencyFilter { all, critical, mine }

/// What the raising hospital may still do (the same status rules as EmergencyRequestService).
({bool approve, bool reject, bool complete}) emergencyActions(EmergencyRequest e) => (
      approve: e.status == 'Pending',
      reject: e.status != 'Completed' && e.status != 'Rejected',
      complete: e.status != 'Completed' && e.status != 'Rejected',
    );

BadgeVariant emergencyStatusVariant(String status) => switch (status) {
      'Pending' => BadgeVariant.warning,
      'Approved' => BadgeVariant.info,
      'Completed' => BadgeVariant.success,
      'Rejected' => BadgeVariant.danger,
      _ => BadgeVariant.neutral,
    };

/// Hospital-to-hospital emergencies: raise one (other approved hospitals holding the exact blood group are alerted
/// and can offer a transfer), list all / critical / mine, and approve, reject or complete your own.
class EmergenciesScreen extends ConsumerStatefulWidget {
  const EmergenciesScreen({super.key});

  @override
  ConsumerState<EmergenciesScreen> createState() => _EmergenciesScreenState();
}

class _EmergenciesScreenState extends ConsumerState<EmergenciesScreen> {
  EmergencyFilter _filter = EmergencyFilter.all;
  bool _acting = false;

  void _reload() {
    ref.invalidate(allEmergenciesProvider);
    ref.invalidate(criticalEmergenciesProvider);
  }

  Future<void> _act(String label, Future<void> Function() action) async {
    if (_acting) return;
    setState(() => _acting = true);
    try {
      await action();
      if (mounted) showSnack(context, 'Emergency $label.', type: SnackType.success);
      _reload();
    } catch (e) {
      final api = ApiError.from(e);
      if (mounted) showSnack(context, api.message, type: SnackType.error, title: 'Action failed');
      if (api.isConflict) _reload();
    } finally {
      if (mounted) setState(() => _acting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(allEmergenciesProvider);
    final me = ref.watch(myHospitalProvider).value?.hospitalId;
    final repo = ref.read(emergencyRepositoryProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Emergencies')),
      floatingActionButton: FloatingActionButton.extended(
        key: const Key('fab-raise-emergency'),
        backgroundColor: AppColors.red600,
        foregroundColor: Colors.white,
        onPressed: () async {
          final raised = await showModalBottomSheet<bool>(
            context: context,
            isScrollControlled: true,
            builder: (_) => const RaiseEmergencySheet(),
          );
          if (raised == true) _reload();
        },
        icon: const Icon(Icons.bolt),
        label: const Text('Raise emergency'),
      ),
      body: RefreshableScroll(
        onRefresh: () async {
          _reload();
          await ref.read(allEmergenciesProvider.future).catchError((_) => <EmergencyRequest>[]);
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Card(
                  child: ListTile(
                    leading: const Icon(Icons.bloodtype, color: AppColors.red600),
                    title: const Text('Need donors?'),
                    subtitle: const Text('Create a Critical blood request. It follows the normal doctor approval, then alerts eligible donors.'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push('${AppRoutes.createRequest}?priority=Critical'),
                  ),
                ),
                const SizedBox(height: 12),
                FilterChips<EmergencyFilter>(
                  options: const [
                    (EmergencyFilter.all, 'All'),
                    (EmergencyFilter.critical, 'Critical'),
                    (EmergencyFilter.mine, 'Raised by us'),
                  ],
                  selected: _filter,
                  onSelected: (f) => setState(() => _filter = f),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: _reload,
                  data: (all) {
                    final list = all
                        .where((e) => switch (_filter) {
                              EmergencyFilter.all => true,
                              EmergencyFilter.critical => e.isCritical,
                              EmergencyFilter.mine => e.hospitalId == me,
                            })
                        .toList();
                    if (list.isEmpty) return const EmptyView(icon: Icons.emergency_outlined, message: 'No emergencies here.');
                    return Column(
                      children: [
                        for (final e in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: _EmergencyCard(
                              e: e,
                              mine: e.hospitalId == me,
                              busy: _acting,
                              onApprove: () => _act('approved', () => repo.approve(e.id)),
                              onReject: () async {
                                if (await confirmDialog(context,
                                    title: 'Reject emergency', message: 'Reject this emergency?', confirmLabel: 'Reject', destructive: true)) {
                                  await _act('rejected', () => repo.reject(e.id));
                                }
                              },
                              onComplete: () async {
                                if (await confirmDialog(context,
                                    title: 'Complete emergency', message: 'Mark this emergency as completed?', confirmLabel: 'Complete')) {
                                  await _act('completed', () => repo.complete(e.id));
                                }
                              },
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

class _EmergencyCard extends StatelessWidget {
  const _EmergencyCard({
    required this.e,
    required this.mine,
    required this.busy,
    required this.onApprove,
    required this.onReject,
    required this.onComplete,
  });

  final EmergencyRequest e;
  final bool mine;
  final bool busy;
  final VoidCallback onApprove;
  final VoidCallback onReject;
  final VoidCallback onComplete;

  @override
  Widget build(BuildContext context) {
    final can = emergencyActions(e);
    final compact = OutlinedButton.styleFrom(minimumSize: const Size(0, 40));
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                BloodGroupBadge(e.bloodGroup),
                const SizedBox(width: 8),
                Expanded(
                  child: Text('${e.unitsRequired} unit(s) · ${mine ? 'Your hospital' : e.hospitalName}',
                      style: const TextStyle(fontWeight: FontWeight.w700)),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Wrap(spacing: 6, children: [
              StatusBadge(e.priority, variant: e.isCritical ? BadgeVariant.danger : BadgeVariant.warning),
              StatusBadge(e.status, variant: emergencyStatusVariant(e.status)),
            ]),
            if (e.reason.isNotEmpty) ...[const SizedBox(height: 6), Text(e.reason)],
            const SizedBox(height: 4),
            Text(Fmt.sriLankaDateTime(e.createdAt), style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
            if (mine && (can.approve || can.reject || can.complete)) ...[
              const SizedBox(height: 10),
              Wrap(
                spacing: 8,
                runSpacing: 8,
                children: [
                  if (can.approve)
                    OutlinedButton.icon(style: compact, onPressed: busy ? null : onApprove, icon: const Icon(Icons.check, size: 18), label: const Text('Approve')),
                  if (can.complete)
                    OutlinedButton.icon(style: compact, onPressed: busy ? null : onComplete, icon: const Icon(Icons.done_all, size: 18), label: const Text('Complete')),
                  if (can.reject)
                    OutlinedButton.icon(style: compact, onPressed: busy ? null : onReject, icon: const Icon(Icons.close, size: 18), label: const Text('Reject')),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// The web Emergency Center form: blood group, units (min 1), Critical / High, justification.
class RaiseEmergencySheet extends ConsumerStatefulWidget {
  const RaiseEmergencySheet({super.key});

  @override
  ConsumerState<RaiseEmergencySheet> createState() => _RaiseEmergencySheetState();
}

class _RaiseEmergencySheetState extends ConsumerState<RaiseEmergencySheet> {
  final _formKey = GlobalKey<FormState>();
  String _group = 'O-';
  String _priority = 'CRITICAL';
  final _units = TextEditingController(text: '5');
  final _reason = TextEditingController(text: 'Emergency ICU Trauma Transfusion');
  // One key per emergency form: a double submit or retry never raises the same emergency twice
  String _key = newIdempotencyKey();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _units.dispose();
    _reason.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(emergencyRepositoryProvider).create(
            bloodGroup: _group,
            unitsRequired: int.parse(_units.text.trim()),
            priority: _priority,
            reason: _reason.text.trim(),
            idempotencyKey: _key,
          );
      if (!mounted) return;
      showSnack(context, 'Approved hospitals holding this exact blood group have been alerted and can send you a transfer offer.',
          type: SnackType.success, title: 'Emergency raised');
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      setState(() => _error = api.message);
      // 409: this form was already submitted; the next submit is a new emergency
      if (api.isConflict) _key = newIdempotencyKey();
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
        child: SingleChildScrollView(
          child: Form(
            key: _formKey,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text('Raise an emergency', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
                const SizedBox(height: 6),
                const Text('Other approved hospitals holding the same blood group are alerted and can offer a transfer.',
                    style: TextStyle(fontSize: 12)),
                const SizedBox(height: 14),
                Row(
                  children: [
                    Expanded(
                      child: DropdownButtonFormField<String>(
                        initialValue: _group,
                        decoration: const InputDecoration(labelText: 'Blood group'),
                        items: [for (final g in AppConstants.bloodGroups) DropdownMenuItem(value: g, child: Text(g))],
                        onChanged: (v) => setState(() => _group = v ?? _group),
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: TextFormField(
                        key: const Key('emergency-units'),
                        controller: _units,
                        keyboardType: TextInputType.number,
                        inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                        decoration: const InputDecoration(labelText: 'Units'),
                        validator: (v) => Validators.intRange(v, 'Units', min: 1),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                DropdownButtonFormField<String>(
                  initialValue: _priority,
                  decoration: const InputDecoration(labelText: 'Priority'),
                  items: const [
                    DropdownMenuItem(value: 'CRITICAL', child: Text('Critical (immediate life safety)')),
                    DropdownMenuItem(value: 'HIGH', child: Text('High (urgent operating room)')),
                  ],
                  onChanged: (v) => setState(() => _priority = v ?? _priority),
                ),
                const SizedBox(height: 14),
                TextFormField(
                  controller: _reason,
                  maxLines: 3,
                  decoration: const InputDecoration(labelText: 'Justification and notes'),
                  validator: (v) => Validators.required(v, 'Justification'),
                ),
                const SizedBox(height: 16),
                if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
                BusyButton(label: 'Alert hospitals holding this blood group', icon: Icons.bolt, busy: _saving, onPressed: _submit),
              ],
            ),
          ),
        ),
      );
}
