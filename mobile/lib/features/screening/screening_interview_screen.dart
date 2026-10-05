import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import 'screening_inputs.dart';
import 'screening_models.dart';
import 'screening_repository.dart';

class ScreeningInterviewScreen extends ConsumerStatefulWidget {
  const ScreeningInterviewScreen({super.key, required this.acceptanceId});
  final String acceptanceId;
  @override
  ConsumerState<ScreeningInterviewScreen> createState() => _ScreeningInterviewScreenState();
}

class _ScreeningInterviewScreenState extends ConsumerState<ScreeningInterviewScreen> {
  final _input = TextEditingController();
  final List<({bool user, String text})> _messages = [];
  ScreeningSession? _session;
  bool _thinking = true;
  String? _error;

  @override
  void initState() { super.initState(); _resume(); }
  @override
  void dispose() { _input.dispose(); super.dispose(); }

  Future<void> _resume() async {
    setState(() { _thinking = true; _error = null; });
    try {
      final reply = await ref.read(screeningRepositoryProvider).chat(widget.acceptanceId);
      if (!mounted) return;
      _messages.clear();
      for (final turn in reply.screening?.transcript ?? const []) {
        _messages.add((user: false, text: turn.question));
        _messages.add((user: true, text: turn.answer));
      }
      _apply(reply);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally { if (mounted) setState(() => _thinking = false); }
  }

  void _apply(AssistantReply reply) {
    if (reply.reply.isNotEmpty) _messages.add((user: false, text: reply.reply));
    if (reply.screening != null) _session = reply.screening;
    setState(() {});
  }

  Future<void> _send({String? text, Map<String, dynamic>? structured}) async {
    final message = (text ?? '').trim();
    if (_thinking || (message.isEmpty && structured == null)) return;
    setState(() { _messages.add((user: true, text: message.isEmpty ? 'Answered' : message)); _thinking = true; _input.clear(); });
    try {
      _apply(await ref.read(screeningRepositoryProvider).chat(widget.acceptanceId,
        message: structured == null ? message : '', structured: structured));
    } catch (e) {
      if (mounted) setState(() => _messages.add((user: false, text: ApiError.from(e).message)));
    } finally { if (mounted) setState(() => _thinking = false); }
  }

  @override
  Widget build(BuildContext context) {
    final session = _session;
    final finished = session?.isComplete == true || session?.status == 'Submitted';
    final closed = session?.status == 'Closed';
    final paused = session?.status == 'Paused';
    final question = session?.question;
    return Scaffold(
      appBar: AppBar(title: const Text('Donor health screening')),
      body: ContentWidth(maxWidth: 760, child: Column(children: [
        if (session != null && !closed && !paused)
          Padding(padding: const EdgeInsets.fromLTRB(16, 12, 16, 4), child: Column(children: [
            Row(children: [Expanded(child: Text(finished ? 'All questions answered' :
              'Question ${session.sectionIndex} of ${session.sectionCount}${session.section.isEmpty ? '' : ': ${session.section}'}')),
              Text('${finished ? 100 : (session.total == 0 ? 0 : session.answered * 100 ~/ session.total)}%')]),
            const SizedBox(height: 5),
            LinearProgressIndicator(value: finished ? 1 : (session.total == 0 ? 0 : session.answered / session.total)),
          ])),
        Expanded(child: _error != null && _messages.isEmpty
          ? ErrorView(error: ApiError(_error!), onRetry: _resume)
          : RefreshIndicator(onRefresh: _resume, child: ListView(
            physics: const AlwaysScrollableScrollPhysics(), padding: const EdgeInsets.all(16), children: [
              const InfoBanner('Answer honestly. Only the reviewing doctor sees your answers and makes the decision.',
                color: AppColors.blue600, icon: Icons.medical_services_outlined),
              const SizedBox(height: 12),
              for (final message in _messages) _Bubble(user: message.user, text: message.text),
              if (question != null && !finished && !closed && !paused)
                _QuestionBubble(key: ValueKey('${question.id}-${question.missing.join(',')}'), question: question,
                  disabled: _thinking, onSend: (answers, summary) => _send(text: summary, structured: answers)),
              if (_thinking) const Padding(padding: EdgeInsets.all(12), child: LinearProgressIndicator()),
            ]))),
        _footer(finished, closed, paused, question != null),
      ])),
    );
  }

  Widget _footer(bool finished, bool closed, bool paused, bool hasQuestion) {
    if (finished) {
      return _StateFooter(icon: Icons.check_circle_outline, text: 'Your answers were sent to the doctor.',
        action: () => context.go(AppRoutes.donorAcceptances), actionLabel: 'View status');
    }
    if (closed) {
      return const _StateFooter(icon: Icons.lock_outline, text: 'Screening is closed for this donation.');
    }
    if (paused) {
      return const _StateFooter(icon: Icons.pause_circle_outline,
        text: 'Screening is paused while the request is suspended. Your answers are saved.');
    }
    if (!hasQuestion && _session != null) {
      return _StateFooter(icon: Icons.cloud_upload_outlined,
        text: 'All answers are saved but have not reached the doctor.', actionLabel: 'Retry', action: _resume);
    }
    return SafeArea(top: false, child: Padding(padding: const EdgeInsets.all(10), child: Row(children: [
      Expanded(child: TextField(controller: _input, maxLength: 1000,
        decoration: const InputDecoration(counterText: '', hintText: 'Type an answer or ask what something means...'),
        onSubmitted: (value) => _send(text: value))),
      const SizedBox(width: 8),
      IconButton.filled(onPressed: _thinking ? null : () => _send(text: _input.text), icon: const Icon(Icons.send)),
    ])));
  }
}

class _Bubble extends StatelessWidget {
  const _Bubble({required this.user, required this.text});
  final bool user;
  final String text;
  @override
  Widget build(BuildContext context) => Align(alignment: user ? Alignment.centerRight : Alignment.centerLeft,
    child: Container(constraints: const BoxConstraints(maxWidth: 580), margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(12), decoration: BoxDecoration(
        color: user ? AppColors.red600 : Theme.of(context).colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(14)),
      child: Text(text, style: TextStyle(color: user ? Colors.white : null))));
}

class _QuestionBubble extends StatefulWidget {
  const _QuestionBubble({super.key, required this.question, required this.disabled, required this.onSend});
  final ScreeningQuestion question;
  final bool disabled;
  final void Function(Map<String, dynamic>, String) onSend;
  @override
  State<_QuestionBubble> createState() => _QuestionBubbleState();
}

class _QuestionBubbleState extends State<_QuestionBubble> {
  late final Map<String, dynamic> _values = {
    for (final part in widget.question.parts) part.id: screeningInputValue(part, part.defaultValue),
  };
  @override
  Widget build(BuildContext context) {
    final answers = collectScreeningAnswers(widget.question.parts, _values);
    return Card(child: Padding(padding: const EdgeInsets.all(14), child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      Row(children: [Expanded(child: Text(widget.question.text, style: const TextStyle(fontWeight: FontWeight.w700))),
        if (widget.question.confidential) const Icon(Icons.lock_outline, size: 17)]),
      if (widget.question.followUp) const Text('Just the highlighted part, please:', style: TextStyle(color: AppColors.rose600)),
      ScreeningInputs(parts: widget.question.parts, values: _values, missing: widget.question.missing,
        disabled: widget.disabled, onChanged: (id, value) => setState(() => _values[id] = value)),
      const SizedBox(height: 10),
      FilledButton.icon(onPressed: widget.disabled || answers.isEmpty ? null : () => widget.onSend(
        answers, summariseScreeningAnswers(widget.question.parts, _values)), icon: const Icon(Icons.send),
        label: Text(widget.question.type == 'confirm' ? 'Send my answers to the doctor' : 'Send answer')),
    ])));
  }
}

class _StateFooter extends StatelessWidget {
  const _StateFooter({required this.icon, required this.text, this.action, this.actionLabel});
  final IconData icon;
  final String text;
  final VoidCallback? action;
  final String? actionLabel;
  @override
  Widget build(BuildContext context) => SafeArea(top: false, child: Padding(padding: const EdgeInsets.all(14), child: Row(children: [
    Icon(icon), const SizedBox(width: 8), Expanded(child: Text(text)),
    if (action != null) FilledButton(onPressed: action, child: Text(actionLabel ?? 'Continue')),
  ])));
}
