import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/config/constants.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import '../auth/auth_widgets.dart';
import 'inventory_models.dart';
import 'inventory_repository.dart';

/// Add a blood group category (defaults 5 / 100, like the web app) or edit an existing group's thresholds.
/// Returns true when saved.
Future<bool> showGroupFormSheet(BuildContext context, {InventoryItem? edit, List<String> existingGroups = const []}) async {
  final saved = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    builder: (_) => GroupFormSheet(edit: edit, existingGroups: existingGroups),
  );
  return saved ?? false;
}

class GroupFormSheet extends ConsumerStatefulWidget {
  const GroupFormSheet({super.key, this.edit, this.existingGroups = const []});

  final InventoryItem? edit;
  final List<String> existingGroups;

  @override
  ConsumerState<GroupFormSheet> createState() => _GroupFormSheetState();
}

class _GroupFormSheetState extends ConsumerState<GroupFormSheet> {
  final _formKey = GlobalKey<FormState>();
  late String _group = widget.edit?.bloodGroup ??
      AppConstants.bloodGroups.firstWhere((g) => !widget.existingGroups.contains(g), orElse: () => 'O+');
  late final _threshold = TextEditingController(text: '${widget.edit?.minimumThreshold ?? 5}');
  late final _capacity = TextEditingController(text: '${widget.edit?.maximumCapacity ?? 100}');
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _threshold.dispose();
    _capacity.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _saving = true;
      _error = null;
    });
    final repo = ref.read(inventoryRepositoryProvider);
    final threshold = int.parse(_threshold.text.trim());
    final capacity = int.parse(_capacity.text.trim());
    try {
      if (widget.edit == null) {
        await repo.createGroup(bloodGroup: _group, minimumThreshold: threshold, maximumCapacity: capacity);
      } else {
        await repo.update(widget.edit!.inventoryId, minimumThreshold: threshold, maximumCapacity: capacity);
      }
      if (!mounted) return;
      showSnack(
        context,
        widget.edit == null ? '$_group thresholds saved.' : '${widget.edit!.bloodGroup} thresholds updated.',
        type: SnackType.success,
        title: widget.edit == null ? 'Blood group added' : 'Thresholds saved',
      );
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      if (api.isConflict) {
        // Changed elsewhere at the same moment: show the current stock
        showSnack(context, api.message, type: SnackType.error, title: 'Not saved');
        Navigator.of(context).pop(true);
      } else {
        setState(() => _error = api.message);
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final editing = widget.edit != null;
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
      child: Form(
        key: _formKey,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(editing ? '${widget.edit!.bloodGroup}: thresholds' : 'Add blood group',
                style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
            const SizedBox(height: 16),
            if (!editing) ...[
              DropdownButtonFormField<String>(
                initialValue: _group,
                decoration: const InputDecoration(labelText: 'Blood group'),
                items: [for (final g in AppConstants.bloodGroups) DropdownMenuItem(value: g, child: Text(g))],
                onChanged: (v) => setState(() => _group = v ?? _group),
              ),
              const SizedBox(height: 14),
            ],
            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    key: const Key('group-threshold'),
                    controller: _threshold,
                    keyboardType: TextInputType.number,
                    inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                    decoration: const InputDecoration(labelText: 'Minimum threshold'),
                    validator: (v) => Validators.intRange(v, 'Minimum threshold', min: 0),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextFormField(
                    key: const Key('group-capacity'),
                    controller: _capacity,
                    keyboardType: TextInputType.number,
                    inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                    decoration: const InputDecoration(labelText: 'Capacity'),
                    validator: (v) => Validators.intRange(v, 'Capacity', min: 1),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            const Text('A blood group is low when its available units are below the minimum threshold.',
                style: TextStyle(fontSize: 12)),
            const SizedBox(height: 16),
            if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
            BusyButton(label: 'Save', busy: _saving, onPressed: _save),
          ],
        ),
      ),
    );
  }
}
