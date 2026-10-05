import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/api/idempotency.dart';
import '../../core/config/constants.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import '../inventory/inventory_models.dart';
import '../inventory/inventory_repository.dart';
import '../inventory/packet_picker.dart';
import '../profile/profile_repository.dart';
import 'transfer_repository.dart';

/// Other approved, unsuspended hospitals, with every hospital's stock for the "available" hints.
final _transferFormDataProvider = FutureProvider.autoDispose<(List<HospitalSummary>, List<InventoryItem>, String)>((ref) async {
  final me = await ref.watch(myHospitalProvider.future);
  final hospitals = await ref.watch(transferRepositoryProvider).verifiedHospitals();
  final stock = await ref.watch(inventoryRepositoryProvider).all().catchError((_) => <InventoryItem>[]);
  final others = hospitals.where((h) => h.isVerified && !h.isSuspended && h.hospitalId != me.hospitalId).toList()
    ..sort((a, b) => a.name.compareTo(b.name));
  return (others, stock, me.hospitalId);
});

/// Returns true when a transfer was created.
Future<bool> showCreateTransferSheet(BuildContext context) async =>
    await showModalBottomSheet<bool>(context: context, isScrollControlled: true, builder: (_) => const CreateTransferSheet()) ??
    false;

/// Request blood from, or offer blood to, another approved hospital (same form as the web page).
class CreateTransferSheet extends ConsumerStatefulWidget {
  const CreateTransferSheet({super.key});

  @override
  ConsumerState<CreateTransferSheet> createState() => _CreateTransferSheetState();
}

class _CreateTransferSheetState extends ConsumerState<CreateTransferSheet> {
  final _formKey = GlobalKey<FormState>();
  String _type = 'Request';
  String? _hospitalId;
  String _group = 'O+';
  final _units = TextEditingController(text: '1');
  final _notes = TextEditingController();
  List<String> _packetIds = [];
  // One key per form: a double submit or retry never creates the same transfer twice
  String _key = newIdempotencyKey();
  bool _saving = false;
  String? _error;

  bool get _isOffer => _type == 'Offer';

  @override
  void dispose() {
    _units.dispose();
    _notes.dispose();
    super.dispose();
  }

  int _availableAt(List<InventoryItem> stock, String hospitalId) =>
      stock.where((s) => s.hospitalId == hospitalId && s.bloodGroup == _group).map((s) => s.unitsAvailable).firstOrNull ?? 0;

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    if (_isOffer && _packetIds.isEmpty) {
      setState(() => _error = 'Choose the packets you are offering.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await ref.read(transferRepositoryProvider).create(
            transferType: _type,
            counterpartHospitalId: _hospitalId!,
            bloodGroup: _group,
            unitsRequested: _isOffer ? _packetIds.length : int.parse(_units.text.trim()),
            notes: _notes.text.trim(),
            packetIds: _isOffer ? _packetIds : null,
            idempotencyKey: _key,
          );
      if (!mounted) return;
      showSnack(
        context,
        _isOffer ? 'The packets are held until the other hospital answers.' : 'The other hospital has been notified.',
        type: SnackType.success,
        title: _isOffer ? 'Offer sent' : 'Request sent',
      );
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      setState(() => _error = api.message);
      // Already submitted, or the selected packets were just used elsewhere: start a fresh submission
      if (api.isConflict) {
        _key = newIdempotencyKey();
        _packetIds = [];
        ref.invalidate(availablePacketsProvider(_group));
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final data = ref.watch(_transferFormDataProvider);
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
      child: SingleChildScrollView(
        child: AsyncView(
          value: data,
          onRetry: () => ref.invalidate(_transferFormDataProvider),
          data: (d) {
            final (hospitals, stock, me) = d;
            return Form(
              key: _formKey,
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const Text('New transfer', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
                  const SizedBox(height: 12),
                  SegmentedButton<String>(
                    segments: const [
                      ButtonSegment(value: 'Request', label: Text('Request blood'), icon: Icon(Icons.call_received)),
                      ButtonSegment(value: 'Offer', label: Text('Offer blood'), icon: Icon(Icons.call_made)),
                    ],
                    selected: {_type},
                    onSelectionChanged: (s) => setState(() {
                      _type = s.first;
                      _packetIds = [];
                    }),
                  ),
                  const SizedBox(height: 14),
                  DropdownButtonFormField<String>(
                    key: const Key('transfer-group'),
                    initialValue: _group,
                    decoration: const InputDecoration(labelText: 'Blood group'),
                    items: [for (final g in AppConstants.bloodGroups) DropdownMenuItem(value: g, child: Text(g))],
                    onChanged: (v) => setState(() {
                      _group = v ?? _group;
                      _packetIds = [];
                    }),
                  ),
                  const SizedBox(height: 14),
                  if (hospitals.isEmpty)
                    const InfoBanner('There is no other approved hospital to transfer with yet.')
                  else
                    DropdownButtonFormField<String>(
                      key: const Key('transfer-hospital'),
                      initialValue: _hospitalId,
                      isExpanded: true,
                      decoration: InputDecoration(labelText: _isOffer ? 'Offer to' : 'Request from'),
                      items: [
                        for (final h in hospitals)
                          DropdownMenuItem(
                            value: h.hospitalId,
                            child: Text(
                              _isOffer ? h.name : '${h.name} (${_availableAt(stock, h.hospitalId)} $_group available)',
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                      ],
                      onChanged: (v) => setState(() => _hospitalId = v),
                      validator: (v) => v == null ? 'Select a hospital.' : null,
                    ),
                  const SizedBox(height: 14),
                  if (_isOffer) ...[
                    Text('Packets to offer (${_availableAt(stock, me)} $_group available)',
                        style: const TextStyle(fontWeight: FontWeight.w600)),
                    const SizedBox(height: 6),
                    PacketPicker(bloodGroup: _group, selected: _packetIds, onChanged: (ids) => setState(() => _packetIds = ids)),
                  ] else
                    TextFormField(
                      key: const Key('transfer-units'),
                      controller: _units,
                      keyboardType: TextInputType.number,
                      inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                      decoration: const InputDecoration(labelText: 'Units'),
                      validator: (v) => Validators.intRange(v, 'Units', min: 1),
                    ),
                  const SizedBox(height: 14),
                  TextField(controller: _notes, maxLength: 500, decoration: const InputDecoration(labelText: 'Notes (optional)')),
                  if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
                  BusyButton(
                    label: _isOffer ? 'Send offer (${_packetIds.length})' : 'Send request',
                    icon: Icons.send,
                    busy: _saving,
                    onPressed: hospitals.isEmpty ? null : _submit,
                  ),
                ],
              ),
            );
          },
        ),
      ),
    );
  }
}
