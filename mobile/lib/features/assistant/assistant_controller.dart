import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import 'assistant_models.dart';
import 'assistant_repository.dart';

class AssistantConversationState {
  const AssistantConversationState({
    this.messages = const [],
    this.thinking = false,
  });

  final List<AssistantMessage> messages;
  final bool thinking;
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

  Future<void> send(String text) async {
    final message = text.trim();
    if (message.isEmpty || state.thinking) return;

    final history = state.messages;
    state = AssistantConversationState(
      messages: _limited([...history, AssistantMessage.user(message)]),
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
        messages: _limited([
          ...state.messages,
          AssistantMessage.assistant(text: ApiError.from(error).message),
        ]),
      );
    }
  }

  List<AssistantMessage> _limited(List<AssistantMessage> messages) =>
      messages.length <= _displayLimit
      ? List.unmodifiable(messages)
      : List.unmodifiable(messages.sublist(messages.length - _displayLimit));
}
