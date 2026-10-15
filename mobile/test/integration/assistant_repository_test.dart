import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/assistant/assistant_models.dart';
import 'package:lifelink_mobile/features/assistant/assistant_repository.dart';

import '../helpers.dart';

void main() {
  test('universal assistant sends message and the latest ten turns', () async {
    final api = MockedApi(token: 'jwt');
    final history = List.generate(
      12,
      (index) => AssistantMessage.user('Turn $index'),
    );
    api.adapter.onPost(
      '/assistant/chat',
      (server) => server.reply(200, {
        'reply': 'Use your profile.',
        'segments': [
          {'type': 'account', 'text': 'Your account is active.', 'sources': []},
        ],
        'actions': [
          {'label': 'My requests', 'route': '/donor/requests/create'},
        ],
      }),
      data: {
        'message': 'What is my status?',
        'history': [
          for (var index = 2; index < 12; index++)
            {'role': 'user', 'content': 'Turn $index'},
        ],
      },
    );

    final reply = await AssistantRepository(api.client)
        .chat(message: 'What is my status?', history: history);

    expect(reply.segments.single.type, 'account');
    expect(reply.actions.single.route, '/donor/requests/create');
    final body = api.sent.single.data as Map<String, dynamic>;
    expect(body.containsKey('acceptanceId'), isFalse);
    expect(body.containsKey('structured'), isFalse);
  });

  test('assistant models preserve sources and unknown actions', () {
    final reply = AssistantReply.fromJson({
      'reply': 'Read this.',
      'segments': [
        {
          'type': 'medical',
          'text': 'Guidance',
          'sources': [
            {
              'title': 'Blood guide',
              'publisher': 'WHO',
              'url': 'https://example.test/guide',
            },
          ],
        },
      ],
      'actions': [
        {'label': 'Unknown destination', 'route': '/not-allowed'},
      ],
    });

    expect(reply.segments.single.sources.single.publisher, 'WHO');
    expect(reply.actions.single.label, 'Unknown destination');
  });
}
