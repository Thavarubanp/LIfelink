import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/admin/admin_home_screen.dart';
import 'package:lifelink_mobile/features/admin/admin_repository.dart';
import 'package:lifelink_mobile/features/admin/appeals/admin_appeals.dart';
import 'package:lifelink_mobile/features/admin/attention_controller.dart';
import 'package:lifelink_mobile/features/admin/complaints/admin_complaints.dart';
import 'package:lifelink_mobile/features/admin/directory/admin_actions.dart';
import 'package:lifelink_mobile/features/admin/registrations/registration_detail_screen.dart';
import 'package:lifelink_mobile/features/admin/registrations/registration_repository.dart';
import 'package:lifelink_mobile/features/appeals/appeal_models.dart';
import 'package:lifelink_mobile/features/governance/governance_repository.dart';
import 'package:lifelink_mobile/features/governance/governance_status_screen.dart';

import '../helpers.dart';

void phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.5;
  addTearDown(tester.view.reset);
}

void main() {
  testWidgets('admin home: nine numbers and attention badges', (tester) async {
    phone(tester);
    await tester.pumpWidget(testApp(const AdminHomeScreen(), overrides: [
      adminStatsProvider.overrideWith((ref) async => AdminStats.fromJson({'totalHospitals': 7, 'pendingAppeals': 1, 'activeSuspendedUsers': 5})),
      adminAttentionProvider.overrideWith(FakeAttention.new),
    ]));
    await tester.pumpAndSettle();
    expect(find.text('Hospitals'), findsOneWidget);
    expect(find.text('7'), findsOneWidget);
    expect(find.text('Suspended users'), findsOneWidget);
    // Badges: registrations 2, appeals 1, complaints 3, new requests 4
    expect(find.descendant(of: find.byKey(const Key('attention-Complaints')), matching: find.text('3')), findsOneWidget);
    expect(find.descendant(of: find.byKey(const Key('attention-New blood requests')), matching: find.text('4')), findsOneWidget);
  });

  group('registration detail', () {
    Future<void> pump(WidgetTester tester, String status) async {
      phone(tester);
      await tester.pumpWidget(testApp(const RegistrationDetailScreen(hospitalId: 'h1'), overrides: [
        adminHospitalsProvider.overrideWith((ref) async => [
              AdminHospital.fromJson({
                'hospitalId': 'h1',
                'name': 'Fake General',
                'approvalStatus': status,
                'approvalHistory': [{'id': 'e1', 'type': 'Submitted', 'fromAdmin': false, 'timestamp': '2026-10-05T04:00:00Z'}],
              }),
            ]),
        adminAttentionProvider.overrideWith(FakeAttention.new),
      ]));
      await tester.pumpAndSettle();
    }

    testWidgets('reject needs a reason of at least 3 characters', (tester) async {
      await pump(tester, 'Pending');
      expect(find.byKey(const Key('registration-approve')), findsOneWidget);
      expect(find.byKey(const Key('registration-comment')), findsNothing);
      await tester.enterText(find.byKey(const Key('registration-text')), 'no');
      await tester.ensureVisible(find.byKey(const Key('registration-reject')));
      await tester.tap(find.byKey(const Key('registration-reject')));
      await tester.pump();
      expect(find.text('Please give a rejection reason (at least 3 characters).'), findsOneWidget);
    });

    testWidgets('a rejected registration waits for the hospital: comment only', (tester) async {
      await pump(tester, 'Rejected');
      expect(find.byKey(const Key('registration-approve')), findsNothing);
      expect(find.byKey(const Key('registration-reject')), findsNothing);
      expect(find.byKey(const Key('registration-comment')), findsOneWidget);
    });
  });

  testWidgets('appeal already rejected once: Reject is disabled', (tester) async {
    phone(tester);
    await tester.pumpWidget(testApp(const AdminAppealDetailScreen(appealId: 'a1'), overrides: [
      adminAppealsProvider.overrideWith((ref, status) async => [
            Appeal.fromJson({'appealId': 'a1', 'userEmail': 'donor@example.test', 'reason': 'Please reinstate me', 'status': 'REJECTED', 'canReject': false}),
          ]),
      adminAttentionProvider.overrideWith(FakeAttention.new),
    ]));
    await tester.pumpAndSettle();
    final reject = tester.widget<ButtonStyleButton>(find.byKey(const Key('appeal-reject')));
    expect(reject.onPressed, isNull);
    expect(find.byKey(const Key('appeal-approve')), findsOneWidget);
  });

  testWidgets('complaints: the "General question" filter', (tester) async {
    phone(tester);
    await tester.pumpWidget(testApp(const AdminComplaintsScreen(), overrides: [
      adminComplaintsProvider.overrideWith((ref) async => [
            Complaint.fromJson({'complaintId': 'c1', 'subject': 'How do I donate?', 'status': 'OPEN'}),
            Complaint.fromJson({'complaintId': 'c2', 'subject': 'Rude staff', 'status': 'OPEN', 'hospitalId': 'h1', 'hospitalName': 'Fake General'}),
          ]),
    ]));
    await tester.pumpAndSettle();
    expect(find.text('Rude staff'), findsOneWidget);
    await tester.ensureVisible(find.text('General question'));
    await tester.tap(find.text('General question'));
    await tester.pumpAndSettle();
    expect(find.text('How do I donate?'), findsOneWidget);
    expect(find.text('Rude staff'), findsNothing);
  });

  testWidgets('admin message: subject and message rules before sending', (tester) async {
    phone(tester);
    final api = MockedApi(token: 'jwt');
    await tester.pumpWidget(testApp(const Scaffold(body: AdminMessageSheet(userId: 'u1', recipient: 'Test Person')), overrides: api.overrides));
    await tester.enterText(find.byKey(const Key('message-subject')), 'Hi');
    await tester.enterText(find.byKey(const Key('message-body')), 'Please update your details.');
    await tester.tap(find.byKey(const Key('message-send')));
    await tester.pump();
    expect(find.text('The subject needs at least 3 characters.'), findsOneWidget);
    expect(api.sent, isEmpty);
  });

  testWidgets('governance: suspended account sees the reason and the appeal rules', (tester) async {
    phone(tester);
    await tester.pumpWidget(testApp(const GovernanceStatusScreen(), overrides: [
      governanceStatusProvider.overrideWith((ref) async => GovernanceStatus.fromJson({
            'isSuspended': true,
            'suspensionReason': 'Fake documents',
            'suspendedEntity': 'User',
            'canAppeal': true,
            'allAppeals': [],
            'profile': {'name': 'Test Person', 'email': 'test.person@example.test', 'role': 'User'},
          })),
    ]));
    await tester.pumpAndSettle();
    expect(find.text('Your account is suspended'), findsOneWidget);
    expect(find.text('Reason: Fake documents'), findsOneWidget);
    await tester.enterText(find.byKey(const Key('appeal-reason')), 'short');
    await tester.ensureVisible(find.byKey(const Key('appeal-submit')));
    await tester.tap(find.byKey(const Key('appeal-submit')));
    await tester.pump();
    expect(find.text('Please explain your appeal in at least 10 characters.'), findsOneWidget);
  });
}
