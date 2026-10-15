import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/attachments/attachment.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/auth/hospital_register_screen.dart';
import 'package:lifelink_mobile/features/auth/hospital_registration_repository.dart';
import 'package:lifelink_mobile/features/governance/waiting_approval_screen.dart';

import '../helpers.dart';

void main() {
  void useTallPhone(WidgetTester tester) {
    tester.view.physicalSize = const Size(1080, 3000);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);
  }

  testWidgets('hospital registration validates every required web field', (
    tester,
  ) async {
    useTallPhone(tester);
    final api = MockedApi();
    await tester.pumpWidget(
      testApp(const HospitalRegisterScreen(), overrides: api.overrides),
    );
    await tester.pumpAndSettle();

    await tester.ensureVisible(
      find.byKey(const Key('hospital-register-submit')),
    );
    await tester.tap(find.byKey(const Key('hospital-register-submit')));
    await tester.pump();

    expect(
      find.text('Hospital / Medical Center Name is required.'),
      findsOneWidget,
    );
    expect(find.text('Registration number is required.'), findsOneWidget);
    expect(
      find.text('Phone number must contain exactly 10 digits.'),
      findsOneWidget,
    );
    expect(
      find.text('Official hospital email address is required.'),
      findsOneWidget,
    );
    expect(find.text('Password is required.'), findsOneWidget);
    expect(
      find.text('Physical Hospital Address & City is required.'),
      findsOneWidget,
    );
    expect(find.text('Authorized person name is required.'), findsOneWidget);
    expect(
      find.text(
        'Authorized person phone number must contain exactly 10 digits.',
      ),
      findsOneWidget,
    );
    expect(api.sent, isEmpty);
  });

  testWidgets(
    'institution type switches the registration label and clears its value',
    (tester) async {
      useTallPhone(tester);
      await tester.pumpWidget(testApp(const HospitalRegisterScreen()));
      await tester.pumpAndSettle();

      expect(find.text('PHSRC Registration Number *'), findsOneWidget);
      await tester.enterText(
        find.byKey(const Key('hospital-registration-number')),
        'PHSRC/PH/123',
      );
      await tester.tap(find.byKey(const Key('hospital-institution-type')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Government Hospital').last);
      await tester.pumpAndSettle();

      expect(
        find.text(
          'Health Institution Number (HIN) / Government Facility Code *',
        ),
        findsOneWidget,
      );
      final field = tester.widget<TextFormField>(
        find.byKey(const Key('hospital-registration-number')),
      );
      expect(field.controller?.text, isEmpty);
    },
  );

  testWidgets('hospital document picker shows the shared 2 MB rejection', (
    tester,
  ) async {
    useTallPhone(tester);
    Future<Attachment?> tooLarge() async =>
        throw const AttachmentException(AttachmentRules.tooLargeMessage);
    await tester.pumpWidget(
      testApp(HospitalRegisterScreen(documentPicker: tooLarge)),
    );
    await tester.pumpAndSettle();

    final license = find.byKey(const Key('hospital-license-document'));
    await tester.ensureVisible(license);
    await tester.tap(
      find.descendant(of: license, matching: find.text('Attach file')),
    );
    await tester.pumpAndSettle();

    expect(find.text('Files cannot exceed 2 MB.'), findsOneWidget);
  });

  testWidgets('waiting screen renders every registration conversation type', (
    tester,
  ) async {
    useTallPhone(tester);
    final entries = <Map<String, dynamic>>[
      {
        'id': '1',
        'type': 'Submitted',
        'fromAdmin': false,
        'timestamp': '2026-10-15T01:00:00Z',
        'message': 'Hospital registration submitted.',
      },
      {
        'id': '2',
        'type': 'Rejected',
        'fromAdmin': true,
        'timestamp': '2026-10-15T02:00:00Z',
        'message': 'Replace the licence.',
        'attachmentName': 'report.pdf',
        'attachmentUrl': 'data:application/pdf;base64,AQID',
      },
      {
        'id': '3',
        'type': 'AdminComment',
        'fromAdmin': true,
        'timestamp': '2026-10-15T03:00:00Z',
        'message': 'Please confirm the address.',
      },
      {
        'id': '4',
        'type': 'HospitalReply',
        'fromAdmin': false,
        'timestamp': '2026-10-15T04:00:00Z',
        'message': 'Corrected.',
        'changedFields': 'Address: Old -> New',
      },
      {
        'id': '5',
        'type': 'Approved',
        'fromAdmin': true,
        'timestamp': '2026-10-15T05:00:00Z',
        'message': 'Approved.',
      },
    ];
    final registration = HospitalRegistration.fromJson(
      hospitalRegistrationJson(status: 'Approved', history: entries),
    );
    await tester.pumpWidget(
      testApp(
        WaitingApprovalScreen(initialRegistration: registration),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(const AuthState.signedOut()),
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.text('Registration Conversation'),
      500,
    );
    await tester.pumpAndSettle();

    for (final title in [
      'Registration submitted',
      'Registration rejected',
      'Admin comment',
      'Hospital correction',
      'Registration approved',
    ]) {
      expect(find.textContaining(title), findsOneWidget);
    }
    expect(find.text('report.pdf'), findsOneWidget);
    expect(find.textContaining('Address: Old → New'), findsOneWidget);
  });

  testWidgets('reply action is unavailable when status is not Rejected', (
    tester,
  ) async {
    useTallPhone(tester);
    final registration = HospitalRegistration.fromJson(
      hospitalRegistrationJson(status: 'AwaitingAdminReview'),
    );
    await tester.pumpWidget(
      testApp(
        const WaitingApprovalScreen(),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(
              AuthState.signedIn(
                testUser(
                  roles: const ['HospitalStaff'],
                  hospitalApprovalStatus: 'AwaitingAdminReview',
                ),
              ),
            ),
          ),
          hospitalRegistrationRepositoryProvider.overrideWithValue(
            FakeHospitalRegistrationRepository(registration),
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();
    await tester.scrollUntilVisible(
      find.textContaining('Your reply is with the administrator.'),
      500,
    );

    expect(
      find.textContaining('Your reply is with the administrator.'),
      findsOneWidget,
    );
    expect(find.byKey(const Key('registration-reply-submit')), findsNothing);
  });
}
