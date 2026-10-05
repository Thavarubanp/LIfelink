import 'package:flutter/material.dart';

import '../../core/api/api_error.dart';
import 'screening_inputs.dart';
import 'screening_models.dart';
import 'screening_repository.dart';

Future<bool> showScreeningAnswersSheet(BuildContext context, {
  required ScreeningAnswersData data,
  required bool editing,
  required Future<void> Function(Map<String, dynamic>) onSave,
}) async => await showModalBottomSheet<bool>(
  context: context,
  isScrollControlled: true,
  useSafeArea: true,
  builder: (_) => _AnswersSheet(data: data, editing: editing, onSave: onSave),
) ?? false;

class _AnswersSheet extends StatefulWidget {
  const _AnswersSheet({required this.data, required this.editing, required this.onSave});
  final ScreeningAnswersData data;
  final bool editing;
  final Future<void> Function(Map<String, dynamic>) onSave;
  @override
  State<_AnswersSheet> createState() => _AnswersSheetState();
}

class _AnswersSheetState extends State<_AnswersSheet> {
  late final Map<String, dynamic> _values = Map<String, dynamic>.from(widget.data.answers);
  bool _busy = false;
  String? _error;

  Future<void> _save() async {
    final questions = widget.data.questionnaire ?? const <ScreeningQuestion>[];
    final required = [for (final q in questions) ...q.parts]
        .where((p) => p.required && screeningPartApplies(p, _values));
    if (required.any((p) => !screeningAnswered(p, _values[p.id]))) {
      setState(() => _error = 'Please complete every required answer.');
      return;
    }
    if (_values[screeningConfirmId] != true && _values[screeningConfirmId] != 'Yes') {
      setState(() => _error = 'Confirm that your updated answers are true.');
      return;
    }
    setState(() { _busy = true; _error = null; });
    try {
      await widget.onSave(_values);
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final questions = widget.data.questionnaire;
    return Scaffold(
      appBar: AppBar(title: Text(widget.editing ? 'Update my answers' : 'My screening answers'),
        actions: [IconButton(onPressed: () => Navigator.pop(context), icon: const Icon(Icons.close))]),
      body: ListView(padding: const EdgeInsets.all(16), children: [
        if (widget.editing && questions != null)
          for (final q in questions) Card(child: Padding(padding: const EdgeInsets.all(14), child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Text(q.text, style: const TextStyle(fontWeight: FontWeight.w700)),
              ScreeningInputs(parts: q.parts, values: _values, missing: const [], disabled: _busy,
                onChanged: (id, value) => setState(() => _values[id] = value)),
            ])))
        else
          for (final section in widget.data.sections) Card(child: Padding(padding: const EdgeInsets.all(14), child: Column(
            crossAxisAlignment: CrossAxisAlignment.start, children: [
              Row(children: [Expanded(child: Text(section.title, style: const TextStyle(fontWeight: FontWeight.w700))),
                if (section.confidential) const Icon(Icons.lock_outline, size: 17)]),
              const Divider(),
              for (final item in section.items) ListTile(contentPadding: EdgeInsets.zero,
                title: Text(item.question), subtitle: Text(item.answer)),
            ]))),
        if (_error != null) Padding(padding: const EdgeInsets.only(top: 8), child: Text(_error!,
          style: TextStyle(color: Theme.of(context).colorScheme.error))),
        if (widget.editing) Padding(padding: const EdgeInsets.only(top: 12), child: FilledButton.icon(
          onPressed: _busy ? null : _save, icon: const Icon(Icons.send),
          label: Text(_busy ? 'Sending...' : 'Send updated answers'))),
      ]),
    );
  }
}
