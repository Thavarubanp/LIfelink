import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/features/auth/login_screen.dart';
import 'package:lifelink_mobile/features/auth/register_screen.dart';

import '../helpers.dart';

void main() {
  testWidgets(
    'login: empty and invalid fields show validation messages, no request is sent',
    (tester) async {
      final api = MockedApi();
      await tester.pumpWidget(
        testApp(const LoginScreen(), overrides: api.overrides),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('login-submit')));
      await tester.pump();
      expect(find.text('Email is required.'), findsOneWidget);
      expect(find.text('Password is required.'), findsOneWidget);

      await tester.enterText(
        find.byKey(const Key('login-email')),
        'not-an-email',
      );
      await tester.tap(find.byKey(const Key('login-submit')));
      await tester.pump();
      expect(find.text('Invalid email address format.'), findsOneWidget);
      expect(api.sent, isEmpty);
    },
  );

  testWidgets(
    'login: the API message is shown when the credentials are wrong',
    (tester) async {
      final api = MockedApi();
      api.adapter.onPost(
        '/Auth/login',
        (s) => s.reply(401, {
          'success': false,
          'message': 'Invalid email or password.',
        }),
        data: Matchers.any,
      );
      await tester.pumpWidget(
        testApp(const LoginScreen(), overrides: api.overrides),
      );
      await tester.pumpAndSettle();

      await tester.enterText(
        find.byKey(const Key('login-email')),
        'donor@example.test',
      );
      await tester.enterText(
        find.descendant(
          of: find.byKey(const Key('login-password')),
          matching: find.byType(TextFormField),
        ),
        'Wrong#123',
      );
      await tester.tap(find.byKey(const Key('login-submit')));
      await tester.pumpAndSettle();

      expect(find.text('Invalid email or password.'), findsOneWidget);
    },
  );

  testWidgets('login: one action opens the descriptive registration chooser', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 3;
    tester.view.physicalSize = const Size(1080, 2340);
    tester.view.padding = const FakeViewPadding(bottom: 144);
    tester.platformDispatcher.textScaleFactorTestValue = 2;
    addTearDown(tester.view.reset);
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    final api = MockedApi();
    await tester.pumpWidget(
      testApp(const LoginScreen(), overrides: api.overrides),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('login-create-account')), findsOneWidget);
    expect(find.byKey(const Key('login-register-user')), findsNothing);
    expect(find.byKey(const Key('login-register-hospital')), findsNothing);

    await tester.tap(find.byKey(const Key('login-create-account')));
    await tester.pumpAndSettle();

    expect(find.text('Join the LifeLink Network'), findsOneWidget);
    expect(find.text('User Registration'), findsOneWidget);
    expect(find.text('Hospital Registration'), findsOneWidget);
    expect(
      find.textContaining('For blood donors, patients requesting blood'),
      findsOneWidget,
    );
    expect(
      find.textContaining('For hospitals, clinics, and medical facilities'),
      findsOneWidget,
    );
    expect(
      find.textContaining('Doctors: Accounts are provisioned directly'),
      findsOneWidget,
    );
    final note = find.textContaining(
      'Doctors: Accounts are provisioned directly',
    );
    await tester.ensureVisible(note);
    await tester.pumpAndSettle();
    expect(tester.getRect(note).bottom, lessThanOrEqualTo(780 - 48));
    expect(tester.takeException(), isNull);
  });

  testWidgets('register: same rules as the API DTO', (tester) async {
    final api = MockedApi();
    tester.view.physicalSize = const Size(1200, 2400);
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      testApp(const RegisterScreen(), overrides: api.overrides),
    );
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const Key('reg-email')),
      'donor@example.test',
    );
    await tester.enterText(find.byKey(const Key('reg-phone')), '07712');
    await tester.enterText(
      find.descendant(
        of: find.byKey(const Key('reg-password')),
        matching: find.byType(TextFormField),
      ),
      'weakpass',
    );
    await tester.enterText(
      find.descendant(
        of: find.byKey(const Key('reg-confirm')),
        matching: find.byType(TextFormField),
      ),
      'other',
    );
    await tester.ensureVisible(find.byKey(const Key('reg-submit')));
    await tester.tap(find.byKey(const Key('reg-submit')));
    await tester.pump();

    expect(find.text('First name is required.'), findsOneWidget);
    expect(find.text('Last name is required.'), findsOneWidget);
    expect(find.text('Enter a 10-digit phone number.'), findsOneWidget);
    expect(
      find.text('Use upper and lower case letters, a number and a symbol.'),
      findsOneWidget,
    );
    expect(find.text('Passwords do not match.'), findsOneWidget);
    expect(api.sent, isEmpty);
  });
}
