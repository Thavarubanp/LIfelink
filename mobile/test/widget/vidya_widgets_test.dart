import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_models.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_repository.dart';
import 'package:lifelink_mobile/features/blood_requests/public_requests_screen.dart';
import 'package:lifelink_mobile/features/screening/screening_inputs.dart';
import 'package:lifelink_mobile/features/screening/screening_models.dart';

import '../helpers.dart';

void main() {
  testWidgets('public requests show API data and filters', (tester) async {
    final item = BloodRequest.fromJson({'bloodRequestId': 'r1', 'hospitalName': 'Central Hospital',
      'bloodGroup': 'AB-', 'unitsRequired': 3, 'fulfilledUnits': 1, 'status': 'Verified', 'reason': 'Surgery'});
    await tester.pumpWidget(testApp(const PublicRequestsScreen(), overrides: [
      authControllerProvider.overrideWith(() => FakeAuthController(AuthState.signedIn(testUser()))),
      publicBloodRequestsProvider.overrideWith((ref, filter) async => [item]),
    ]));
    await tester.pumpAndSettle();
    expect(find.text('Central Hospital'), findsOneWidget);
    expect(find.text('Expiring soon'), findsOneWidget);
    expect(find.text('AB-'), findsWidgets);
  });

  testWidgets('screening renders structured choices and unknown date', (tester) async {
    final values = <String, dynamic>{};
    await tester.pumpWidget(testApp(StatefulBuilder(builder: (context, setState) => Scaffold(body: ScreeningInputs(
      parts: const [
        ScreeningPart('OK', 'Are you well?', 'yes_no', ['Yes', 'No'], true, false, false, false, null, null, null, null),
        ScreeningPart('DATE', 'Last donation', 'date', [], true, true, false, false, null, null, null, null),
      ], values: values, onChanged: (id, value) => setState(() => values[id] = value),
    )))));
    expect(find.text('Are you well?'), findsOneWidget);
    expect(find.text('I don\'t remember'), findsOneWidget);
    await tester.tap(find.text('Yes'));
    await tester.pump();
    expect(values['OK'], 'Yes');
  });
}
