import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/core/routing/app_router.dart';
import 'package:lifelink_mobile/core/routing/routes.dart';
import 'package:lifelink_mobile/core/theme/app_theme.dart';
import 'package:lifelink_mobile/core/widgets/floating_overlay_padding.dart';
import 'package:lifelink_mobile/core/widgets/state_views.dart';
import 'package:lifelink_mobile/features/assistant/assistant_launcher.dart';
import 'package:lifelink_mobile/features/assistant/assistant_sheet.dart';

import '../helpers.dart';

Future<void> pumpLauncher(
  WidgetTester tester, {
  required AuthState auth,
  String path = AppRoutes.donorHome,
  bool primaryFab = false,
  bool bottomNavigation = false,
  Size logicalSize = const Size(360, 780),
  double textScale = 1,
  double safeBottom = 0,
  double navigationHeight = 80,
  List<Override> overrides = const [],
}) async {
  tester.view.devicePixelRatio = 3;
  tester.view.physicalSize = logicalSize * 3;
  tester.view.padding = FakeViewPadding(bottom: safeBottom * 3);
  tester.platformDispatcher.textScaleFactorTestValue = textScale;
  addTearDown(tester.view.reset);
  addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);

  final router = GoRouter(
    navigatorKey: rootNavigatorKey,
    initialLocation: path,
    routes: [
      GoRoute(
        path: path,
        builder: (_, _) => Scaffold(
          floatingActionButton: primaryFab
              ? FloatingActionButton.extended(
                  key: const Key('primary-fab'),
                  onPressed: () {},
                  icon: const Icon(Icons.add),
                  label: const Text('New transfer'),
                )
              : null,
          bottomNavigationBar: bottomNavigation
              ? BottomNavigationBarSizeReporter(
                  child: NavigationBar(
                    height: navigationHeight,
                    destinations: const [
                      NavigationDestination(
                        icon: Icon(Icons.home_outlined),
                        label: 'Home',
                      ),
                      NavigationDestination(
                        icon: Icon(Icons.menu),
                        label: 'More',
                      ),
                    ],
                  ),
                )
              : null,
          body: RefreshableScroll(
            onRefresh: () async {},
            child: const SizedBox(height: 900, width: double.infinity),
          ),
        ),
      ),
    ],
  );
  addTearDown(router.dispose);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        ...overrides,
        routerProvider.overrideWithValue(router),
        authControllerProvider.overrideWith(() => FakeAuthController(auth)),
      ],
      child: MaterialApp.router(
        theme: AppTheme.light(),
        routerConfig: router,
        builder: (context, child) => AssistantLauncher(child: child!),
      ),
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('assistant shows donor suggestions and renders a reply', (
    tester,
  ) async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onPost(
      '/assistant/chat',
      (server) => server.reply(200, {
        'reply': 'Your account is active.',
        'segments': [
          {'type': 'account', 'text': 'Your account is active.', 'sources': []},
        ],
        'actions': [
          {'label': 'Unsupported place', 'route': '/unsafe'},
        ],
      }),
      data: {'message': 'What is my donation status?', 'history': []},
    );
    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          ...api.overrides,
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
        ],
        child: const MaterialApp(home: Scaffold(body: AssistantSheet())),
      ),
    );

    expect(find.text('Can I donate after a tattoo?'), findsOneWidget);
    await tester.tap(find.text('What is my donation status?'));
    await tester.pumpAndSettle();

    expect(find.text('Your account'), findsOneWidget);
    expect(find.text('Your account is active.'), findsOneWidget);
    expect(find.text('Unsupported place'), findsOneWidget);
    expect(
      find.widgetWithText(OutlinedButton, 'Unsupported place'),
      findsNothing,
    );
  });

  test('assistant action routes are allow-listed and translated', () {
    expect(
      mobileRouteForAssistantAction('/doctor/screenings'),
      '/doctor/reports',
    );
    expect(
      mobileRouteForAssistantAction('/hospital/requests/verify'),
      '/hospital/verify-requests',
    );
    expect(mobileRouteForAssistantAction('/arbitrary'), isNull);
  });

  for (final role in const [
    ['User'],
    ['HospitalStaff'],
    ['Doctor'],
    ['Admin'],
  ]) {
    testWidgets('launcher is visible for active ${role.single}', (
      tester,
    ) async {
      await pumpLauncher(
        tester,
        auth: AuthState.signedIn(
          testUser(
            roles: role,
            hospitalApprovalStatus: role.single == 'HospitalStaff'
                ? 'Approved'
                : null,
          ),
        ),
      );
      expect(find.byKey(const Key('assistant-launcher')), findsOneWidget);
    });
  }

  testWidgets('launcher opens the existing assistant sheet', (tester) async {
    await pumpLauncher(tester, auth: AuthState.signedIn(testUser()));

    await tester.tap(find.byKey(const Key('assistant-launcher')));
    await tester.pumpAndSettle();

    expect(find.text('LifeLink Assistant'), findsOneWidget);
    expect(find.byKey(const Key('assistant-input')), findsOneWidget);
  });

  testWidgets('conversation survives closing and reopening the sheet', (
    tester,
  ) async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onPost(
      '/assistant/chat',
      (server) => server.reply(200, {'reply': 'Still here.'}),
      data: {'message': 'Remember this', 'history': []},
    );
    await pumpLauncher(
      tester,
      auth: AuthState.signedIn(testUser()),
      overrides: api.overrides,
    );

    await tester.tap(find.byKey(const Key('assistant-launcher')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('assistant-input')),
      'Remember this',
    );
    await tester.tap(find.byTooltip('Send'));
    await tester.pumpAndSettle();
    expect(find.text('Still here.'), findsOneWidget);

    await tester.tap(find.byTooltip('Close assistant'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('assistant-launcher')));
    await tester.pumpAndSettle();

    expect(find.text('Remember this'), findsOneWidget);
    expect(find.text('Still here.'), findsOneWidget);
  });

  testWidgets('assistant input clears a three-button navigation inset', (
    tester,
  ) async {
    await pumpLauncher(
      tester,
      auth: AuthState.signedIn(testUser()),
      textScale: 2,
      safeBottom: 48,
    );

    await tester.tap(find.byKey(const Key('assistant-launcher')));
    await tester.pumpAndSettle();
    final input = find.byKey(const Key('assistant-input'));
    expect(input, findsOneWidget);
    expect(tester.getRect(input).bottom, lessThanOrEqualTo(780 - 48));
    expect(tester.takeException(), isNull);
  });

  for (final testCase in const [
    (
      label: '360x780, default text, button navigation',
      size: Size(360, 780),
      textScale: 1.0,
      safeBottom: 0.0,
      navigationHeight: 96.0,
    ),
    (
      label: '360x780, large text, gesture navigation',
      size: Size(360, 780),
      textScale: 2.0,
      safeBottom: 24.0,
      navigationHeight: 104.0,
    ),
    (
      label: '412x915, default text, gesture navigation',
      size: Size(412, 915),
      textScale: 1.0,
      safeBottom: 24.0,
      navigationHeight: 92.0,
    ),
  ]) {
    testWidgets('launcher clears measured NavigationBar at ${testCase.label}', (
      tester,
    ) async {
      await pumpLauncher(
        tester,
        auth: AuthState.signedIn(testUser()),
        path: AppRoutes.donorHome,
        bottomNavigation: true,
        logicalSize: testCase.size,
        textScale: testCase.textScale,
        safeBottom: testCase.safeBottom,
        navigationHeight: testCase.navigationHeight,
      );

      final assistant = tester.getRect(
        find.byKey(const Key('assistant-launcher')),
      );
      final navigation = tester.getRect(find.byType(NavigationBar));
      expect(assistant.bottom, lessThanOrEqualTo(navigation.top - 16));
      expect(assistant.left, greaterThanOrEqualTo(0));
      expect(assistant.right, lessThanOrEqualTo(testCase.size.width));
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('launcher remeasures the bottom bar after a pushed route pops', (
    tester,
  ) async {
    tester.view.devicePixelRatio = 3;
    tester.view.physicalSize = const Size(1080, 2340);
    addTearDown(tester.view.reset);

    final router = GoRouter(
      navigatorKey: rootNavigatorKey,
      initialLocation: AppRoutes.donorHome,
      routes: [
        ShellRoute(
          builder: (context, state, child) => Scaffold(
            body: child,
            bottomNavigationBar: BottomNavigationBarSizeReporter(
              child: NavigationBar(
                height: 104,
                destinations: const [
                  NavigationDestination(
                    icon: Icon(Icons.home_outlined),
                    label: 'Home',
                  ),
                  NavigationDestination(icon: Icon(Icons.menu), label: 'More'),
                ],
              ),
            ),
          ),
          routes: [
            GoRoute(
              path: AppRoutes.donorHome,
              builder: (context, state) => Center(
                child: FilledButton(
                  onPressed: () => context.push(AppRoutes.profile),
                  child: const Text('Open profile'),
                ),
              ),
            ),
          ],
        ),
        GoRoute(
          path: AppRoutes.profile,
          parentNavigatorKey: rootNavigatorKey,
          builder: (context, state) => Scaffold(
            appBar: AppBar(title: Text('Profile')),
            body: const SizedBox.shrink(),
          ),
        ),
      ],
    );
    addTearDown(router.dispose);

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          routerProvider.overrideWithValue(router),
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
        ],
        child: MaterialApp.router(
          theme: AppTheme.light(),
          routerConfig: router,
          builder: (context, child) => AssistantLauncher(child: child!),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('Open profile'));
    await tester.pumpAndSettle();
    expect(find.text('Profile'), findsOneWidget);

    router.pop();
    await tester.pumpAndSettle();

    final assistant = tester.getRect(
      find.byKey(const Key('assistant-launcher')),
    );
    final navigation = tester.getRect(find.byType(NavigationBar));
    expect(assistant.bottom, lessThanOrEqualTo(navigation.top - 16));
    expect(tester.takeException(), isNull);
  });

  testWidgets('launcher stacks 12px above a page FAB and measured bar', (
    tester,
  ) async {
    await pumpLauncher(
      tester,
      auth: AuthState.signedIn(
        testUser(
          roles: const ['HospitalStaff'],
          hospitalApprovalStatus: 'Approved',
        ),
      ),
      path: AppRoutes.hospitalTransfers,
      primaryFab: true,
      bottomNavigation: true,
      logicalSize: const Size(412, 915),
      textScale: 2,
      safeBottom: 24,
      navigationHeight: 104,
    );

    final assistant = tester.getRect(
      find.byKey(const Key('assistant-launcher')),
    );
    final primary = tester.getRect(find.byKey(const Key('primary-fab')));
    final navigation = tester.getRect(find.byType(NavigationBar));
    expect(assistant.bottom, lessThanOrEqualTo(primary.top - 12));
    expect(primary.bottom, lessThan(navigation.top));
    expect(assistant.left, greaterThanOrEqualTo(0));
    expect(tester.takeException(), isNull);

    final scroll = tester.widget<SingleChildScrollView>(
      find.byType(SingleChildScrollView),
    );
    expect(scroll.padding, const EdgeInsets.only(bottom: 64));
  });

  for (final testCase in const [
    (size: Size(360, 780), textScale: 1.0, safeBottom: 0.0),
    (size: Size(412, 915), textScale: 2.0, safeBottom: 24.0),
  ]) {
    testWidgets(
      'pushed-screen launcher clears only safe area at ${testCase.size}',
      (tester) async {
        await pumpLauncher(
          tester,
          auth: AuthState.signedIn(testUser()),
          path: AppRoutes.profile,
          logicalSize: testCase.size,
          textScale: testCase.textScale,
          safeBottom: testCase.safeBottom,
        );

        final assistant = tester.getRect(
          find.byKey(const Key('assistant-launcher')),
        );
        expect(
          testCase.size.height - assistant.bottom,
          testCase.safeBottom + 16,
        );
      },
    );
  }

  final hiddenCases = <(String, AuthState, String)>[
    ('signed out', const AuthState.signedOut(), AppRoutes.donorHome),
    (
      'suspended',
      AuthState.signedIn(testUser(suspended: true)),
      AppRoutes.governanceStatus,
    ),
    ('an auth page', AuthState.signedIn(testUser()), AppRoutes.login),
    (
      'forced password change',
      AuthState.signedIn(
        testUser(roles: const ['Doctor'], mustChangePassword: true),
      ),
      AppRoutes.changePassword,
    ),
    (
      'hospital approval waiting',
      AuthState.signedIn(
        testUser(
          roles: const ['HospitalStaff'],
          hospitalApprovalStatus: 'Pending',
        ),
      ),
      AppRoutes.waitingApproval,
    ),
  ];
  for (final (label, auth, path) in hiddenCases) {
    testWidgets('launcher is hidden while $label', (tester) async {
      await pumpLauncher(tester, auth: auth, path: path);
      expect(find.byKey(const Key('assistant-launcher')), findsNothing);
    });
  }

  testWidgets('launcher is hidden while the keyboard is open', (tester) async {
    tester.view.viewInsets = const FakeViewPadding(bottom: 300);
    addTearDown(() => tester.view.viewInsets = const FakeViewPadding());
    await pumpLauncher(tester, auth: AuthState.signedIn(testUser()));
    expect(find.byKey(const Key('assistant-launcher')), findsNothing);
  });
}
