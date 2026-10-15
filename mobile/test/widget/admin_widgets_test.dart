import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/admin/admin_home_screen.dart';
import 'package:lifelink_mobile/features/admin/admin_repository.dart';
import 'package:lifelink_mobile/features/admin/appeals/admin_appeals.dart';
import 'package:lifelink_mobile/features/admin/attention_controller.dart';
import 'package:lifelink_mobile/features/admin/complaints/admin_complaints.dart';
import 'package:lifelink_mobile/features/admin/directory/admin_actions.dart';
import 'package:lifelink_mobile/features/admin/directory/directory.dart';
import 'package:lifelink_mobile/features/admin/oversight/admin_activity_screen.dart';
import 'package:lifelink_mobile/features/admin/registrations/registration_detail_screen.dart';
import 'package:lifelink_mobile/features/admin/registrations/registration_repository.dart';
import 'package:lifelink_mobile/features/appeals/appeal_models.dart';
import 'package:lifelink_mobile/features/doctors/doctor_repository.dart';
import 'package:lifelink_mobile/features/governance/governance_repository.dart';
import 'package:lifelink_mobile/features/governance/governance_status_screen.dart';

import '../helpers.dart';

void phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.5;
  addTearDown(tester.view.reset);
}

void smallPhone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 2340);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
}

AdminUser adminUser({bool suspended = false, bool blocked = false}) =>
    AdminUser.fromJson({
      'userId': 'u1',
      'firstName': 'Nimal',
      'lastName': 'Perera',
      'email': 'nimal@example.test',
      'phoneNumber': '0771234567',
      'accountStatus': blocked
          ? 'Blocked'
          : suspended
          ? 'Suspended'
          : 'Active',
      'isSuspended': suspended,
      'isPermanentlyBlocked': blocked,
      'roles': ['User'],
    });

Future<MockedApi> pumpUserActions(
  WidgetTester tester, {
  bool suspended = false,
}) async {
  phone(tester);
  final api = MockedApi();
  await tester.pumpWidget(
    testApp(
      Consumer(
        builder: (context, ref, _) => Scaffold(
          body: Wrap(
            children: userAdminActions(
              context,
              ref,
              adminUser(suspended: suspended),
            ),
          ),
        ),
      ),
      overrides: [
        ...api.overrides,
        authControllerProvider.overrideWith(
          () => FakeAuthController(
            AuthState.signedIn(testUser(roles: const ['Admin'])),
          ),
        ),
        adminAttentionProvider.overrideWith(FakeAttention.new),
      ],
    ),
  );
  await tester.pumpAndSettle();
  return api;
}

void main() {
  testWidgets('admin home keeps its heading and nine statistics only', (
    tester,
  ) async {
    smallPhone(tester);
    await tester.pumpWidget(
      testApp(
        const AdminHomeScreen(),
        overrides: [
          adminStatsProvider.overrideWith(
            (ref) async => AdminStats.fromJson({
              'totalHospitals': 7,
              'pendingAppeals': 1,
              'activeSuspendedUsers': 5,
            }),
          ),
          adminAttentionProvider.overrideWith(FakeAttention.new),
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Hospitals'), findsOneWidget);
    expect(find.text('7'), findsOneWidget);
    expect(find.text('Suspended users'), findsOneWidget);
    expect(find.text('Administration'), findsOneWidget);
    expect(find.text('Platform statistics'), findsOneWidget);
    for (final label in [
      'Donors / patients',
      'Hospitals',
      'Doctors',
      'Active requests',
      'Pending complaints',
      'Pending registrations',
      'Pending appeals',
      'Suspended users',
      'Suspended hospitals',
    ]) {
      expect(find.text(label), findsOneWidget);
    }
    expect(find.text('Needs attention'), findsNothing);
    expect(find.byKey(const Key('attention-Complaints')), findsNothing);
    for (final label in [
      'Donors / patients',
      'Hospitals',
      'Doctors',
      'Active requests',
      'Pending complaints',
      'Pending registrations',
      'Pending appeals',
      'Suspended users',
      'Suspended hospitals',
    ]) {
      expect(find.byKey(Key('admin-directory-$label')), findsOneWidget);
      expect(
        tester.widget<InkWell>(find.byKey(Key('admin-directory-$label'))).onTap,
        isNotNull,
      );
    }
  });

  test('all statistics map to existing useful destinations', () {
    expect(adminStatisticDestination('Donors / patients'), '/admin/users');
    expect(adminStatisticDestination('Hospitals'), '/admin/hospitals');
    expect(adminStatisticDestination('Doctors'), '/admin/doctors');
    expect(
      adminStatisticDestination('Active requests'),
      '/admin/activity?tab=blood-requests&status=active',
    );
    expect(
      adminStatisticDestination('Pending complaints'),
      '/admin/complaints',
    );
    expect(
      adminStatisticDestination('Pending registrations'),
      '/admin/registrations',
    );
    expect(adminStatisticDestination('Pending appeals'), '/admin/appeals');
    expect(
      adminStatisticDestination('Suspended users'),
      '/admin/users?filter=suspended',
    );
    expect(
      adminStatisticDestination('Suspended hospitals'),
      '/admin/hospitals?filter=suspended',
    );
  });

  testWidgets('active-request destination visibly selects the Active filter', (
    tester,
  ) async {
    phone(tester);
    await tester.pumpWidget(
      testApp(
        const AdminActivityScreen(
          initialTab: 'blood-requests',
          initialStatus: 'active',
        ),
        overrides: [
          adminAttentionProvider.overrideWith(FakeAttention.new),
          oversightTabProvider.overrideWith(
            (ref, kind) async => (<OversightItem>[], null),
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();

    final active = tester.widget<ChoiceChip>(
      find.widgetWithText(ChoiceChip, 'Active'),
    );
    expect(active.selected, isTrue);
  });

  for (final width in [360.0, 900.0]) {
    testWidgets('statistics use a uniform overflow-safe grid at $width px', (
      tester,
    ) async {
      tester.view.devicePixelRatio = 3;
      tester.view.physicalSize = Size(width * 3, 2400);
      addTearDown(tester.view.reset);
      final labels = [
        'Donors / patients',
        'Hospitals',
        'Doctors',
        'Active requests',
        'Pending complaints',
        'Pending registrations with an exceptionally long label',
        'Pending appeals',
        'Suspended users',
        'Suspended hospitals',
      ];
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: SingleChildScrollView(
              child: StatGrid(
                stats: [
                  for (var i = 0; i < labels.length; i++)
                    (
                      labels[i],
                      i == 0 ? 999999999999 : i,
                      Icons.insights,
                      Colors.red,
                    ),
                ],
                actions: {for (final label in labels) label: () {}},
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();

      final rects = [
        for (final label in labels)
          tester.getRect(find.byKey(Key('admin-directory-$label'))),
      ];
      for (final rect in rects.skip(1)) {
        expect(rect.height, closeTo(rects.first.height, 0.001));
        expect(rect.width, closeTo(rects.first.width, 0.001));
      }
      expect(tester.takeException(), isNull);
    });
  }

  testWidgets('attention contains only its three reachable governance queues', (
    tester,
  ) async {
    smallPhone(tester);
    await tester.pumpWidget(
      testApp(
        const AttentionScreen(),
        overrides: [adminAttentionProvider.overrideWith(FakeAttention.new)],
      ),
    );
    await tester.pumpAndSettle();

    for (final label in ['Hospital registrations', 'Appeals', 'Complaints']) {
      expect(find.byKey(Key('attention-$label')), findsOneWidget);
    }
    expect(
      find.descendant(
        of: find.byKey(const Key('attention-Complaints')),
        matching: find.text('3'),
      ),
      findsOneWidget,
    );
    expect(find.text('New blood requests'), findsNothing);
    expect(find.text('New transfers'), findsNothing);
  });

  testWidgets('activity sub-tab badges clear on selection and fit at 360px', (
    tester,
  ) async {
    smallPhone(tester);
    await tester.pumpWidget(
      testApp(
        const AdminActivityScreen(initialTab: 'transfers'),
        overrides: [
          adminAttentionProvider.overrideWith(
            () => FakeAttention(
              const AdminAttention(newBloodRequests: 120, newTransfers: 120),
            ),
          ),
          oversightTabProvider.overrideWith(
            (ref, kind) async => (<OversightItem>[], null),
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Blood requests'), findsOneWidget);
    expect(find.text('Transfers'), findsOneWidget);
    expect(find.byTooltip('Directories'), findsNothing);
    expect(
      find.byKey(const Key('activity-tab-badge-blood-requests')),
      findsOneWidget,
    );
    expect(find.byKey(const Key('activity-tab-badge-transfers')), findsNothing);
    expect(tester.takeException(), isNull);

    await tester.tap(find.text('Blood requests'));
    await tester.pumpAndSettle();
    expect(
      find.byKey(const Key('activity-tab-badge-blood-requests')),
      findsNothing,
    );
    expect(
      find.byKey(const Key('activity-tab-badge-transfers')),
      findsOneWidget,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('user suspend requires a named reason confirmation', (
    tester,
  ) async {
    final api = await pumpUserActions(tester);
    api.adapter.onPut(
      '/Admin/users/u1/suspend',
      (server) => server.reply(200, {}),
      data: {'reason': 'Policy review', 'suspendedUntil': null},
    );

    await tester.tap(find.byKey(const Key('suspend-account')));
    await tester.pumpAndSettle();
    expect(find.text('Suspend Nimal Perera'), findsOneWidget);
    expect(api.sent.where((r) => r.path == '/Admin/users/u1/suspend'), isEmpty);
    await tester.enterText(
      find.byKey(const Key('suspend-reason')),
      'Policy review',
    );
    await tester.tap(find.byKey(const Key('suspend-submit')));
    await tester.pumpAndSettle();
    expect(
      api.sent.where((r) => r.path == '/Admin/users/u1/suspend'),
      hasLength(1),
    );
  });

  testWidgets('user block states permanence before calling the API', (
    tester,
  ) async {
    final api = await pumpUserActions(tester);
    api.adapter.onPut(
      '/Admin/users/u1/block',
      (server) => server.reply(200, {}),
    );

    await tester.tap(find.byKey(const Key('block-account')));
    await tester.pumpAndSettle();
    expect(find.text('Permanently block Nimal Perera?'), findsOneWidget);
    expect(find.textContaining('This cannot be undone.'), findsOneWidget);
    expect(api.sent.where((r) => r.path == '/Admin/users/u1/block'), isEmpty);
    await tester.tap(find.widgetWithText(FilledButton, 'Permanently block'));
    await tester.pumpAndSettle();
    expect(
      api.sent.where((r) => r.path == '/Admin/users/u1/block'),
      hasLength(1),
    );
  });

  testWidgets('promotion needs two named confirmations before transfer', (
    tester,
  ) async {
    final api = await pumpUserActions(tester);
    api.adapter.onPut(
      '/Admin/users/u1/promote',
      (server) => server.reply(200, {}),
    );

    await tester.tap(find.byKey(const Key('promote-account')));
    await tester.pumpAndSettle();
    expect(
      find.text('Transfer admin ownership to Nimal Perera?'),
      findsOneWidget,
    );
    expect(find.textContaining('sole admin ownership'), findsOneWidget);
    expect(find.textContaining('signed out immediately'), findsOneWidget);
    expect(api.sent.where((r) => r.path == '/Admin/users/u1/promote'), isEmpty);

    await tester.tap(find.widgetWithText(FilledButton, 'Continue'));
    await tester.pumpAndSettle();
    expect(find.text('Re-confirm ownership transfer'), findsOneWidget);
    expect(find.textContaining('Nimal Perera'), findsOneWidget);
    expect(api.sent.where((r) => r.path == '/Admin/users/u1/promote'), isEmpty);

    await tester.tap(
      find.widgetWithText(FilledButton, 'Transfer and sign out'),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
    expect(
      api.sent.where((r) => r.path == '/Admin/users/u1/promote'),
      hasLength(1),
    );
  });

  testWidgets('reinstate requires a named confirmation', (tester) async {
    final api = await pumpUserActions(tester, suspended: true);
    api.adapter.onPut(
      '/Admin/users/u1/reinstate',
      (server) => server.reply(200, {}),
    );

    await tester.tap(find.byKey(const Key('reinstate-account')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Reinstate Nimal Perera?'), findsOneWidget);
    expect(
      api.sent.where((r) => r.path == '/Admin/users/u1/reinstate'),
      isEmpty,
    );
    await tester.tap(find.widgetWithText(FilledButton, 'Reinstate'));
    await tester.pumpAndSettle();
    expect(
      api.sent.where((r) => r.path == '/Admin/users/u1/reinstate'),
      hasLength(1),
    );
  });

  testWidgets('admin doctors directory is read-only and hospital-managed', (
    tester,
  ) async {
    smallPhone(tester);
    await tester.pumpWidget(
      testApp(
        const AdminDoctorsScreen(),
        overrides: [
          adminDoctorsProvider.overrideWith(
            (ref) async => const [
              Doctor(
                doctorId: 'd1',
                hospitalId: 'h1',
                hospitalName: 'Fake General',
                userId: 'u2',
                firstName: 'Anjali',
                lastName: 'Silva',
                email: 'anjali@example.test',
                phoneNumber: '0770000000',
                licenseNumber: 'SLMC-123',
                specialization: 'Haematology',
                isActive: true,
                mustChangePassword: false,
                deletedAt: null,
                createdAt: null,
              ),
            ],
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Dr. Anjali Silva'), findsOneWidget);
    expect(find.textContaining('Managed by hospital'), findsWidgets);
    for (final key in [
      'suspend-account',
      'reinstate-account',
      'block-account',
      'promote-account',
      'send-message',
    ]) {
      expect(find.byKey(Key(key)), findsNothing);
    }
  });

  testWidgets('hospital actions remain message and suspend only', (
    tester,
  ) async {
    phone(tester);
    await tester.pumpWidget(
      testApp(
        Consumer(
          builder: (context, ref, _) => Scaffold(
            body: Wrap(
              children: hospitalAdminActions(
                context,
                ref,
                AdminHospital.fromJson({
                  'hospitalId': 'h1',
                  'name': 'Fake General',
                  'approvalStatus': 'Approved',
                  'isSuspended': false,
                }),
              ),
            ),
          ),
        ),
        overrides: [adminAttentionProvider.overrideWith(FakeAttention.new)],
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('send-message')), findsOneWidget);
    expect(find.byKey(const Key('suspend-account')), findsOneWidget);
    expect(find.byKey(const Key('block-account')), findsNothing);
    expect(find.byKey(const Key('promote-account')), findsNothing);
  });

  group('registration detail', () {
    Future<void> pump(WidgetTester tester, String status) async {
      phone(tester);
      await tester.pumpWidget(
        testApp(
          const RegistrationDetailScreen(hospitalId: 'h1'),
          overrides: [
            adminHospitalsProvider.overrideWith(
              (ref) async => [
                AdminHospital.fromJson({
                  'hospitalId': 'h1',
                  'name': 'Fake General',
                  'approvalStatus': status,
                  'approvalHistory': [
                    {
                      'id': 'e1',
                      'type': 'Submitted',
                      'fromAdmin': false,
                      'timestamp': '2026-10-05T04:00:00Z',
                    },
                  ],
                }),
              ],
            ),
            adminAttentionProvider.overrideWith(FakeAttention.new),
          ],
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('reject needs a reason of at least 3 characters', (
      tester,
    ) async {
      await pump(tester, 'Pending');
      expect(find.byKey(const Key('registration-approve')), findsOneWidget);
      expect(find.byKey(const Key('registration-comment')), findsNothing);
      await tester.enterText(find.byKey(const Key('registration-text')), 'no');
      await tester.ensureVisible(find.byKey(const Key('registration-reject')));
      await tester.tap(find.byKey(const Key('registration-reject')));
      await tester.pump();
      expect(
        find.text('Please give a rejection reason (at least 3 characters).'),
        findsOneWidget,
      );
    });

    testWidgets(
      'a rejected registration waits for the hospital: comment only',
      (tester) async {
        await pump(tester, 'Rejected');
        expect(find.byKey(const Key('registration-approve')), findsNothing);
        expect(find.byKey(const Key('registration-reject')), findsNothing);
        expect(find.byKey(const Key('registration-comment')), findsOneWidget);
      },
    );
  });

  testWidgets('appeal already rejected once: Reject is disabled', (
    tester,
  ) async {
    phone(tester);
    await tester.pumpWidget(
      testApp(
        const AdminAppealDetailScreen(appealId: 'a1'),
        overrides: [
          adminAppealsProvider.overrideWith(
            (ref, status) async => [
              Appeal.fromJson({
                'appealId': 'a1',
                'userEmail': 'donor@example.test',
                'reason': 'Please reinstate me',
                'status': 'REJECTED',
                'canReject': false,
              }),
            ],
          ),
          adminAttentionProvider.overrideWith(FakeAttention.new),
        ],
      ),
    );
    await tester.pumpAndSettle();
    final reject = tester.widget<ButtonStyleButton>(
      find.byKey(const Key('appeal-reject')),
    );
    expect(reject.onPressed, isNull);
    expect(find.byKey(const Key('appeal-approve')), findsOneWidget);
  });

  testWidgets('complaints: the "General question" filter', (tester) async {
    phone(tester);
    await tester.pumpWidget(
      testApp(
        const AdminComplaintsScreen(),
        overrides: [
          adminComplaintsProvider.overrideWith(
            (ref) async => [
              Complaint.fromJson({
                'complaintId': 'c1',
                'subject': 'How do I donate?',
                'status': 'OPEN',
              }),
              Complaint.fromJson({
                'complaintId': 'c2',
                'subject': 'Rude staff',
                'status': 'OPEN',
                'hospitalId': 'h1',
                'hospitalName': 'Fake General',
              }),
            ],
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Rude staff'), findsOneWidget);
    await tester.ensureVisible(find.text('General question'));
    await tester.tap(find.text('General question'));
    await tester.pumpAndSettle();
    expect(find.text('How do I donate?'), findsOneWidget);
    expect(find.text('Rude staff'), findsNothing);
  });

  testWidgets('admin message: subject and message rules before sending', (
    tester,
  ) async {
    phone(tester);
    final api = MockedApi(token: 'jwt');
    await tester.pumpWidget(
      testApp(
        const Scaffold(
          body: AdminMessageSheet(userId: 'u1', recipient: 'Test Person'),
        ),
        overrides: api.overrides,
      ),
    );
    await tester.enterText(find.byKey(const Key('message-subject')), 'Hi');
    await tester.enterText(
      find.byKey(const Key('message-body')),
      'Please update your details.',
    );
    await tester.tap(find.byKey(const Key('message-send')));
    await tester.pump();
    expect(
      find.text('The subject needs at least 3 characters.'),
      findsOneWidget,
    );
    expect(api.sent, isEmpty);
  });

  testWidgets(
    'governance: suspended account sees the reason and the appeal rules',
    (tester) async {
      phone(tester);
      await tester.pumpWidget(
        testApp(
          const GovernanceStatusScreen(),
          overrides: [
            governanceStatusProvider.overrideWith(
              (ref) async => GovernanceStatus.fromJson({
                'isSuspended': true,
                'suspensionReason': 'Fake documents',
                'suspendedEntity': 'User',
                'canAppeal': true,
                'allAppeals': [],
                'profile': {
                  'name': 'Test Person',
                  'email': 'test.person@example.test',
                  'role': 'User',
                },
              }),
            ),
          ],
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Your account is suspended'), findsOneWidget);
      expect(find.text('Reason: Fake documents'), findsOneWidget);
      await tester.enterText(find.byKey(const Key('appeal-reason')), 'short');
      await tester.ensureVisible(find.byKey(const Key('appeal-submit')));
      await tester.tap(find.byKey(const Key('appeal-submit')));
      await tester.pump();
      expect(
        find.text('Please explain your appeal in at least 10 characters.'),
        findsOneWidget,
      );
    },
  );
}
