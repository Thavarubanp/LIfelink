import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/core/routing/app_router.dart';
import 'package:lifelink_mobile/core/routing/routes.dart';
import 'package:lifelink_mobile/core/theme/app_theme.dart';
import 'package:lifelink_mobile/features/notifications/notifications_repository.dart';

import 'package:lifelink_mobile/features/admin/admin_repository.dart';
import 'package:lifelink_mobile/features/admin/attention_controller.dart';
import 'package:lifelink_mobile/features/governance/governance_repository.dart';

import '../helpers.dart';

AuthState signedIn({
  List<String> roles = const ['User'],
  bool suspended = false,
  bool mustChangePassword = false,
  String? approval,
}) =>
    AuthState.signedIn(testUser(roles: roles, suspended: suspended, mustChangePassword: mustChangePassword, hospitalApprovalStatus: approval));

void main() {
  group('resolveRedirect (same order as the web ProtectedRoute)', () {
    test('checking the session: splash only', () {
      expect(resolveRedirect(const AuthState.unknown(), AppRoutes.hospitalHome), AppRoutes.splash);
      expect(resolveRedirect(const AuthState.unknown(), AppRoutes.splash), isNull);
      expect(resolveRedirect(const AuthState.unreachable('x'), AppRoutes.login), AppRoutes.splash);
    });

    test('signed out: public screens only', () {
      const out = AuthState.signedOut();
      expect(resolveRedirect(out, AppRoutes.hospitalInventory), AppRoutes.login);
      expect(resolveRedirect(out, AppRoutes.register), isNull);
      expect(resolveRedirect(out, AppRoutes.forgotPassword), isNull);
    });

    test('signed in: sign-in screens lead to the role home', () {
      expect(resolveRedirect(signedIn(), AppRoutes.login), AppRoutes.donorHome);
      expect(resolveRedirect(signedIn(roles: ['HospitalStaff'], approval: 'Approved'), AppRoutes.splash), AppRoutes.hospitalHome);
      expect(resolveRedirect(signedIn(roles: ['Doctor']), AppRoutes.splash), AppRoutes.doctorHome);
      expect(resolveRedirect(signedIn(roles: ['Admin', 'User']), AppRoutes.splash), AppRoutes.adminHome);
    });

    test('suspended accounts only reach the governance status', () {
      final s = signedIn(suspended: true);
      expect(resolveRedirect(s, AppRoutes.donorHome), AppRoutes.governanceStatus);
      expect(resolveRedirect(s, AppRoutes.notifications), AppRoutes.governanceStatus);
      expect(resolveRedirect(s, AppRoutes.governanceStatus), isNull);
    });

    test('hospital staff of an unapproved hospital only see the waiting screen', () {
      final s = signedIn(roles: ['HospitalStaff'], approval: 'Pending');
      expect(resolveRedirect(s, AppRoutes.hospitalInventory), AppRoutes.waitingApproval);
      expect(resolveRedirect(s, AppRoutes.waitingApproval), isNull);
      expect(resolveRedirect(signedIn(roles: ['HospitalStaff'], approval: 'Approved'), AppRoutes.waitingApproval), AppRoutes.hospitalHome);
    });

    test('a doctor with a temporary password must change it first', () {
      final s = signedIn(roles: ['Doctor'], mustChangePassword: true);
      expect(resolveRedirect(s, AppRoutes.doctorHome), AppRoutes.changePassword);
      expect(resolveRedirect(s, AppRoutes.changePassword), isNull);
    });

    test('wrong role goes to the own home', () {
      expect(resolveRedirect(signedIn(), AppRoutes.hospitalInventory), AppRoutes.donorHome);
      expect(resolveRedirect(signedIn(roles: ['HospitalStaff'], approval: 'Approved'), AppRoutes.adminHome), AppRoutes.hospitalHome);
      expect(resolveRedirect(signedIn(roles: ['HospitalStaff'], approval: 'Approved'), AppRoutes.hospitalPacket('p1')), isNull);
      // Hospitals may open the (Step 2) create-request screen, like on the web
      expect(resolveRedirect(signedIn(roles: ['HospitalStaff'], approval: 'Approved'), AppRoutes.createRequest), isNull);
      expect(resolveRedirect(signedIn(roles: ['Doctor']), AppRoutes.donorRequests), AppRoutes.doctorHome);
    });
  });

  group('router', () {
    Future<void> pumpRouter(WidgetTester tester, AuthState state) async {
      // A phone screen (360 x 780 dp): bottom navigation, not the tablet rail
      tester.view.physicalSize = const Size(1080, 2340);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => FakeAuthController(state)),
          unreadCountProvider.overrideWith(FakeUnreadCount.new),
          adminAttentionProvider.overrideWith(FakeAttention.new),
          adminStatsProvider.overrideWith((ref) async => AdminStats.fromJson({'totalHospitals': 1})),
          governanceStatusProvider.overrideWith((ref) async => GovernanceStatus.fromJson({
                'isSuspended': state.user?.isSuspended ?? false,
                'suspendedEntity': 'User',
                'profile': {'name': 'Test Person'},
              })),
        ],
        retry: (_, _) => null,
      );
      addTearDown(container.dispose);
      await tester.pumpWidget(UncontrolledProviderScope(
        container: container,
        child: MaterialApp.router(theme: AppTheme.light(), routerConfig: container.read(routerProvider)),
      ));
      await tester.pumpAndSettle();
    }

    testWidgets('signed out opens the sign-in screen', (tester) async {
      await pumpRouter(tester, const AuthState.signedOut());
      expect(find.text('Sign in to continue'), findsOneWidget);
    });

    testWidgets('suspended account lands on the governance status', (tester) async {
      await pumpRouter(tester, signedIn(suspended: true));
      expect(find.text('Your account is suspended'), findsOneWidget);
    });

    testWidgets('unapproved hospital lands on the waiting screen', (tester) async {
      await pumpRouter(tester, signedIn(roles: ['HospitalStaff'], approval: 'Pending'));
      expect(find.text('Your hospital registration is waiting for the administrator.'), findsOneWidget);
    });

    testWidgets('doctor with a temporary password lands on change password', (tester) async {
      await pumpRouter(tester, signedIn(roles: ['Doctor'], mustChangePassword: true));
      expect(find.text('Temporary password'), findsOneWidget);
    });

    testWidgets('admin lands on the admin shell: Home, Attention, Activity, More', (tester) async {
      await pumpRouter(tester, signedIn(roles: ['Admin']));
      expect(find.text('Administration'), findsOneWidget);
      for (final tab in ['Home', 'Attention', 'Activity', 'More']) {
        expect(find.descendant(of: find.byType(NavigationBar), matching: find.text(tab)), findsOneWidget);
      }
    });

    testWidgets('donor lands on the donor shell with bottom navigation (Step 2 stubs)', (tester) async {
      await pumpRouter(tester, signedIn());
      expect(find.byType(NavigationBar), findsOneWidget);
      expect(find.text('Coming in Step 2'), findsOneWidget);
    });
  });
}
