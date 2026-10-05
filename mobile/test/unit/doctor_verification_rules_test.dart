import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/utils/contact_launcher.dart';
import 'package:lifelink_mobile/features/doctor/screening_models.dart';
import 'package:lifelink_mobile/features/doctors/doctor_repository.dart';
import 'package:lifelink_mobile/features/verification/verification_models.dart';

import '../helpers.dart';

ClinicalRequest req(String status, {bool suspended = false, int units = 3, int fulfilled = 1, int reserved = 1}) => ClinicalRequest.fromJson({
      'bloodRequestId': '12345678-aaaa',
      'bloodGroup': 'O-',
      'unitsRequired': units,
      'fulfilledUnits': fulfilled,
      'reservedUnits': reserved,
      'priority': 'Critical',
      'reason': 'Surgery',
      'status': status,
      'createdByName': 'Test Person',
      'assignedDoctorName': 'Removed doctor',
      'isSuspended': suspended,
    });

ScreeningReport report({
  String acceptance = 'a1',
  int version = 1,
  String status = 'Pending',
  String acceptanceStatus = 'ScreeningCompleted',
  String? risk,
  bool mine = false,
  bool freeSlot = true,
  String created = '2026-10-05T04:00:00Z',
}) =>
    ScreeningReport.fromJson({
      'donorVerificationId': '$acceptance-v$version',
      'acceptanceId': acceptance,
      'reportVersion': version,
      'status': status,
      'acceptanceStatus': acceptanceStatus,
      'riskLevel': risk,
      'isAssignedToMe': mine,
      'hasFreeSlot': freeSlot,
      'createdAt': created,
      'donorName': 'Donor $acceptance',
      'reportJson': reportJson(),
    });

void main() {
  group('requests', () {
    test('hospital verifies Pending only; doctor decides Verified only; suspended is frozen', () {
      expect(req('Pending').canVerify, isTrue);
      expect(req('Pending', suspended: true).canVerify, isFalse);
      expect(req('Verified').canVerify, isFalse);
      expect(req('Verified').canDoctorDecide, isTrue);
      expect(req('Verified', suspended: true).canDoctorDecide, isFalse);
      expect(req('Approved').canDoctorDecide, isFalse);
      expect(req('Approved').freeSlots, 1);
      expect(req('Approved', units: 2, fulfilled: 1, reserved: 3).freeSlots, 0);
    });

    test('status filter and search (incl. a removed doctor)', () {
      final r = req('Pending');
      expect(requestMatches(r, status: 'Pending'), isTrue);
      expect(requestMatches(r, status: 'Verified'), isFalse);
      expect(requestMatches(r, query: '12345678'), isTrue);
      expect(requestMatches(r, query: 'removed doctor'), isTrue);
      expect(requestMatches(r, query: 'zzz'), isFalse);
    });

    test('a reason or message is required (max 500)', () {
      expect(requiredReasonError('  '), 'A reason is required.');
      expect(requiredReasonError('x' * 501), isNotNull);
      expect(requiredReasonError('Insufficient documentation'), isNull);
    });
  });

  group('doctors', () {
    Doctor doc({bool mustChange = false, bool active = true, String? userId = 'u1'}) =>
        Doctor.fromJson({'doctorId': 'd1', 'firstName': 'Ann', 'lastName': 'Perera', 'isActive': active, 'mustChangePassword': mustChange, 'userId': userId});

    test('only doctors who finished their first sign-in can be assigned (API rule)', () {
      expect(doc().canBeAssigned, isTrue);
      expect(doc(mustChange: true).canBeAssigned, isFalse);
      expect(doc(active: false).canBeAssigned, isFalse);
      expect(doc(userId: null).canBeAssigned, isFalse);
      expect(doc().name, 'Dr. Ann Perera');
    });

    test('add doctor: DTO rules and the duplicate SLMC check', () {
      const ok = DoctorForm(
          firstName: 'Ann', lastName: 'Perera', phoneNumber: '0771234567', licenseNumber: 'SLMC-1', email: 'ann@example.test', password: 'Secret#123');
      expect(ok.errors(), isEmpty);
      expect(ok.errors(existingSlmc: ['slmc-1'])['licenseNumber'], 'A doctor with this SLMC number already exists.');
      const bad = DoctorForm(firstName: '', lastName: 'P', phoneNumber: '07712', licenseNumber: '', email: 'x', password: 'weak');
      expect(bad.errors().keys, containsAll(['firstName', 'phoneNumber', 'licenseNumber', 'email', 'password']));
      // Profile edit: no email or password fields
      expect(const DoctorForm(firstName: 'Ann', lastName: 'P', phoneNumber: '0771234567', licenseNumber: 'S').errors(isNew: false), isEmpty);
    });
  });

  group('acceptances', () {
    RequestAcceptance acc(String status, {String? hospitalId}) =>
        RequestAcceptance.fromJson({'acceptanceId': 'x', 'status': status, 'donorHospitalId': hospitalId, 'donorName': 'D', 'packets': [{'trackingNumber': 'PKT-00000001'}]});

    test('hospital donations, reserved donors and labels', () {
      expect(acc('Accepted', hospitalId: 'h2').awaitsHospitalDecision, isTrue);
      expect(acc('Accepted', hospitalId: 'h2').statusLabel, 'Awaiting doctor approval');
      expect(acc('Accepted').awaitsHospitalDecision, isFalse);
      expect(acc('Verified').isReserved, isTrue);
      expect(acc('Verified', hospitalId: 'h2').isReserved, isFalse);
      expect(acc('Matched').statusLabel, 'Donated');
      expect(acc('Accepted', hospitalId: 'h2').packets.single.trackingNumber, 'PKT-00000001');
    });
  });

  group('screening reports (agent recommendation + doctor decision)', () {
    test('flags: "Likely deferral – doctor to confirm" or "Review"; flagged sections highlighted', () {
      final content = ScreeningReportContent.tryParse(reportJson(flags: [
        {'code': 'AGE_RANGE', 'severity': 'defer', 'section': 1, 'message': 'Age 66 is outside 18 to 60.'},
        {'code': 'MEDICATION', 'severity': 'review', 'section': 2, 'message': 'Takes medicine.'},
      ]))!;
      expect(content.riskLevel, 'HIGH');
      expect(content.flags.first.label, 'Likely deferral – doctor to confirm');
      expect(content.flags.last.label, 'Review');
      expect(content.isHighlighted(content.sections[0]), isTrue);
      expect(content.isHighlighted(content.sections[2]), isFalse);
      expect(content.sections[2].confidential, isTrue);
      expect(ScreeningReportContent.tryParse(null), isNull);
      expect(ScreeningReportContent.tryParse('{not json'), isNull);
    });

    test('only the newest Pending version of a completed screening can be decided', () {
      expect(report().canDecide, isTrue);
      expect(report(status: 'Superseded').canDecide, isFalse);
      expect(report(status: 'Approved', acceptanceStatus: 'Verified').canDecide, isFalse);
      expect(report(acceptanceStatus: 'ScreeningPending').canDecide, isFalse);
    });

    test('queue tabs: mine first, then highest risk, then oldest; versions grouped per donor', () {
      final tabs = groupReports([
        report(acceptance: 'low', risk: 'LOW', created: '2026-10-05T01:00:00Z'),
        report(acceptance: 'high', risk: 'HIGH', created: '2026-10-05T03:00:00Z'),
        report(acceptance: 'mine', risk: 'LOW', mine: true),
        report(acceptance: 'v', version: 1, status: 'Superseded'),
        report(acceptance: 'v', version: 2, risk: 'MEDIUM'),
        report(acceptance: 'approved', status: 'Approved', acceptanceStatus: 'Verified'),
        report(acceptance: 'done', status: 'Rejected', acceptanceStatus: 'Rejected'),
      ]);
      expect(tabs[ReportTab.review]!.map((g) => g.latest.acceptanceId), ['mine', 'high', 'v', 'low']);
      expect(tabs[ReportTab.review]!.firstWhere((g) => g.latest.acceptanceId == 'v').versions.length, 2);
      expect(tabs[ReportTab.awaiting]!.single.latest.acceptanceId, 'approved');
      expect(tabs[ReportTab.history]!.single.latest.acceptanceId, 'done');
    });
  });

  group('contact links (device feature)', () {
    test('tel and mailto links', () {
      expect(ContactLauncher.phoneUri('077 123-4567').toString(), 'tel:0771234567');
      expect(ContactLauncher.phoneUri('12'), isNull);
      expect(ContactLauncher.emailUri('donor@example.test', subject: 'LifeLink blood donation').toString(),
          'mailto:donor@example.test?subject=LifeLink%20blood%20donation');
      expect(ContactLauncher.emailUri('not-an-email'), isNull);
    });
  });
}
