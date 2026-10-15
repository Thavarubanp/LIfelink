import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/doctor/doctor_home_screen.dart';
import 'package:lifelink_mobile/features/doctor/screening_models.dart';
import 'package:lifelink_mobile/features/doctor/screening_reports_screen.dart';
import 'package:lifelink_mobile/features/doctors/doctor_repository.dart';
import 'package:lifelink_mobile/features/doctors/doctors_screen.dart';
import 'package:lifelink_mobile/features/verification/request_donors_screen.dart';
import 'package:lifelink_mobile/features/verification/verification_models.dart';
import 'package:lifelink_mobile/features/verification/verification_repository.dart';
import 'package:lifelink_mobile/features/verification/verify_requests_screen.dart';

import '../helpers.dart';

void phone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 2400);
  tester.view.devicePixelRatio = 2.5;
  addTearDown(tester.view.reset);
}

Doctor doctor(String id, {bool mustChange = false}) => Doctor.fromJson({
  'doctorId': id,
  'firstName': 'Doc',
  'lastName': id,
  'licenseNumber': 'SLMC-$id',
  'isActive': true,
  'userId': 'u-$id',
  'mustChangePassword': mustChange,
});

void main() {
  testWidgets('assign doctor: doctors pending first login are not offered', (
    tester,
  ) async {
    phone(tester);
    final request = ClinicalRequest.fromJson({
      'bloodRequestId': 'r1-000000',
      'status': 'Pending',
    });
    await tester.pumpWidget(
      testApp(
        Scaffold(body: AssignDoctorSheet(request: request)),
        overrides: [
          hospitalDoctorsProvider.overrideWith(
            (ref) async => [doctor('A'), doctor('B', mustChange: true)],
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('doctor-A')), findsOneWidget);
    expect(find.byKey(const Key('doctor-B')), findsNothing);
    expect(
      find.text('1 doctor(s) pending first login are not listed.'),
      findsOneWidget,
    );

    await tester.tap(find.byKey(const Key('assign-submit')));
    await tester.pump();
    expect(
      find.text('Choose the doctor who will review this request.'),
      findsOneWidget,
    );
  });

  testWidgets(
    'doctors list shows "Pending first login"; add doctor validates SLMC and phone',
    (tester) async {
      phone(tester);
      await tester.pumpWidget(
        testApp(
          const DoctorsScreen(),
          overrides: [
            hospitalDoctorsProvider.overrideWith(
              (ref) async => [doctor('A'), doctor('B', mustChange: true)],
            ),
          ],
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byType(AppBar), findsNothing);
      expect(find.text('Doctors'), findsOneWidget);
      expect(find.text('Pending first login'), findsOneWidget);

      await tester.tap(find.byKey(const Key('add-doctor')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('doctor-firstName')), 'Ann');
      await tester.enterText(
        find.byKey(const Key('doctor-lastName')),
        'Perera',
      );
      await tester.enterText(
        find.byKey(const Key('doctor-email')),
        'ann@example.test',
      );
      await tester.enterText(
        find.byKey(const Key('doctor-licenseNumber')),
        'slmc-a',
      );
      await tester.enterText(
        find.byKey(const Key('doctor-phoneNumber')),
        '07712',
      );
      await tester.ensureVisible(find.byKey(const Key('doctor-save')));
      await tester.tap(find.byKey(const Key('doctor-save')));
      await tester.pump();
      expect(
        find.text('A doctor with this SLMC number already exists.'),
        findsOneWidget,
      );
      expect(
        find.text('Phone number must be exactly 10 digits.'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'doctor home: Approve / Reject only on Verified, not on suspended requests',
    (tester) async {
      phone(tester);
      await tester.pumpWidget(
        testApp(
          const DoctorRequestsScreen(),
          overrides: [
            assignedRequestsProvider.overrideWith(
              (ref) async => [
                ClinicalRequest.fromJson({
                  'bloodRequestId': 'r-verified',
                  'status': 'Verified',
                  'bloodGroup': 'A+',
                  'priority': 'Normal',
                }),
                ClinicalRequest.fromJson({
                  'bloodRequestId': 'r-suspended',
                  'status': 'Verified',
                  'bloodGroup': 'B+',
                  'isSuspended': true,
                }),
              ],
            ),
          ],
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('approve-r-verified')), findsOneWidget);
      expect(find.byKey(const Key('approve-r-suspended')), findsNothing);
      expect(
        find.textContaining('Suspended by the administrator'),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'doctor dashboard is 2x2 on phones and has a useful empty state',
    (tester) async {
      tester.view.physicalSize = const Size(1080, 2340);
      tester.view.devicePixelRatio = 3;
      addTearDown(tester.view.reset);
      await tester.pumpWidget(
        testApp(
          const DoctorDashboardScreen(),
          overrides: [
            assignedRequestsProvider.overrideWith((ref) async => const []),
            screeningReportsProvider.overrideWith((ref) async => const []),
          ],
        ),
      );
      await tester.pumpAndSettle();

      final assigned = tester.getTopLeft(
        find.byKey(const Key('doctor-stat-assigned')),
      );
      final screened = tester.getTopLeft(
        find.byKey(const Key('doctor-stat-screened')),
      );
      final pending = tester.getTopLeft(
        find.byKey(const Key('doctor-stat-pending')),
      );
      final awaiting = tester.getTopLeft(
        find.byKey(const Key('doctor-stat-awaiting')),
      );
      expect(assigned.dy, screened.dy);
      expect(pending.dy, awaiting.dy);
      expect(pending.dy, greaterThan(assigned.dy));
      for (final label in [
        'Assigned decisions',
        'Screened donors',
        'Pending reviews',
        'Awaiting donation',
      ]) {
        expect(find.text(label), findsOneWidget);
      }
      expect(find.text('0'), findsNWidgets(4));
      expect(find.byKey(const Key('doctor-dashboard-empty')), findsOneWidget);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('doctor dashboard uses four columns on wide layouts', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(1200, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(
      testApp(
        const DoctorDashboardScreen(),
        overrides: [
          assignedRequestsProvider.overrideWith((ref) async => const []),
          screeningReportsProvider.overrideWith((ref) async => const []),
        ],
      ),
    );
    await tester.pumpAndSettle();

    final positions = [
      for (final key in [
        'doctor-stat-assigned',
        'doctor-stat-screened',
        'doctor-stat-pending',
        'doctor-stat-awaiting',
      ])
        tester.getTopLeft(find.byKey(Key(key))),
    ];
    expect(positions.map((p) => p.dy).toSet(), hasLength(1));
    expect(positions.map((p) => p.dx).toSet(), hasLength(4));
    expect(tester.takeException(), isNull);
  });

  test('dashboard and Reports share one screening provider load', () async {
    var reportLoads = 0;
    final container = ProviderContainer(
      overrides: [
        assignedRequestsProvider.overrideWith((ref) async => const []),
        screeningReportsProvider.overrideWith((ref) async {
          reportLoads++;
          return const [];
        }),
      ],
    );
    addTearDown(container.dispose);
    final subscription = container.listen(
      doctorDashboardProvider,
      (_, _) {},
      fireImmediately: true,
    );
    addTearDown(subscription.close);

    await container.read(doctorDashboardProvider.future);
    await container.read(screeningReportsProvider.future);

    expect(reportLoads, 1);
  });

  group('screening report detail', () {
    Future<void> pump(
      WidgetTester tester,
      List<ScreeningReport> reports,
    ) async {
      phone(tester);
      await tester.pumpWidget(
        testApp(
          const ScreeningReportDetailScreen(acceptanceId: 'a1'),
          overrides: [
            screeningReportsProvider.overrideWith((ref) async => reports),
          ],
        ),
      );
      await tester.pumpAndSettle();
    }

    ScreeningReport r({
      int version = 1,
      String status = 'Pending',
      bool freeSlot = true,
    }) => ScreeningReport.fromJson({
      'donorVerificationId': 'v$version',
      'acceptanceId': 'a1',
      'reportVersion': version,
      'status': status,
      'acceptanceStatus': 'ScreeningCompleted',
      'hasFreeSlot': freeSlot,
      'donorName': 'Test Donor',
      'reportJson': reportJson(
        flags: [
          {
            'code': 'AGE_RANGE',
            'severity': 'defer',
            'section': 1,
            'message': 'Age 66 is outside 18 to 60.',
          },
        ],
      ),
    });

    testWidgets(
      'shows the agent flags and the doctor decides; no free slot disables Approve',
      (tester) async {
        await pump(tester, [r(freeSlot: false)]);
        expect(
          find.text('Likely deferral – doctor to confirm'),
          findsOneWidget,
        );
        expect(find.textContaining('Age 66'), findsOneWidget);
        expect(find.text('Flagged'), findsOneWidget); // section 1 highlighted
        expect(
          tester
              .widget<ButtonStyleButton>(
                find.byKey(const Key('report-approve')),
              )
              .onPressed,
          isNull,
        );
        expect(
          tester
              .widget<ButtonStyleButton>(find.byKey(const Key('report-reject')))
              .onPressed,
          isNotNull,
        );
      },
    );

    testWidgets('superseded and decided versions are read-only', (
      tester,
    ) async {
      await pump(tester, [
        r(version: 1, status: 'Superseded'),
        r(version: 2, status: 'Rejected'),
      ]);
      expect(find.byKey(const Key('report-approve')), findsNothing);
      expect(find.textContaining('Decided: Rejected'), findsOneWidget);
      await tester.tap(find.text('v1 · Superseded'));
      await tester.pumpAndSettle();
      expect(
        find.textContaining('Superseded: the donor updated their answers'),
        findsOneWidget,
      );
    });
  });

  testWidgets(
    'donors: call / email buttons; hospital donation decisions for the doctor only',
    (tester) async {
      phone(tester);
      final acceptances = [
        RequestAcceptance.fromJson({
          'acceptanceId': 'a1',
          'donorName': 'Test Donor',
          'donorEmail': 'donor@example.test',
          'donorPhoneNumber': '0771234567',
          'status': 'Verified',
        }),
        RequestAcceptance.fromJson({
          'acceptanceId': 'a2',
          'donorName': 'Fake General',
          'donorEmail': 'h@example.test',
          'status': 'Accepted',
          'donorHospitalId': 'h2',
        }),
      ];
      await tester.pumpWidget(
        testApp(
          const RequestDonorsScreen(requestId: 'r1', isDoctor: false),
          overrides: [
            requestAcceptancesProvider.overrideWith(
              (ref, id) async => acceptances,
            ),
          ],
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('call-a1')), findsOneWidget);
      expect(find.byKey(const Key('email-a1')), findsOneWidget);
      expect(find.byKey(const Key('record-a1')), findsOneWidget);
      expect(
        find.byKey(const Key('hospital-approve-a2')),
        findsNothing,
      ); // hospital staff cannot decide

      await tester.pumpWidget(
        testApp(
          const RequestDonorsScreen(
            requestId: 'r1',
            isDoctor: true,
            key: Key('doctor'),
          ),
          overrides: [
            requestAcceptancesProvider.overrideWith(
              (ref, id) async => acceptances,
            ),
          ],
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('hospital-approve-a2')), findsOneWidget);
    },
  );

  testWidgets('record donation requires the tested blood group', (
    tester,
  ) async {
    phone(tester);
    await tester.pumpWidget(
      testApp(
        const Scaffold(
          body: RecordDonationSheet(
            requestId: 'r1',
            acceptanceId: 'a1',
            donorName: 'Test Donor',
          ),
        ),
        overrides: MockedApi(token: 'jwt').overrides,
      ),
    );
    await tester.tap(find.byKey(const Key('record-submit')));
    await tester.pump();
    expect(find.text('Select the tested blood group.'), findsOneWidget);
  });
}
