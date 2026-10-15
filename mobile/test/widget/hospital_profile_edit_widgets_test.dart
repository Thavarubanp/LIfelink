import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/profile/hospital_profile_edit_sheet.dart';
import 'package:lifelink_mobile/features/profile/profile_repository.dart';
import 'package:lifelink_mobile/features/profile/public_profile_screens.dart';

import '../helpers.dart';

const hospital = HospitalProfile(
  hospitalId: 'h1',
  name: 'Central Hospital',
  address: '1 Main Street',
  city: 'Colombo',
  contactNumber: '0112345678',
  email: 'official@example.test',
  isVerified: true,
  doctorCount: 4,
  packetShelfLifeDays: 35,
  expiryAlertDays: 7,
  contactPersonName: 'Nimal Perera',
  contactPersonPhone: '0771234567',
  contactPersonEmail: 'nimal@example.test',
  canEdit: true,
);

Map<String, dynamic> get hospitalJson => {
  'hospitalId': hospital.hospitalId,
  'name': hospital.name,
  'address': hospital.address,
  'city': hospital.city,
  'contactNumber': hospital.contactNumber,
  'email': hospital.email,
  'isVerified': hospital.isVerified,
  'doctorCount': hospital.doctorCount,
  'packetShelfLifeDays': hospital.packetShelfLifeDays,
  'expiryAlertDays': hospital.expiryAlertDays,
  'contactPersonName': hospital.contactPersonName,
  'contactPersonPhone': hospital.contactPersonPhone,
  'contactPersonEmail': hospital.contactPersonEmail,
  'canEdit': hospital.canEdit,
};

void tallPhone(WidgetTester tester) {
  tester.view.physicalSize = const Size(1080, 3000);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);
}

List<Override> signedInHospitalOverrides({bool suspended = false}) => [
  authControllerProvider.overrideWith(
    () => FakeAuthController(
      AuthState.signedIn(
        testUser(
          roles: const ['HospitalStaff'],
          suspended: suspended,
          hospitalApprovalStatus: 'Approved',
        ),
      ),
    ),
  ),
];

class _EditHarness extends ConsumerWidget {
  const _EditHarness();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    ref.watch(myHospitalProvider);
    ref.watch(hospitalProfileProvider(hospital.hospitalId));
    return Scaffold(
      body: Center(
        child: FilledButton(
          onPressed: () => showHospitalProfileEditSheet(context, ref, hospital),
          child: const Text('Edit'),
        ),
      ),
    );
  }
}

void main() {
  test('editing requires both API permission and a non-suspended user', () {
    final user = testUser(
      roles: const ['HospitalStaff'],
      hospitalApprovalStatus: 'Approved',
    );
    expect(canEditHospitalProfile(hospital, user), isTrue);
    expect(
      canEditHospitalProfile(
        hospital,
        testUser(
          roles: const ['HospitalStaff'],
          suspended: true,
          hospitalApprovalStatus: 'Approved',
        ),
      ),
      isFalse,
    );
    expect(
      canEditHospitalProfile(
        const HospitalProfile(
          hospitalId: 'h1',
          name: 'Central Hospital',
          address: '1 Main Street',
          city: null,
          contactNumber: '0112345678',
          email: 'official@example.test',
          isVerified: true,
          doctorCount: 4,
          packetShelfLifeDays: 35,
          expiryAlertDays: 7,
        ),
        user,
      ),
      isFalse,
    );
  });

  testWidgets('owned public hospital profile exposes edit when authorized', (
    tester,
  ) async {
    await tester.pumpWidget(
      testApp(
        const HospitalProfileScreen(hospitalId: 'h1'),
        overrides: [
          ...signedInHospitalOverrides(),
          hospitalProfileProvider.overrideWith((ref, id) async => hospital),
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(
      find.byKey(const Key('edit-owned-hospital-profile')),
      findsOneWidget,
    );
  });

  testWidgets('edit sheet validates fields and excludes protected properties', (
    tester,
  ) async {
    tallPhone(tester);
    final api = MockedApi(token: 'jwt');
    await tester.pumpWidget(
      testApp(
        const Scaffold(body: HospitalProfileEditSheet(profile: hospital)),
        overrides: api.overrides,
      ),
    );

    expect(find.text('Official email'), findsNothing);
    expect(find.text('Licence number'), findsNothing);
    expect(find.text('Registration number'), findsNothing);
    expect(find.text('Approval state'), findsNothing);

    await tester.enterText(find.byKey(const Key('hospital-profile-name')), '');
    await tester.enterText(
      find.byKey(const Key('hospital-profile-contactNumber')),
      '07712',
    );
    await tester.enterText(
      find.byKey(const Key('hospital-profile-contactPersonName')),
      '',
    );
    await tester.enterText(
      find.byKey(const Key('hospital-profile-contactPersonEmail')),
      'invalid',
    );
    await tester.enterText(
      find.byKey(const Key('hospital-profile-packetShelfLifeDays')),
      '20',
    );
    await tester.ensureVisible(find.byKey(const Key('hospital-profile-save')));
    await tester.tap(find.byKey(const Key('hospital-profile-save')));
    await tester.pump();

    expect(find.text('Hospital name is required.'), findsOneWidget);
    expect(find.text('Enter a 10-digit phone number.'), findsOneWidget);
    expect(find.text('Authorized person name is required.'), findsOneWidget);
    expect(find.text('Invalid email address format.'), findsOneWidget);
    expect(find.text('Packet shelf life must be at least 21.'), findsOneWidget);
    expect(api.sent, isEmpty);
  });

  testWidgets('server validation is mapped back to its hospital field', (
    tester,
  ) async {
    tallPhone(tester);
    final api = MockedApi(token: 'jwt');
    api.adapter.onPut(
      '/profiles/hospital/h1',
      (server) => server.reply(400, {
        'title': 'Validation failed',
        'errors': {
          'ContactPersonName': ['Choose an authorized contact.'],
        },
      }),
      data: Matchers.any,
    );
    await tester.pumpWidget(
      testApp(
        const Scaffold(body: HospitalProfileEditSheet(profile: hospital)),
        overrides: api.overrides,
      ),
    );

    await tester.ensureVisible(find.byKey(const Key('hospital-profile-save')));
    await tester.tap(find.byKey(const Key('hospital-profile-save')));
    await tester.pumpAndSettle();
    final field = tester.widget<TextField>(
      find.byKey(const Key('hospital-profile-contactPersonName')),
    );
    expect(field.decoration?.errorText, 'Choose an authorized contact.');
  });

  testWidgets('409 directs the user to the registration conversation', (
    tester,
  ) async {
    tallPhone(tester);
    final api = MockedApi(token: 'jwt');
    api.adapter.onPut(
      '/profiles/hospital/h1',
      (server) => server.reply(409, {
        'message': 'Hospital registration is still under review.',
      }),
      data: Matchers.any,
    );
    await tester.pumpWidget(
      testApp(
        const Scaffold(body: HospitalProfileEditSheet(profile: hospital)),
        overrides: api.overrides,
      ),
    );

    await tester.ensureVisible(find.byKey(const Key('hospital-profile-save')));
    await tester.tap(find.byKey(const Key('hospital-profile-save')));
    await tester.pumpAndSettle();
    expect(find.textContaining('registration conversation'), findsOneWidget);
    expect(find.byKey(const Key('open-registration-thread')), findsOneWidget);
    expect(find.text('Open registration thread'), findsOneWidget);
  });

  testWidgets('successful save invalidates both hospital profile providers', (
    tester,
  ) async {
    tallPhone(tester);
    final api = MockedApi(token: 'jwt');
    var myHospitalBuilds = 0;
    var publicHospitalBuilds = 0;
    api.adapter.onPut(
      '/profiles/hospital/h1',
      (server) => server.reply(200, hospitalJson),
      data: Matchers.any,
    );
    await tester.pumpWidget(
      testApp(
        const _EditHarness(),
        overrides: [
          ...api.overrides,
          ...signedInHospitalOverrides(),
          myHospitalProvider.overrideWith((ref) async {
            myHospitalBuilds++;
            return hospital;
          }),
          hospitalProfileProvider.overrideWith((ref, id) async {
            publicHospitalBuilds++;
            return hospital;
          }),
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(myHospitalBuilds, 1);
    expect(publicHospitalBuilds, 1);

    await tester.tap(find.text('Edit'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(const Key('hospital-profile-save')));
    await tester.tap(find.byKey(const Key('hospital-profile-save')));
    await tester.pumpAndSettle();

    expect(myHospitalBuilds, greaterThan(1));
    expect(publicHospitalBuilds, greaterThan(1));
    expect(api.sent.single.path, '/profiles/hospital/h1');
  });
}
