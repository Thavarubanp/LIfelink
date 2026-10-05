import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';
import 'package:lifelink_mobile/features/doctor/screening_models.dart';
import 'package:lifelink_mobile/features/doctors/doctor_repository.dart';
import 'package:lifelink_mobile/features/verification/verification_repository.dart';

import '../helpers.dart';

/// Step 3 repositories against a mocked HTTP layer, with the web app's request bodies.
void main() {
  test('verify, approve and reject requests', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet('/BloodRequests/hospital', (s) => s.reply(200, [
            {'bloodRequestId': 'r1', 'status': 'Pending', 'bloodGroup': 'A+', 'createdAt': '2026-10-05T04:00:00Z'},
          ]))
      ..onPut('/requests/r1/verify', (s) => s.reply(200, {'success': true}), data: {'doctorId': 'd1'})
      ..onPut('/requests/r1/approve', (s) => s.reply(200, {'success': true}), data: {'notes': ''})
      ..onPut('/requests/r2/reject', (s) => s.reply(409, {'message': 'This request was already decided.'}), data: {'notes': 'Duplicate request'});
    final repo = VerificationRepository(api.client);

    expect((await repo.hospitalRequests()).single.canVerify, isTrue);
    await repo.verify('r1', 'd1');
    await repo.approve('r1', '');
    await expectLater(repo.reject('r2', 'Duplicate request'),
        throwsA(isA<ApiError>().having((e) => e.isConflict, 'conflict', isTrue).having((e) => e.message, 'message', 'This request was already decided.')));
    expect(api.sent.last.headers['X-LifeLink-Activity'], anyOf(isNull, '1'));
  });

  test('donors, record donation, release, hospital donation decisions', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet('/BloodRequests/r1/acceptances', (s) => s.reply(200, [
            {'acceptanceId': 'a1', 'donorName': 'Test Donor', 'donorEmail': 'donor@example.test', 'donorPhoneNumber': '0771234567', 'status': 'Verified'},
            {'acceptanceId': 'a2', 'donorName': 'Fake General', 'status': 'Accepted', 'donorHospitalId': 'h2', 'packets': [{'trackingNumber': 'PKT-00000009'}]},
          ]))
      ..onPut('/BloodRequests/r1/finalize-selection', (s) => s.reply(200, {}), data: {
        'selectedAcceptanceIds': ['a1'],
        'testedBloodGroups': {'a1': 'O+'},
      })
      ..onPut('/Acceptances/a1/release', (s) => s.reply(200, {}), data: {'reason': 'No-show'})
      ..onPut('/Acceptances/a2/hospital-approve', (s) => s.reply(200, {}), data: {'notes': 'Checked'})
      ..onPut('/Acceptances/a2/hospital-reject', (s) => s.reply(200, {}), data: {'reason': 'Packets expire too soon'});
    final repo = VerificationRepository(api.client);

    final list = await repo.acceptances('r1');
    expect(list.first.isReserved, isTrue);
    expect(list.last.awaitsHospitalDecision, isTrue);
    await repo.recordDonation('r1', 'a1', 'O+');
    await repo.release('a1', 'No-show');
    await repo.approveHospitalDonation('a2', 'Checked');
    await repo.rejectHospitalDonation('a2', 'Packets expire too soon');
  });

  test('screening reports: list and decisions', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet('/donor-verification', (s) => s.reply(200, [
            {'donorVerificationId': 'v1', 'acceptanceId': 'a1', 'reportVersion': 1, 'status': 'Pending', 'acceptanceStatus': 'ScreeningCompleted', 'hasFreeSlot': true},
          ]))
      ..onPut('/donor-verification/v1/approve', (s) => s.reply(200, {}), data: {'notes': 'See you at 10am'})
      ..onPut('/donor-verification/v1/reject', (s) => s.reply(200, {}), data: {'notes': 'Recent tattoo'});
    final repo = ScreeningRepository(api.client);

    expect((await repo.reports()).single.canDecide, isTrue);
    await repo.approve('v1', 'See you at 10am');
    await repo.reject('v1', 'Recent tattoo');
  });

  test('doctors: create (email lower-cased, hospital id), remove, profile edit', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onPost('/Doctors', (s) => s.reply(201, {'success': true}), data: {
        'hospitalId': 'h1',
        'firstName': 'Ann',
        'lastName': 'Perera',
        'email': 'ann.perera@example.test',
        'password': 'Secret#123',
        'licenseNumber': 'SLMC-1',
        'specialization': 'Haematology',
        'phoneNumber': '0771234567',
      })
      ..onDelete('/Doctors/d1', (s) => s.reply(200, {'success': true}))
      ..onPut('/profiles/doctor/d1', (s) => s.reply(409, {'message': 'Another doctor already uses this SLMC number.'}), data: {
        'firstName': 'Ann',
        'lastName': 'Perera',
        'phoneNumber': '0771234567',
        'specialization': '',
        'licenseNumber': 'SLMC-2',
      });
    final repo = DoctorRepository(api.client);

    await repo.create(
        'h1',
        const DoctorForm(
          firstName: ' Ann ',
          lastName: 'Perera',
          email: 'Ann.Perera@Example.test',
          password: 'Secret#123',
          licenseNumber: 'SLMC-1',
          specialization: 'Haematology',
          phoneNumber: '0771234567',
        ));
    await repo.remove('d1');
    await expectLater(
      repo.updateProfile('d1', const DoctorForm(firstName: 'Ann', lastName: 'Perera', phoneNumber: '0771234567', licenseNumber: 'SLMC-2')),
      throwsA(isA<ApiError>().having((e) => e.message, 'message', 'Another doctor already uses this SLMC number.')),
    );
  });
}
