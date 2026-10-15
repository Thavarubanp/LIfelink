import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import 'assistant_models.dart';
import 'assistant_repository.dart';

class AssistantConversationState {
  const AssistantConversationState({
    this.messages = const [],
    this.thinking = false,
    this.error,
    this.failedMessage,
  });

  final List<AssistantMessage> messages;
  final bool thinking;
  final String? error;
  final String? failedMessage;
}

final assistantConversationProvider =
    NotifierProvider<
      AssistantConversationController,
      AssistantConversationState
    >(AssistantConversationController.new);

class AssistantConversationController
    extends Notifier<AssistantConversationState> {
  static const _displayLimit = 30;

  @override
  AssistantConversationState build() {
    ref.watch(authControllerProvider.select((auth) => auth.user?.userId));
    return const AssistantConversationState();
  }

  void clear() => state = const AssistantConversationState();

  void dismissError() {
    if (state.error == null) return;
    state = AssistantConversationState(messages: state.messages);
  }

  Future<void> retry() async {
    final message = state.failedMessage;
    if (message == null || state.thinking) return;
    await _request(message, appendUserMessage: false);
  }

  Future<void> send(String text) async {
    final message = text.trim();
    if (message.isEmpty || state.thinking) return;

    await _request(message, appendUserMessage: true);
  }

  Future<void> _request(
    String message, {
    required bool appendUserMessage,
  }) async {
    final history = appendUserMessage
        ? state.messages
        : state.messages.isNotEmpty && state.messages.last.isUser
        ? state.messages.sublist(0, state.messages.length - 1)
        : state.messages;

    state = AssistantConversationState(
      messages: appendUserMessage
          ? _limited([...history, AssistantMessage.user(message)])
          : state.messages,
      thinking: true,
    );
    try {
      final reply = await ref
          .read(assistantRepositoryProvider)
          .chat(message: message, history: history);
      if (!ref.mounted) return;
      state = AssistantConversationState(
        messages: _limited([
          ...state.messages,
          AssistantMessage.assistant(
            text: reply.reply,
            segments: reply.segments,
            actions: reply.actions,
          ),
        ]),
      );
    } catch (error) {
      if (!ref.mounted) return;
      state = AssistantConversationState(
        messages: state.messages,
        error: ApiError.from(error).message,
        failedMessage: message,
      );
    }
  }

  List<AssistantMessage> _limited(List<AssistantMessage> messages) =>
      messages.length <= _displayLimit
      ? List.unmodifiable(messages)
      : List.unmodifiable(messages.sublist(messages.length - _displayLimit));
}
