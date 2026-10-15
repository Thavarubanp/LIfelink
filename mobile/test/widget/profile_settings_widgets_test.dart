import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/core/theme/app_theme.dart';
import 'package:lifelink_mobile/features/home/lifelink_app_bar.dart';
import 'package:lifelink_mobile/features/home/more_screen.dart';
import 'package:lifelink_mobile/features/notifications/notifications_repository.dart';
import 'package:lifelink_mobile/features/profile/profile_screen.dart';
import 'package:lifelink_mobile/features/profile/settings_screen.dart';

import '../helpers.dart';

void main() {
  testWidgets(
    'profile contains details and edit without settings or sign out',
    (tester) async {
      await tester.pumpWidget(
        testApp(
          const ProfileScreen(),
          overrides: [
            authControllerProvider.overrideWith(
              () => FakeAuthController(AuthState.signedIn(testUser())),
            ),
          ],
        ),
      );

      expect(find.text('Test Person'), findsOneWidget);
      expect(find.text('View and edit full profile'), findsOneWidget);
      expect(find.textContaining('Idle sign-out'), findsNothing);
      expect(find.text('Appearance'), findsNothing);
      expect(find.text('Server'), findsNothing);
      expect(find.text('Sign out'), findsNothing);
    },
  );

  testWidgets('settings contains appearance, password and notifications', (
    tester,
  ) async {
    await tester.pumpWidget(testApp(const SettingsScreen()));
    await tester.pump();

    expect(find.text('Appearance'), findsOneWidget);
    expect(find.text('Change password'), findsOneWidget);
    expect(find.text('Phone notifications'), findsOneWidget);
    expect(find.text('Server'), findsNothing);
    expect(find.text('Sign out'), findsNothing);
  });

  testWidgets('More retains sign out and exposes assistant and settings', (
    tester,
  ) async {
    await tester.pumpWidget(
      testApp(
        const MoreScreen(entries: []),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
        ],
      ),
    );

    expect(find.text('LifeLink Assistant'), findsOneWidget);
    expect(find.text('Account status and appeals'), findsOneWidget);
    expect(find.text('Settings'), findsOneWidget);
    expect(find.text('Sign out'), findsOneWidget);
  });

  testWidgets('wordmark uses web red variants and themed Life colour', (
    tester,
  ) async {
    Future<RichText> render(ThemeData theme) async {
      await tester.pumpWidget(
        ProviderScope(
          overrides: [unreadCountProvider.overrideWith(FakeUnreadCount.new)],
          child: MaterialApp(
            theme: theme,
            darkTheme: theme,
            themeMode: theme.brightness == Brightness.dark
                ? ThemeMode.dark
                : ThemeMode.light,
            home: Scaffold(appBar: LifeLinkAppBar(onBrandTap: () {})),
          ),
        ),
      );
      await tester.pumpAndSettle();
      return tester.widget<RichText>(find.text('LifeLink', findRichText: true));
    }

    final light = (await render(AppTheme.light())).text as TextSpan;
    expect((light.children![0] as TextSpan).style?.color, AppColors.slate900);
    expect((light.children![1] as TextSpan).style?.color, AppColors.red600);

    final dark = (await render(AppTheme.dark())).text as TextSpan;
    expect((dark.children![0] as TextSpan).style?.color, AppColors.slate100);
    expect((dark.children![1] as TextSpan).style?.color, AppColors.red500);
  });
}
