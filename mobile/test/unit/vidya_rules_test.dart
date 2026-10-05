import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_models.dart';
import 'package:lifelink_mobile/features/screening/screening_models.dart';

void main() {
  test('blood request derives remaining units and active state', () {
    final request = BloodRequest.fromJson({
      'bloodRequestId': 'r1', 'bloodGroup': 'O-', 'unitsRequired': 5,
      'fulfilledUnits': 2, 'reservedUnits': 1, 'status': 'Verified',
    });
    expect(request.remainingUnits, 3);
    expect(request.isActive, isTrue);
  });

  test('screening conditional parts and structured answers follow questionnaire rules', () {
    const part = ScreeningPart('DETAIL', 'Details', 'text', [], true, false, false, false,
      {'field': 'HAS', 'equals': 'Yes'}, null, null, null);
    expect(screeningPartApplies(part, {'HAS': 'No'}), isFalse);
    expect(screeningPartApplies(part, {'HAS': 'Yes'}), isTrue);
    expect(collectScreeningAnswers([part], {'HAS': 'Yes', 'DETAIL': 'Answer'}), {'DETAIL': 'Answer'});
  });

  test('trip answer needs country and return date', () {
    const trips = ScreeningPart('TRIPS', 'Trips', 'trips', [], true, false, false, false, null, null, null, null);
    expect(screeningAnswered(trips, [{'country': 'India', 'return_date': ''}]), isFalse);
    expect(screeningAnswered(trips, [{'country': 'India', 'return_date': '2026-09-01'}]), isTrue);
  });
}
