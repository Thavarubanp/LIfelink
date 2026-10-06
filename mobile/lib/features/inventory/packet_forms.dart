import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/api/idempotency.dart';
import '../../core/config/constants.dart';
import '../../core/utils/format.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import '../auth/auth_widgets.dart';
import '../profile/profile_repository.dart';
import 'inventory_models.dart';
import 'inventory_repository.dart';
import 'packet_picker.dart';
import 'packet_rules.dart';

Future<bool> _sheet(BuildContext context, Widget child) async =>
    await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => child,
    ) ??
    false;

/// "Add packets": blood group, number of packets (1–20) and the collected date from a date picker.
Future<bool> showAddPacketsSheet(BuildContext context) =>
    _sheet(context, const PacketFormSheet());

/// Edit an Available packet created by this hospital (blood group and collected date only).
Future<bool> showEditPacketSheet(BuildContext context, BloodPacket packet) =>
    _sheet(context, PacketFormSheet(edit: packet));

/// Issue selected packets of a group with a required reason.
Future<bool> showIssueSheet(
  BuildContext context,
  InventoryItem group,
  List<String> preselected,
) => _sheet(context, IssuePacketsSheet(group: group, preselected: preselected));

class _SheetFrame extends StatelessWidget {
  const _SheetFrame({required this.title, required this.children});

  final String title;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(
      20,
      0,
      20,
      MediaQuery.viewInsetsOf(context).bottom + 20,
    ),
    child: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            title,
            style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 16),
          ...children,
        ],
      ),
    ),
  );
}

class PacketFormSheet extends ConsumerStatefulWidget {
  const PacketFormSheet({super.key, this.edit, this.today});

  final BloodPacket? edit;

  /// Overrides "today" in tests.
  final DateTime? today;

  @override
  ConsumerState<PacketFormSheet> createState() => _PacketFormSheetState();
}

class _PacketFormSheetState extends ConsumerState<PacketFormSheet> {
  final _formKey = GlobalKey<FormState>();
  late final DateTime _today = widget.today ?? Fmt.sriLankaToday();
  late String _group = widget.edit?.bloodGroup ?? 'O+';
  late DateTime? _collected = _initialDate();

  DateTime _initialDate() {
    final c = widget.edit?.collectionDate;
    if (c == null) return _today;
    final sl = Fmt.toSriLanka(c);
    return DateTime(sl.year, sl.month, sl.day);
  }

  final _quantity = TextEditingController(text: '1');
  // One idempotency key per opened form: a double submit or retry never adds the packets twice
  final String _key = newIdempotencyKey();
  bool _saving = false;
  String? _error;

  bool get _editing => widget.edit != null;

  @override
  void dispose() {
    _quantity.dispose();
    super.dispose();
  }

  Future<void> _pickDate(int? shelfLife) async {
    final first = earliestCollectedDate(shelfLife, _today);
    final current =
        _collected == null ||
            _collected!.isBefore(first) ||
            _collected!.isAfter(_today)
        ? _today
        : _collected!;
    final picked = await showDatePicker(
      context: context,
      initialDate: current,
      firstDate: first,
      lastDate: _today,
      helpText: 'Collected date',
    );
    if (picked != null) setState(() => _collected = picked);
  }

  Future<void> _save(int? shelfLife) async {
    final dateError = collectedDateError(_collected, shelfLife, _today);
    final valid = _formKey.currentState!.validate();
    if (dateError != null || !valid) {
      setState(() => _error = dateError);
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    final repo = ref.read(inventoryRepositoryProvider);
    final date = Fmt.apiDate(_collected!);
    try {
      if (_editing) {
        await repo.updatePacket(
          widget.edit!.packetId,
          bloodGroup: _group,
          collectionDate: date,
        );
        if (mounted) {
          showSnack(
            context,
            '${widget.edit!.trackingNumber} saved.',
            type: SnackType.success,
            title: 'Packet updated',
          );
        }
      } else {
        final created = await repo.createPackets(
          bloodGroup: _group,
          collectionDate: date,
          quantity: int.parse(_quantity.text.trim()),
          idempotencyKey: _key,
        );
        if (mounted) {
          showSnack(
            context,
            '${created.length} $_group packet(s): ${created.map((p) => p.trackingNumber).join(', ')}',
            type: SnackType.success,
            title: 'Packets added',
          );
        }
      }
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      if (api.isConflict) {
        // Already submitted, or changed elsewhere at the same moment: show the current stock
        showSnack(
          context,
          api.message,
          type: SnackType.error,
          title: 'Not saved',
        );
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
    final hospital = ref.watch(myHospitalProvider).value;
    final shelfLife = hospital?.packetShelfLifeDays;
    return _SheetFrame(
      title: _editing
          ? 'Edit packet ${widget.edit!.trackingNumber}'
          : 'Add blood packets',
      children: [
        Form(
          key: _formKey,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              if (_editing) ...[
                const Text(
                  'Tracking number, created-by hospital and created date cannot be changed.',
                  style: TextStyle(fontSize: 12),
                ),
                const SizedBox(height: 12),
              ],
              DropdownButtonFormField<String>(
                key: const Key('packet-group'),
                initialValue: _group,
                decoration: const InputDecoration(labelText: 'Blood group'),
                items: [
                  for (final g in AppConstants.bloodGroups)
                    DropdownMenuItem(value: g, child: Text(g)),
                ],
                onChanged: (v) => setState(() => _group = v ?? _group),
              ),
              const SizedBox(height: 14),
              InkWell(
                key: const Key('packet-date'),
                borderRadius: BorderRadius.circular(12),
                onTap: () => _pickDate(shelfLife),
                child: InputDecorator(
                  decoration: const InputDecoration(
                    labelText: 'Collected date *',
                    suffixIcon: Icon(Icons.calendar_month),
                  ),
                  child: Text(
                    _collected == null
                        ? 'Choose a date'
                        : Fmt.apiDate(_collected!),
                  ),
                ),
              ),
              if (!_editing) ...[
                const SizedBox(height: 14),
                TextFormField(
                  key: const Key('packet-quantity'),
                  controller: _quantity,
                  keyboardType: TextInputType.number,
                  inputFormatters: [
                    FilteringTextInputFormatter.digitsOnly,
                    LengthLimitingTextInputFormatter(2),
                  ],
                  decoration: const InputDecoration(
                    labelText: 'Number of packets (1–$maxPacketsPerForm)',
                  ),
                  validator: (v) => Validators.intRange(
                    v,
                    'Number of packets',
                    min: 1,
                    max: maxPacketsPerForm,
                  ),
                ),
              ],
              const SizedBox(height: 10),
              Text(
                'Expiry is the collected date plus your shelf life (${shelfLife ?? '-'} days).'
                '${_editing ? '' : ' Each packet gets its own tracking number.'}',
                style: const TextStyle(fontSize: 12),
              ),
              const SizedBox(height: 16),
              if (_error != null) ...[
                FormErrorBox(_error!),
                const SizedBox(height: 12),
              ],
              BusyButton(
                key: const Key('packet-save'),
                label: _editing ? 'Save' : 'Add packets',
                busy: _saving,
                onPressed: () => _save(shelfLife),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class IssuePacketsSheet extends ConsumerStatefulWidget {
  const IssuePacketsSheet({
    super.key,
    required this.group,
    this.preselected = const [],
  });

  final InventoryItem group;
  final List<String> preselected;

  @override
  ConsumerState<IssuePacketsSheet> createState() => _IssuePacketsSheetState();
}

class _IssuePacketsSheetState extends ConsumerState<IssuePacketsSheet> {
  late List<String> _selected = List.of(widget.preselected);
  final _reason = TextEditingController();
  bool _saving = false;
  String? _error;

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final problem = _selected.isEmpty
        ? 'Select at least one packet to remove.'
        : _reason.text.trim().isEmpty
        ? 'A reason is required when issuing blood.'
        : null;
    if (problem != null) {
      setState(() => _error = problem);
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    final g = widget.group;
    try {
      await ref
          .read(inventoryRepositoryProvider)
          .update(
            g.inventoryId,
            minimumThreshold: g.minimumThreshold,
            maximumCapacity: g.maximumCapacity,
            issuePacketIds: _selected,
            auditNotes: _reason.text.trim(),
          );
      if (!mounted) return;
      showSnack(
        context,
        '${_selected.length} ${g.bloodGroup} packet(s) removed from available stock.',
        type: SnackType.success,
        title: 'Stock updated',
      );
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      if (api.isConflict) {
        showSnack(
          context,
          api.message,
          type: SnackType.error,
          title: 'Not saved',
        );
        Navigator.of(context).pop(true);
      } else {
        setState(() => _error = api.message);
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) => _SheetFrame(
    title: 'Remove ${widget.group.bloodGroup} blood',
    children: [
      const Text(
        'Choose the packets to remove from available stock. They remain in packet history with status Issued.',
        style: TextStyle(fontSize: 12),
      ),
      const SizedBox(height: 10),
      PacketPicker(
        bloodGroup: widget.group.bloodGroup,
        selected: _selected,
        onChanged: (ids) => setState(() => _selected = ids),
      ),
      const SizedBox(height: 12),
      TextField(
        controller: _reason,
        maxLength: 500,
        decoration: const InputDecoration(
          labelText: 'Reason',
          hintText: 'Enter why these packets are being removed',
        ),
      ),
      if (_error != null) ...[
        FormErrorBox(_error!),
        const SizedBox(height: 12),
      ],
      BusyButton(
        label:
            'Remove ${_selected.isEmpty ? '' : '${_selected.length} '}packet(s)',
        busy: _saving,
        onPressed: _save,
      ),
    ],
  );
}
