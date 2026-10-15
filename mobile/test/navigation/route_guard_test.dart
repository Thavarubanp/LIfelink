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
import 'package:lifelink_mobile/features/admin/directory/directory.dart';
import 'package:lifelink_mobile/features/attention/tab_attention_controller.dart';
import 'package:lifelink_mobile/features/analysis/analysis_repository.dart';
import 'package:lifelink_mobile/features/auth/hospital_registration_repository.dart';
import 'package:lifelink_mobile/features/governance/governance_repository.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_models.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_repository.dart';
import 'package:lifelink_mobile/features/doctors/doctor_repository.dart';
import 'package:lifelink_mobile/features/doctor/screening_models.dart';
import 'package:lifelink_mobile/features/emergencies/emergency_repository.dart';
import 'package:lifelink_mobile/features/inventory/inventory_repository.dart';
import 'package:lifelink_mobile/features/profile/profile_repository.dart';
import 'package:lifelink_mobile/features/screening/screening_repository.dart';

import 'package:lifelink_mobile/features/verification/verification_repository.dart';

import '../helpers.dart';

AuthState signedIn({
  List<String> roles = const ['User'],
  bool suspended = false,
  bool mustChangePassword = false,
  String? approval,
}) => AuthState.signedIn(
  testUser(
    roles: roles,
    suspended: suspended,
    mustChangePassword: mustChangePassword,
    hospitalApprovalStatus: approval,
  ),
);

class _FakeAnalysisRepository extends AnalysisRepository {
  _FakeAnalysisRepository() : super(MockedApi().client);

  @override
  Future<AnalysisStatus> status({bool background = false}) async =>
      AnalysisStatus(
        state: 'Idle',
        cooldownEndsAt: null,
        serverNow: DateTime.utc(2026, 10, 15),
        lastRun: null,
        nextScheduledAt: null,
        scheduleEnabled: false,
        cooldownSeconds: 120,
      );
}

class _FakeTabAttention extends TabAttentionController {
  _FakeTabAttention(this.value);

  final TabAttentionState value;

  @override
  TabAttentionState build() => value;

  @override
  Future<void> refresh() async {}

  @override
  Future<void> markSeen(TabAttentionKey? key) async {}
}

void main() {
  group('resolveRedirect (same order as the web ProtectedRoute)', () {
    test('checking the session: splash only', () {
      expect(
        resolveRedirect(const AuthState.unknown(), AppRoutes.hospitalHome),
        AppRoutes.splash,
      );
      expect(
        resolveRedirect(const AuthState.unknown(), AppRoutes.splash),
        isNull,
      );
      expect(
        resolveRedirect(const AuthState.unreachable('x'), AppRoutes.login),
        AppRoutes.splash,
      );
    });

    test('signed out: public screens only', () {
      const out = AuthState.signedOut();
      expect(
        resolveRedirect(out, AppRoutes.hospitalInventory),
        AppRoutes.login,
      );
      expect(resolveRedirect(out, AppRoutes.register), isNull);
      expect(resolveRedirect(out, AppRoutes.registerHospital), isNull);
      expect(resolveRedirect(out, AppRoutes.forgotPassword), isNull);
      expect(resolveRedirect(out, AppRoutes.waitingApproval), isNull);
    });

    test('signed in: sign-in screens lead to the role home', () {
      expect(resolveRedirect(signedIn(), AppRoutes.login), AppRoutes.donorHome);
      expect(
        resolveRedirect(
          signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
          AppRoutes.splash,
        ),
        AppRoutes.hospitalHome,
      );
      expect(
        resolveRedirect(signedIn(roles: ['Doctor']), AppRoutes.splash),
        AppRoutes.doctorHome,
      );
      expect(
        resolveRedirect(signedIn(roles: ['Admin', 'User']), AppRoutes.splash),
        AppRoutes.adminHome,
      );
    });

    test(
      'suspended donor-capable accounts only gain narrow withdrawal access',
      () {
        final s = signedIn(suspended: true);
        expect(
          resolveRedirect(s, AppRoutes.donorHome),
          AppRoutes.governanceStatus,
        );
        expect(
          resolveRedirect(s, AppRoutes.notifications),
          AppRoutes.governanceStatus,
        );
        expect(resolveRedirect(s, AppRoutes.governanceStatus), isNull);
        expect(resolveRedirect(s, AppRoutes.suspendedWithdrawals), isNull);
        expect(
          resolveRedirect(
            signedIn(roles: ['Admin'], suspended: true),
            AppRoutes.suspendedWithdrawals,
          ),
          isNull,
        );
        expect(
          resolveRedirect(
            signedIn(roles: ['Doctor'], suspended: true),
            AppRoutes.suspendedWithdrawals,
          ),
          AppRoutes.governanceStatus,
        );
        expect(
          resolveRedirect(
            signedIn(roles: ['HospitalStaff'], suspended: true),
            AppRoutes.suspendedWithdrawals,
          ),
          AppRoutes.governanceStatus,
        );
      },
    );

    test(
      'hospital staff of an unapproved hospital only see the waiting screen',
      () {
        final s = signedIn(roles: ['HospitalStaff'], approval: 'Pending');
        expect(
          resolveRedirect(s, AppRoutes.hospitalInventory),
          AppRoutes.waitingApproval,
        );
        expect(resolveRedirect(s, AppRoutes.waitingApproval), isNull);
        expect(
          resolveRedirect(
            signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
            AppRoutes.waitingApproval,
          ),
          AppRoutes.hospitalHome,
        );
      },
    );

    test('a doctor with a temporary password must change it first', () {
      final s = signedIn(roles: ['Doctor'], mustChangePassword: true);
      expect(
        resolveRedirect(s, AppRoutes.doctorHome),
        AppRoutes.changePassword,
      );
      expect(resolveRedirect(s, AppRoutes.changePassword), isNull);
    });

    test('Step 3 routes: doctors and hospital staff only', () {
      final doctor = signedIn(roles: ['Doctor']);
      final staff = signedIn(roles: ['HospitalStaff'], approval: 'Approved');
      expect(resolveRedirect(doctor, AppRoutes.doctorReport('a1')), isNull);
      expect(
        resolveRedirect(doctor, AppRoutes.doctorRequestDonors('r1')),
        isNull,
      );
      expect(
        resolveRedirect(doctor, AppRoutes.hospitalVerifyRequests),
        AppRoutes.doctorHome,
      );
      expect(resolveRedirect(staff, AppRoutes.hospitalVerifyRequests), isNull);
      expect(
        resolveRedirect(staff, AppRoutes.hospitalRequestDonors('r1')),
        isNull,
      );
      expect(
        resolveRedirect(staff, AppRoutes.doctorReport('a1')),
        AppRoutes.hospitalHome,
      );
      expect(
        resolveRedirect(signedIn(), AppRoutes.hospitalDoctors),
        AppRoutes.donorHome,
      );
    });

    test('wrong role goes to the own home', () {
      expect(
        resolveRedirect(signedIn(), AppRoutes.hospitalInventory),
        AppRoutes.donorHome,
      );
      expect(
        resolveRedirect(
          signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
          AppRoutes.adminHome,
        ),
        AppRoutes.hospitalHome,
      );
      expect(
        resolveRedirect(
          signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
          AppRoutes.hospitalPacket('p1'),
        ),
        isNull,
      );
      // Hospitals may open the (Step 2) create-request screen, like on the web
      expect(
        resolveRedirect(
          signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
          AppRoutes.createRequest,
        ),
        isNull,
      );
      expect(
        resolveRedirect(
          signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
          AppRoutes.donorComplaints,
        ),
        isNull,
      );
      expect(
        resolveRedirect(signedIn(roles: ['Admin']), AppRoutes.donorRequests),
        isNull,
      );
      expect(
        resolveRedirect(signedIn(roles: ['Admin']), AppRoutes.donorAcceptances),
        isNull,
      );
      expect(
        resolveRedirect(signedIn(roles: ['Admin']), AppRoutes.screening('a1')),
        isNull,
      );
      expect(
        resolveRedirect(signedIn(roles: ['Admin']), AppRoutes.adminCreate),
        isNull,
      );
      expect(
        resolveRedirect(
          signedIn(roles: ['Admin']),
          AppRoutes.adminScreening('a1'),
        ),
        isNull,
      );
      expect(
        resolveRedirect(signedIn(), AppRoutes.adminAcceptances),
        AppRoutes.donorHome,
      );
      expect(
        resolveRedirect(signedIn(roles: ['Doctor']), AppRoutes.donorRequests),
        AppRoutes.doctorHome,
      );
    });
  });

  group('router', () {
    Future<ProviderContainer> pumpRouter(
      WidgetTester tester,
      AuthState state, {
      String? initialPath,
      TabAttentionState tabAttention = const TabAttentionState(
        donorCreate: 11,
        donorDonations: 12,
        hospitalHome: 21,
        hospitalInventory: 22,
        hospitalTransfers: 23,
        doctorRequests: 31,
        doctorReports: 32,
      ),
    }) async {
      // A phone screen (360 x 780 dp): bottom navigation, not the tablet rail
      tester.view.physicalSize = const Size(1080, 2340);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      final container = ProviderContainer(
        overrides: [
          authControllerProvider.overrideWith(() => FakeAuthController(state)),
          unreadCountProvider.overrideWith(FakeUnreadCount.new),
          adminAttentionProvider.overrideWith(FakeAttention.new),
          tabAttentionProvider.overrideWith(
            () => _FakeTabAttention(tabAttention),
          ),
          adminStatsProvider.overrideWith(
            (ref) async => AdminStats.fromJson({'totalHospitals': 1}),
          ),
          assignedRequestsProvider.overrideWith((ref) async => []),
          screeningReportsProvider.overrideWith((ref) async => []),
          adminDoctorsProvider.overrideWith((ref) async => []),
          hospitalDoctorsProvider.overrideWith((ref) async => []),
          myHospitalProvider.overrideWith(
            (ref) async => const HospitalProfile(
              hospitalId: 'h1',
              name: 'Central Hospital',
              address: '1 Main Street',
              city: 'Colombo',
              contactNumber: '0112345678',
              email: 'hospital@example.test',
              isVerified: true,
              doctorCount: 0,
              packetShelfLifeDays: 35,
              expiryAlertDays: 7,
            ),
          ),
          hospitalInventoryProvider.overrideWith((ref) async => []),
          criticalEmergenciesProvider.overrideWith((ref) async => []),
          analysisRepositoryProvider.overrideWithValue(
            _FakeAnalysisRepository(),
          ),
          governanceStatusProvider.overrideWith(
            (ref) async => GovernanceStatus.fromJson({
              'isSuspended': state.user?.isSuspended ?? false,
              'suspendedEntity': 'User',
              'profile': {'name': 'Test Person'},
            }),
          ),
          donorHomeProvider.overrideWith(
            (ref) async => DonorHomeData(
              requests: const [],
              acceptances: const [],
              profile: DonorProfileSummary.fromJson({}),
            ),
          ),
          publicBloodRequestsProvider.overrideWith(
            (ref, filter) async => const [],
          ),
          hospitalChoicesProvider.overrideWith(
            (ref) async => const [
              HospitalChoice(
                id: 'h1',
                name: 'Central Hospital',
                address: 'Colombo',
                email: 'hospital@example.test',
              ),
            ],
          ),
          myBloodRequestsProvider.overrideWith((ref) async => const []),
          myAcceptancesProvider.overrideWith((ref) async => const []),
          hospitalRegistrationRepositoryProvider.overrideWithValue(
            FakeHospitalRegistrationRepository(
              HospitalRegistration.fromJson(hospitalRegistrationJson()),
            ),
          ),
        ],
        retry: (_, _) => null,
      );
      addTearDown(container.dispose);
      final router = container.read(routerProvider);
      if (initialPath != null) router.go(initialPath);
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp.router(
            theme: AppTheme.light(),
            routerConfig: router,
          ),
        ),
      );
      await tester.pumpAndSettle();
      return container;
    }

    testWidgets('signed out opens the sign-in screen', (tester) async {
      await pumpRouter(tester, const AuthState.signedOut());
      expect(find.text('Sign in to continue'), findsOneWidget);
    });

    testWidgets('signed out can choose user registration from sign in', (
      tester,
    ) async {
      await pumpRouter(tester, const AuthState.signedOut());

      await tester.tap(find.byKey(const Key('login-create-account')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('register-user-option')));
      await tester.pumpAndSettle();

      expect(find.text('Donor / patient account'), findsOneWidget);
      expect(find.byKey(const Key('reg-submit')), findsOneWidget);
    });

    testWidgets('signed out can choose hospital registration from sign in', (
      tester,
    ) async {
      await pumpRouter(tester, const AuthState.signedOut());

      await tester.tap(find.byKey(const Key('login-create-account')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('register-hospital-option')));
      await tester.pumpAndSettle();

      expect(
        find.text('Sri Lanka Healthcare Facility Registration'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('hospital-register-submit')), findsOneWidget);
    });

    testWidgets('signed out can open public hospital registration', (
      tester,
    ) async {
      final container = await pumpRouter(tester, const AuthState.signedOut());
      container.read(routerProvider).go(AppRoutes.registerHospital);
      await tester.pumpAndSettle();

      expect(
        find.text('Sri Lanka Healthcare Facility Registration'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('hospital-register-submit')), findsOneWidget);
    });

    testWidgets('suspended account lands on the governance status', (
      tester,
    ) async {
      await pumpRouter(tester, signedIn(suspended: true));
      expect(find.text('Your account is suspended'), findsOneWidget);
    });

    testWidgets('unapproved hospital lands on the waiting screen', (
      tester,
    ) async {
      await pumpRouter(
        tester,
        signedIn(roles: ['HospitalStaff'], approval: 'Pending'),
      );
      expect(find.text('Pending Administrator Review'), findsOneWidget);
    });

    testWidgets('doctor with a temporary password lands on change password', (
      tester,
    ) async {
      await pumpRouter(
        tester,
        signedIn(roles: ['Doctor'], mustChangePassword: true),
      );
      expect(find.text('Temporary password'), findsOneWidget);
    });

    testWidgets('donor badges appear only on Create and Donations', (
      tester,
    ) async {
      await pumpRouter(tester, signedIn(), initialPath: AppRoutes.donorHome);
      expect(find.text('11'), findsOneWidget);
      expect(find.text('12'), findsOneWidget);
      expect(find.text('21'), findsNothing);
    });

    testWidgets('hospital badges appear on Home, Inventory and Transfers', (
      tester,
    ) async {
      await pumpRouter(
        tester,
        signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
        initialPath: AppRoutes.hospitalHome,
      );
      for (final count in ['21', '22', '23']) {
        expect(find.text(count), findsOneWidget);
      }
      expect(find.text('11'), findsNothing);
    });

    testWidgets('hospital More keeps every entry except Scan packet', (
      tester,
    ) async {
      await pumpRouter(
        tester,
        signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
        initialPath: AppRoutes.hospitalMore,
      );

      for (final label in [
        'Emergencies',
        'Create blood request',
        'Complaints',
        'Donate blood',
        'Recommendations',
        'Verify requests',
      ]) {
        expect(find.text(label), findsOneWidget);
      }
      expect(find.text('Scan packet'), findsNothing);
    });

    testWidgets('doctor tabs fit at 360px with badges on the right branches', (
      tester,
    ) async {
      await pumpRouter(
        tester,
        signedIn(roles: ['Doctor']),
        initialPath: AppRoutes.doctorHome,
      );
      final navigation = tester.widget<NavigationBar>(
        find.byType(NavigationBar),
      );
      expect(navigation.selectedIndex, 0);
      expect(
        navigation.destinations.map(
          (destination) => (destination as NavigationDestination).label,
        ),
        ['Home', 'Requests', 'Reports', 'More'],
      );
      expect(find.text('31'), findsOneWidget);
      expect(find.text('32'), findsOneWidget);
      expect(find.text('21'), findsNothing);
      expect(
        find.descendant(
          of: find.widgetWithText(NavigationDestination, 'Requests'),
          matching: find.text('31'),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: find.widgetWithText(NavigationDestination, 'Reports'),
          matching: find.text('32'),
        ),
        findsOneWidget,
      );
      expect(find.text('Doctor dashboard'), findsOneWidget);
      expect(tester.takeException(), isNull);
    });

    testWidgets(
      'hospital Doctors deep link selects branch 3 without a duplicate app bar',
      (tester) async {
        await pumpRouter(
          tester,
          signedIn(roles: ['HospitalStaff'], approval: 'Approved'),
          initialPath: AppRoutes.hospitalDoctors,
        );

        final navigation = tester.widget<NavigationBar>(
          find.byType(NavigationBar),
        );
        expect(navigation.selectedIndex, 3);
        for (final tab in [
          'Home',
          'Inventory',
          'Transfers',
          'Doctors',
          'More',
        ]) {
          expect(
            find.descendant(
              of: find.byType(NavigationBar),
              matching: find.text(tab),
            ),
            findsOneWidget,
          );
        }
        expect(find.byType(AppBar), findsOneWidget);
        expect(
          find.text('Search name, email, SLMC no. or specialization'),
          findsOneWidget,
        );

        await tester.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('More'),
          ),
        );
        await tester.pumpAndSettle();
        expect(
          tester
              .widget<NavigationBar>(find.byType(NavigationBar))
              .selectedIndex,
          4,
        );
        expect(find.widgetWithText(ListTile, 'Doctors'), findsNothing);

        await tester.tap(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('Doctors'),
          ),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.bySemanticsLabel('LifeLink, go to home'));
        await tester.pump();
        expect(
          tester
              .widget<NavigationBar>(find.byType(NavigationBar))
              .selectedIndex,
          0,
        );
      },
    );

    testWidgets('doctor lands on Home and the logo returns there', (
      tester,
    ) async {
      await pumpRouter(tester, signedIn(roles: ['Doctor']));
      expect(find.text('Doctor dashboard'), findsOneWidget);
      for (final tab in ['Home', 'Requests', 'Reports', 'More']) {
        expect(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text(tab),
          ),
          findsOneWidget,
        );
      }
      await tester.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('Requests'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Assigned requests'), findsOneWidget);
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        1,
      );

      await tester.tap(find.bySemanticsLabel('LifeLink, go to home'));
      await tester.pumpAndSettle();
      expect(find.text('Doctor dashboard'), findsOneWidget);
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        0,
      );
    });

    testWidgets('doctor dashboard cards open visibly selected filters', (
      tester,
    ) async {
      await pumpRouter(tester, signedIn(roles: ['Doctor']));

      await tester.tap(find.byKey(const Key('doctor-stat-assigned')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<ChoiceChip>(find.widgetWithText(ChoiceChip, 'Awaiting you'))
            .selected,
        isTrue,
      );

      await tester.tap(find.bySemanticsLabel('LifeLink, go to home'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('doctor-stat-screened')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<ChoiceChip>(
              find.widgetWithText(ChoiceChip, 'All screened (0)'),
            )
            .selected,
        isTrue,
      );

      await tester.tap(find.bySemanticsLabel('LifeLink, go to home'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('doctor-stat-pending')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<ChoiceChip>(
              find.widgetWithText(ChoiceChip, 'Waiting for review (0)'),
            )
            .selected,
        isTrue,
      );

      await tester.tap(find.bySemanticsLabel('LifeLink, go to home'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('doctor-stat-awaiting')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<ChoiceChip>(
              find.widgetWithText(
                ChoiceChip,
                'Approved – awaiting donation (0)',
              ),
            )
            .selected,
        isTrue,
      );
    });

    testWidgets('admin five-tab shell reuses donor screens without duplicates', (
      tester,
    ) async {
      await pumpRouter(tester, signedIn(roles: ['Admin']));
      expect(find.text('Administration'), findsOneWidget);
      for (final tab in ['Home', 'Attention', 'Create', 'Activity', 'More']) {
        expect(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text(tab),
          ),
          findsOneWidget,
        );
      }
      expect(tester.takeException(), isNull);

      await tester.tap(find.byKey(const Key('admin-directory-Doctors')));
      await tester.pumpAndSettle();
      expect(
        find.text(
          'Read-only directory. Doctor accounts are managed by their hospital.',
        ),
        findsOneWidget,
      );
      await tester.tap(find.byType(BackButton));
      await tester.pumpAndSettle();
      expect(find.text('Administration'), findsOneWidget);

      await tester.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('Create'),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        2,
      );
      expect(find.text('Create and donate'), findsOneWidget);
      expect(find.byKey(const Key('admin-create-request')), findsOneWidget);
      expect(find.byKey(const Key('admin-my-acceptances')), findsOneWidget);
      expect(
        find.text('No available requests match these filters.'),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);

      await tester.tap(find.byKey(const Key('admin-my-acceptances')));
      await tester.pumpAndSettle();
      expect(find.text('My donations'), findsOneWidget);
      await tester.tap(find.text('Browse requests'));
      await tester.pumpAndSettle();
      expect(find.text('Create and donate'), findsOneWidget);
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        2,
      );

      await tester.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('More'),
        ),
      );
      await tester.pumpAndSettle();
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        4,
      );
      for (final removed in [
        'Donate blood',
        'My acceptances',
        'Create blood request',
      ]) {
        expect(find.widgetWithText(ListTile, removed), findsNothing);
      }
      expect(find.widgetWithText(ListTile, 'Users'), findsOneWidget);
      expect(find.widgetWithText(ListTile, 'Hospitals'), findsOneWidget);

      await tester.tap(find.bySemanticsLabel('LifeLink, go to home'));
      await tester.pumpAndSettle();
      expect(
        tester.widget<NavigationBar>(find.byType(NavigationBar)).selectedIndex,
        0,
      );
    });

    testWidgets('donor lands on the implemented donor home shell', (
      tester,
    ) async {
      await pumpRouter(tester, signedIn());
      expect(find.byType(NavigationBar), findsOneWidget);
      expect(find.text('Your donor dashboard'), findsOneWidget);
      for (final tab in ['Home', 'Requests', 'Create', 'Donations', 'More']) {
        expect(
          find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text(tab),
          ),
          findsOneWidget,
        );
      }
      await tester.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('Requests'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Available requests'), findsOneWidget);
      await tester.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('Create'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Create blood request'), findsOneWidget);
      expect(find.text('New blood request'), findsOneWidget);
      expect(find.text('My requests'), findsOneWidget);
      await tester.tap(
        find.descendant(
          of: find.byType(NavigationBar),
          matching: find.text('Donations'),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('My donations'), findsOneWidget);
    });
  });
}
