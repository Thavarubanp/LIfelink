import 'dart:convert';

import 'package:flutter/material.dart';

import '../../core/utils/format.dart';
import 'screening_models.dart';

Object? screeningInputValue(ScreeningPart part, Object? value) {
  if (value == null) return part.type == 'trips' ? <dynamic>[] : null;
  if ((part.type == 'checklist' || part.type == 'trips') && value is String) {
    try { return jsonDecode(value); } catch (_) { return part.type == 'trips' ? <dynamic>[] : null; }
  }
  return value;
}

class ScreeningInputs extends StatelessWidget {
  const ScreeningInputs({super.key, required this.parts, required this.values, required this.onChanged,
    this.disabled = false, this.missing = const []});
  final List<ScreeningPart> parts;
  final Map<String, dynamic> values;
  final void Function(String id, Object? value) onChanged;
  final bool disabled;
  final List<String> missing;

  @override
  Widget build(BuildContext context) => Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
    for (final part in parts.where((p) => screeningPartApplies(p, values))) ...[
      if (part.type != 'confirm')
        Padding(padding: const EdgeInsets.only(top: 8, bottom: 4), child: Text(
          '${part.label}${part.required ? '' : ' (optional)'}',
          style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600,
            color: missing.contains(part.id) ? Theme.of(context).colorScheme.error : null))),
      _PartInput(part: part, value: values[part.id], disabled: disabled, onChanged: (v) => onChanged(part.id, v)),
    ],
  ]);
}

class _PartInput extends StatelessWidget {
  const _PartInput({required this.part, required this.value, required this.disabled, required this.onChanged});
  final ScreeningPart part;
  final Object? value;
  final bool disabled;
  final ValueChanged<Object?> onChanged;

  @override
  Widget build(BuildContext context) {
    if (part.type == 'date') return _DateInput(part: part, value: value, disabled: disabled, onChanged: onChanged);
    if (part.type == 'number') {
      return TextFormField(
      initialValue: value?.toString(), enabled: !disabled, keyboardType: const TextInputType.numberWithOptions(decimal: true),
      decoration: InputDecoration(hintText: [if (part.min != null) 'Min ${part.min}', if (part.max != null) 'Max ${part.max}'].join(' · ')),
      onChanged: onChanged);
    }
    if (part.type == 'select' || part.type == 'yes_no') {
      return Wrap(spacing: 7, runSpacing: 7, children: [
      for (final option in part.options) ChoiceChip(label: Text(option), selected: value == option,
        onSelected: disabled ? null : (_) => onChanged(option)),
      ]);
    }
    if (part.type == 'checklist') {
      final rawValue = value;
      final list = rawValue is List ? rawValue.map((e) => '$e').toList() : <String>[];
      return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
        for (final option in part.options) CheckboxListTile(
          dense: true, contentPadding: EdgeInsets.zero, title: Text(option), value: list.contains(option),
          onChanged: disabled ? null : (_) => onChanged(list.contains(option)
              ? list.where((x) => x != option).toList() : [...list, option])),
        if (part.allowNone) ChoiceChip(label: const Text('None of these'), selected: rawValue is List && rawValue.isEmpty,
          onSelected: disabled ? null : (_) => onChanged(<String>[])),
      ]);
    }
    if (part.type == 'trips') return _TripsInput(value: value, disabled: disabled, onChanged: onChanged);
    if (part.type == 'confirm') {
      return CheckboxListTile(
        contentPadding: EdgeInsets.zero, title: Text(part.label, style: const TextStyle(fontWeight: FontWeight.w600)),
        value: value == 'Yes', onChanged: disabled ? null : (v) => onChanged(v == true ? 'Yes' : ''));
    }
    return TextFormField(initialValue: value?.toString(), enabled: !disabled, maxLength: 200, onChanged: onChanged);
  }
}

class _DateInput extends StatelessWidget {
  const _DateInput({required this.part, required this.value, required this.disabled, required this.onChanged});
  final ScreeningPart part;
  final Object? value;
  final bool disabled;
  final ValueChanged<Object?> onChanged;

  Future<void> _pick(BuildContext context) async {
    final parsed = DateTime.tryParse('$value');
    final today = Fmt.sriLankaToday();
    final date = await showDatePicker(context: context, initialDate: parsed ?? today,
      firstDate: DateTime(1900), lastDate: today);
    if (date != null) onChanged(Fmt.apiDate(date));
  }

  @override
  Widget build(BuildContext context) {
    final unknown = value == screeningUnknown;
    return Wrap(spacing: 8, runSpacing: 6, crossAxisAlignment: WrapCrossAlignment.center, children: [
      OutlinedButton.icon(onPressed: disabled || unknown ? null : () => _pick(context),
        icon: const Icon(Icons.calendar_month_outlined), label: Text(unknown ? 'Date unknown' : (value?.toString().isNotEmpty == true ? '$value' : 'Choose date'))),
      if (part.allowUnknown) FilterChip(label: const Text('I don\'t remember'), selected: unknown,
        onSelected: disabled ? null : (selected) => onChanged(selected ? screeningUnknown : null)),
    ]);
  }
}

class _TripsInput extends StatelessWidget {
  const _TripsInput({required this.value, required this.disabled, required this.onChanged});
  final Object? value;
  final bool disabled;
  final ValueChanged<Object?> onChanged;

  List<Map<String, dynamic>> get trips => value is List && (value as List).isNotEmpty
      ? (value as List).whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList()
      : [<String, dynamic>{'country': '', 'return_date': ''}];

  @override
  Widget build(BuildContext context) {
    final current = trips;
    void set(int index, String key, Object? next) {
      final updated = [for (final trip in current) Map<String, dynamic>.from(trip)];
      updated[index][key] = next;
      onChanged(updated);
    }
    return Column(children: [
      for (var i = 0; i < current.length; i++)
        Padding(padding: const EdgeInsets.only(bottom: 8), child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Expanded(child: TextFormField(initialValue: '${current[i]['country']}', enabled: !disabled,
            maxLength: 80, decoration: const InputDecoration(labelText: 'Country'), onChanged: (v) => set(i, 'country', v))),
          const SizedBox(width: 8),
          Expanded(child: _DateInput(part: const ScreeningPart('return', 'Return date', 'date', [], true, true, false, false, null, null, null, null),
            value: current[i]['return_date'], disabled: disabled, onChanged: (v) => set(i, 'return_date', v))),
          if (current.length > 1) IconButton(icon: const Icon(Icons.delete_outline), onPressed: disabled ? null : () {
            final updated = [...current]..removeAt(i); onChanged(updated);
          }),
        ])),
      Align(alignment: Alignment.centerLeft, child: TextButton.icon(onPressed: disabled ? null : () => onChanged([
        ...current, {'country': '', 'return_date': ''}
      ]), icon: const Icon(Icons.add), label: const Text('Add another country'))),
    ]);
  }
}
