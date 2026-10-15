import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/notifications/notifications_repository.dart';
import 'package:lifelink_mobile/features/notifications/notifications_screen.dart';

import '../helpers.dart';

class _TrackingUnreadCount extends UnreadCountController {
  _TrackingUnreadCount(this.onBuild);

  final VoidCallback onBuild;

  @override
  int build() {
    onBuild();
    return 2;
  }

  @override
  Future<void> refresh() async {}
}

void main() {
  testWidgets(
    'notification cards expose mark-read and dismiss and invalidate badges',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2340);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);

      var unreadBuilds = 0;
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onGet(
          '/notifications/my',
          (server) => server.reply(200, [
            {
              'notificationId': 'unread',
              'title': 'Blood request updated',
              'message': 'A donor has accepted your request.',
              'notificationType': 'BloodRequest',
              'isRead': false,
              'createdAt': '2026-10-15T04:30:00Z',
            },
            {
              'notificationId': 'read',
              'title': 'Welcome',
              'message': 'Your account is ready.',
              'notificationType': 'Account',
              'isRead': true,
              'createdAt': '2026-10-14T04:30:00Z',
            },
          ]),
        )
        ..onPatch(
          '/notifications/unread/read',
          (server) => server.reply(204, null),
        )
        ..onDelete('/notifications/read', (server) => server.reply(204, null));

      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            ...api.overrides,
            authControllerProvider.overrideWith(
              () => FakeAuthController(AuthState.signedIn(testUser())),
            ),
            unreadCountProvider.overrideWith(
              () => _TrackingUnreadCount(() => unreadBuilds++),
            ),
          ],
          child: const MaterialApp(home: NotificationsScreen()),
        ),
      );
      await tester.pumpAndSettle();

      expect(
        find.byKey(const ValueKey('mark-read-notification-unread')),
        findsOneWidget,
      );
      expect(
        find.byKey(const ValueKey('mark-read-notification-read')),
        findsNothing,
      );
      expect(
        find.byKey(const ValueKey('dismiss-notification-unread')),
        findsOneWidget,
      );
      expect(
        find.byKey(const ValueKey('dismiss-notification-read')),
        findsOneWidget,
      );

      final buildsBeforeRead = unreadBuilds;
      await tester.tap(
        find.byKey(const ValueKey('mark-read-notification-unread')),
      );
      await tester.pumpAndSettle();

      expect(
        find.byKey(const ValueKey('mark-read-notification-unread')),
        findsNothing,
      );
      expect(
        api.sent.where(
          (request) => request.path == '/notifications/unread/read',
        ),
        hasLength(1),
      );
      expect(unreadBuilds, greaterThan(buildsBeforeRead));

      final buildsBeforeDismiss = unreadBuilds;
      await tester.tap(find.byKey(const ValueKey('dismiss-notification-read')));
      await tester.pump();

      expect(find.text('Welcome'), findsNothing);
      await tester.pumpAndSettle();
      expect(
        api.sent.where((request) => request.path == '/notifications/read'),
        hasLength(1),
      );
      expect(unreadBuilds, greaterThan(buildsBeforeDismiss));
    },
  );
}
