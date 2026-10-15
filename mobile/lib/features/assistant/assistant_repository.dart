import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import 'assistant_models.dart';

class AssistantRepository {
  AssistantRepository(this._api);

  final ApiClient _api;

  Future<AssistantReply> chat({
    required String message,
    required List<AssistantMessage> history,
  }) async => AssistantReply.fromJson(
    unwrapMap(
      await _api.post(
        '/assistant/chat',
        body: {
          'message': message,
          'history': [
            for (final turn in history.takeLast(10))
              {
                'role': turn.isUser ? 'user' : 'assistant',
                'content': turn.historyContent,
              },
          ],
        },
        options: apiOptions(receiveTimeout: const Duration(seconds: 70)),
      ),
    ),
  );
}

extension<T> on List<T> {
  Iterable<T> takeLast(int count) => skip(length > count ? length - count : 0);
}

final assistantRepositoryProvider = Provider<AssistantRepository>(
  (ref) => AssistantRepository(ref.watch(apiClientProvider)),
);
