import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/attention/tab_attention_controller.dart';

import '../helpers.dart';

class _CountingTabAttentionController extends TabAttentionController {
  int refreshes = 0;

  @override
  Future<void> refresh() async {
    refreshes++;
  }
}

void main() {
  Future<void> settle() =>
      Future<void>.delayed(const Duration(milliseconds: 30));

  Future<void> waitForRequestCount(MockedApi api, int count) async {
    final deadline = DateTime.now().add(const Duration(seconds: 2));
    while (api.sent.length < count && DateTime.now().isBefore(deadline)) {
      await Future<void>.delayed(const Duration(milliseconds: 10));
    }
  }

  test('signed-out and suspended sessions do not poll tab attention', () async {
    for (final state in [
      const AuthState.signedOut(),
      AuthState.signedIn(testUser(suspended: true)),
    ]) {
      final api = MockedApi(token: 'jwt');
      final container = ProviderContainer(
        overrides: [
          ...api.overrides,
          authControllerProvider.overrideWith(() => FakeAuthController(state)),
        ],
      );
      addTearDown(container.dispose);

      expect(container.read(tabAttentionProvider), const TabAttentionState());
      await settle();
      expect(api.sent, isEmpty);
      container.dispose();
    }
  });

  test(
    'donor cycle shares one timer and performs only its two data calls',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onGet('/BloodRequests/my', (server) => server.reply(200, []))
        ..onGet('/Acceptances/my', (server) => server.reply(200, []));
      final container = ProviderContainer(
        overrides: [
          ...api.overrides,
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
        ],
      );
      addTearDown(container.dispose);

      container.read(tabAttentionProvider);
      await waitForRequestCount(api, 2);

      expect(api.sent.map((r) => r.path), [
        '/BloodRequests/my',
        '/Acceptances/my',
      ]);
      expect(
        api.sent.every((r) => !r.headers.containsKey('X-LifeLink-Activity')),
        isTrue,
      );
    },
  );

  testWidgets(
    'foreground attention polling pauses and resumes with lifecycle',
    (tester) async {
      tester.binding.handleAppLifecycleStateChanged(AppLifecycleState.resumed);
      final controller = _CountingTabAttentionController();

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            authControllerProvider.overrideWith(
              () => FakeAuthController(AuthState.signedIn(testUser())),
            ),
            tabAttentionProvider.overrideWith(() => controller),
          ],
          child: MaterialApp(
            home: Consumer(
              builder: (context, ref, _) {
                ref.watch(tabAttentionProvider);
                return const SizedBox.shrink();
              },
            ),
          ),
        ),
      );
      await tester.pump();
      expect(controller.refreshes, 1);
      final beforePause = controller.refreshes;

      controller.didChangeAppLifecycleState(AppLifecycleState.paused);
      await tester.pump(const Duration(seconds: 31));
      expect(controller.refreshes, beforePause);

      controller.didChangeAppLifecycleState(AppLifecycleState.resumed);
      await tester.pump();
      expect(controller.refreshes, beforePause + 1);
    },
  );
}
