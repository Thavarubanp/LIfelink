import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../attention/tab_attention_controller.dart';
import 'screening_inputs.dart';
import 'screening_models.dart';
import 'screening_repository.dart';

class _PendingScreeningAnswer {
  const _PendingScreeningAnswer({
    required this.message,
    required this.displayText,
    required this.structured,
    required this.answeredBefore,
    required this.transcriptLengthBefore,
    required this.questionKeyBefore,
  });

  final String message;
  final String displayText;
  final Map<String, dynamic>? structured;
  final int answeredBefore;
  final int transcriptLengthBefore;
  final String questionKeyBefore;

  bool isConfirmedBy(ScreeningSession? session) {
    if (session == null) return false;
    if (session.isComplete || session.status == 'Submitted') return true;
    if (session.answered > answeredBefore ||
        session.transcript.length > transcriptLengthBefore) {
      return true;
    }
    return questionKeyBefore.isNotEmpty &&
        _questionKey(session.question) != questionKeyBefore;
  }

  static String _questionKey(ScreeningQuestion? question) =>
      question == null ? '' : '${question.id}:${question.missing.join(',')}';
}

class ScreeningInterviewScreen extends ConsumerStatefulWidget {
  const ScreeningInterviewScreen({
    super.key,
    required this.acceptanceId,
    this.returnPath = AppRoutes.donorAcceptances,
  });
  final String acceptanceId;
  final String returnPath;
  @override
  ConsumerState<ScreeningInterviewScreen> createState() =>
      _ScreeningInterviewScreenState();
}

class _ScreeningInterviewScreenState
    extends ConsumerState<ScreeningInterviewScreen> {
  final _input = TextEditingController();
  final List<({bool user, String text})> _messages = [];
  ScreeningSession? _session;
  _PendingScreeningAnswer? _failedAnswer;
  bool _thinking = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _resume();
  }

  @override
  void dispose() {
    _input.dispose();
    super.dispose();
  }

  Future<void> _resume() async {
    setState(() {
      _thinking = true;
      _error = null;
    });
    try {
      final reply = await ref
          .read(screeningRepositoryProvider)
          .chat(widget.acceptanceId);
      if (!mounted) return;
      final pending = _failedAnswer;
      if (pending != null && !pending.isConfirmedBy(reply.screening)) {
        setState(() {
          if (reply.screening != null) _session = reply.screening;
          _error = 'The server has not confirmed this answer yet. Retry it.';
        });
        return;
      }
      _messages.clear();
      for (final turn in reply.screening?.transcript ?? const []) {
        _messages.add((user: false, text: turn.question));
        _messages.add((user: true, text: turn.answer));
      }
      if (pending != null) _clearConfirmedAnswer();
      _apply(reply);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _thinking = false);
    }
  }

  void _apply(AssistantReply reply) {
    if (reply.reply.isNotEmpty) _messages.add((user: false, text: reply.reply));
    if (reply.screening != null) _session = reply.screening;
    setState(() {});
  }

  Future<void> _send({String? text, Map<String, dynamic>? structured}) async {
    final message = (text ?? '').trim();
    if (_thinking ||
        _failedAnswer != null ||
        (message.isEmpty && structured == null)) {
      return;
    }
    final session = _session;
    final pending = _PendingScreeningAnswer(
      message: structured == null ? message : '',
      displayText: message.isEmpty ? 'Answered' : message,
      structured: structured == null
          ? null
          : Map<String, dynamic>.from(structured),
      answeredBefore: session?.answered ?? 0,
      transcriptLengthBefore: session?.transcript.length ?? 0,
      questionKeyBefore: _PendingScreeningAnswer._questionKey(
        session?.question,
      ),
    );
    setState(() {
      _messages.add((user: true, text: pending.displayText));
      _failedAnswer = pending;
      _thinking = true;
      _error = null;
    });
    await _submitPending(pending);
  }

  Future<void> _submitPending(_PendingScreeningAnswer pending) async {
    try {
      final reply = await ref
          .read(screeningRepositoryProvider)
          .chat(
            widget.acceptanceId,
            message: pending.message,
            structured: pending.structured,
          );
      if (!mounted) return;
      _clearConfirmedAnswer();
      _apply(reply);
      if (reply.screening?.isComplete == true ||
          reply.screening?.status == 'Submitted') {
        ref.read(tabAttentionProvider.notifier).refresh();
      }
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _thinking = false);
    }
  }

  Future<void> _retryFailedAnswer() async {
    final pending = _failedAnswer;
    if (_thinking || pending == null) return;
    setState(() {
      _thinking = true;
      _error = null;
    });
    try {
      // Reconcile first: the original response may have been lost after the
      // server saved the answer. Resending without this check could duplicate it.
      final saved = await ref
          .read(screeningRepositoryProvider)
          .chat(widget.acceptanceId);
      if (!mounted) return;
      if (pending.isConfirmedBy(saved.screening)) {
        _messages.clear();
        for (final turn in saved.screening?.transcript ?? const []) {
          _messages.add((user: false, text: turn.question));
          _messages.add((user: true, text: turn.answer));
        }
        _clearConfirmedAnswer();
        _apply(saved);
        if (saved.screening?.isComplete == true ||
            saved.screening?.status == 'Submitted') {
          ref.read(tabAttentionProvider.notifier).refresh();
        }
        return;
      }
      await _submitPending(pending);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted && _thinking) setState(() => _thinking = false);
    }
  }

  void _clearConfirmedAnswer() {
    _failedAnswer = null;
    _input.clear();
  }

  @override
  Widget build(BuildContext context) {
    final session = _session;
    final finished =
        session?.isComplete == true || session?.status == 'Submitted';
    final closed = session?.status == 'Closed';
    final paused = session?.status == 'Paused';
    final question = session?.question;
    return Scaffold(
      appBar: AppBar(title: const Text('Donor health screening')),
      body: ContentWidth(
        maxWidth: 760,
        child: Column(
          children: [
            if (session != null && !closed && !paused)
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                child: Column(
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            finished
                                ? 'All questions answered'
                                : 'Question ${session.sectionIndex} of ${session.sectionCount}${session.section.isEmpty ? '' : ': ${session.section}'}',
                          ),
                        ),
                        Text(
                          '${finished ? 100 : (session.total == 0 ? 0 : session.answered * 100 ~/ session.total)}%',
                        ),
                      ],
                    ),
                    const SizedBox(height: 5),
                    LinearProgressIndicator(
                      value: finished
                          ? 1
                          : (session.total == 0
                                ? 0
                                : session.answered / session.total),
                    ),
                  ],
                ),
              ),
            Expanded(
              child: _error != null && _messages.isEmpty
                  ? ErrorView(error: ApiError(_error!), onRetry: _resume)
                  : RefreshIndicator(
                      onRefresh: _resume,
                      child: ListView(
                        physics: const AlwaysScrollableScrollPhysics(),
                        padding: const EdgeInsets.all(16),
                        children: [
                          const InfoBanner(
                            'Answer honestly. Only the reviewing doctor sees your answers and makes the decision.',
                            color: AppColors.blue600,
                            icon: Icons.medical_services_outlined,
                          ),
                          const SizedBox(height: 12),
                          for (final message in _messages)
                            _Bubble(user: message.user, text: message.text),
                          if (_error != null && _messages.isNotEmpty) ...[
                            InfoBanner(
                              'Your answer was not confirmed: $_error',
                              color: AppColors.rose600,
                              icon: Icons.cloud_off_outlined,
                            ),
                            const SizedBox(height: 8),
                            Align(
                              alignment: Alignment.centerLeft,
                              child: OutlinedButton.icon(
                                onPressed: _thinking
                                    ? null
                                    : _retryFailedAnswer,
                                icon: const Icon(Icons.refresh),
                                label: const Text('Retry answer'),
                              ),
                            ),
                            const SizedBox(height: 8),
                          ],
                          if (question != null &&
                              !finished &&
                              !closed &&
                              !paused)
                            _QuestionBubble(
                              key: ValueKey(
                                '${question.id}-${question.missing.join(',')}',
                              ),
                              question: question,
                              disabled: _thinking || _failedAnswer != null,
                              onSend: (answers, summary) =>
                                  _send(text: summary, structured: answers),
                            ),
                          if (_thinking)
                            const Padding(
                              padding: EdgeInsets.all(12),
                              child: LinearProgressIndicator(),
                            ),
                        ],
                      ),
                    ),
            ),
            _footer(finished, closed, paused, question != null),
          ],
        ),
      ),
    );
  }

  Widget _footer(bool finished, bool closed, bool paused, bool hasQuestion) {
    if (finished) {
      return _StateFooter(
        icon: Icons.check_circle_outline,
        text: 'Your answers were sent to the doctor.',
        action: () => context.go(widget.returnPath),
        actionLabel: 'View status',
      );
    }
    if (closed) {
      return const _StateFooter(
        icon: Icons.lock_outline,
        text: 'Screening is closed for this donation.',
      );
    }
    if (paused) {
      return const _StateFooter(
        icon: Icons.pause_circle_outline,
        text: 'Screening is paused while the request is suspended. Your answers are saved.',
      );
    }
    if (!hasQuestion && _session != null) {
      return _StateFooter(
        icon: Icons.cloud_upload_outlined,
        text: 'All answers are saved but have not reached the doctor.',
        actionLabel: 'Retry',
        action: _resume,
      );
    }
    return SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.all(10),
        child: Row(
          children: [
            Expanded(
              child: TextField(
                controller: _input,
                maxLength: 1000,
                decoration: const InputDecoration(
                  counterText: '',
                  hintText: 'Type an answer or ask what something means...',
                ),
                onSubmitted: (value) => _send(text: value),
              ),
            ),
            const SizedBox(width: 8),
            IconButton.filled(
              onPressed: _thinking || _failedAnswer != null
                  ? null
                  : () => _send(text: _input.text),
              icon: const Icon(Icons.send),
            ),
          ],
        ),
      ),
    );
  }
}

class _Bubble extends StatelessWidget {
  const _Bubble({required this.user, required this.text});
  final bool user;
  final String text;
  @override
  Widget build(BuildContext context) => Align(
    alignment: user ? Alignment.centerRight : Alignment.centerLeft,
    child: Container(
      constraints: const BoxConstraints(maxWidth: 580),
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: user
            ? AppColors.red600
            : Theme.of(context).colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Text(text, style: TextStyle(color: user ? Colors.white : null)),
    ),
  );
}

class _QuestionBubble extends StatefulWidget {
  const _QuestionBubble({
    super.key,
    required this.question,
    required this.disabled,
    required this.onSend,
  });
  final ScreeningQuestion question;
  final bool disabled;
  final void Function(Map<String, dynamic>, String) onSend;
  @override
  State<_QuestionBubble> createState() => _QuestionBubbleState();
}

class _QuestionBubbleState extends State<_QuestionBubble> {
  late final Map<String, dynamic> _values = {
    for (final part in widget.question.parts)
      part.id: screeningInputValue(part, part.defaultValue),
  };
  @override
  Widget build(BuildContext context) {
    final answers = collectScreeningAnswers(widget.question.parts, _values);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    widget.question.text,
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                ),
                if (widget.question.confidential)
                  const Icon(Icons.lock_outline, size: 17),
              ],
            ),
            if (widget.question.followUp)
              const Text(
                'Just the highlighted part, please:',
                style: TextStyle(color: AppColors.rose600),
              ),
            ScreeningInputs(
              parts: widget.question.parts,
              values: _values,
              missing: widget.question.missing,
              disabled: widget.disabled,
              onChanged: (id, value) => setState(() => _values[id] = value),
            ),
            const SizedBox(height: 10),
            FilledButton.icon(
              onPressed: widget.disabled || answers.isEmpty
                  ? null
                  : () => widget.onSend(
                      answers,
                      summariseScreeningAnswers(widget.question.parts, _values),
                    ),
              icon: const Icon(Icons.send),
              label: Text(
                widget.question.type == 'confirm'
                    ? 'Send my answers to the doctor'
                    : 'Send answer',
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _StateFooter extends StatelessWidget {
  const _StateFooter({
    required this.icon,
    required this.text,
    this.action,
    this.actionLabel,
  });
  final IconData icon;
  final String text;
  final VoidCallback? action;
  final String? actionLabel;
  @override
  Widget build(BuildContext context) => SafeArea(
    top: false,
    child: Padding(
      padding: const EdgeInsets.all(14),
      child: Row(
        children: [
          Icon(icon),
          const SizedBox(width: 8),
          Expanded(child: Text(text)),
          if (action != null)
            FilledButton(
              onPressed: action,
              child: Text(actionLabel ?? 'Continue'),
            ),
        ],
      ),
    ),
  );
}
