import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';
import 'package:lifelink_mobile/features/attention/tab_attention_controller.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_models.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_repository.dart';
import 'package:lifelink_mobile/features/blood_requests/create_request_screen.dart';
import 'package:lifelink_mobile/features/blood_requests/public_requests_screen.dart';
import 'package:lifelink_mobile/features/screening/screening_inputs.dart';
import 'package:lifelink_mobile/features/screening/screening_interview_screen.dart';
import 'package:lifelink_mobile/features/screening/screening_models.dart';
import 'package:lifelink_mobile/features/screening/screening_repository.dart';

import '../helpers.dart';

class _NoTabAttention extends TabAttentionController {
  @override
  TabAttentionState build() => const TabAttentionState();

  @override
  Future<void> refresh() async {}
}

class _FailThenConfirmScreeningRepository extends ScreeningRepository {
  _FailThenConfirmScreeningRepository() : super(MockedApi().client);

  int calls = 0;
  int submittedPayloads = 0;

  @override
  Future<AssistantReply> chat(
    String acceptanceId, {
    String message = '',
    Map<String, dynamic>? structured,
  }) async {
    calls++;
    if (calls == 1) {
      return AssistantReply(
        'Please answer the current question.',
        ScreeningSession(
          'InProgress',
          false,
          1,
          1,
          'Health',
          0,
          1,
          const ScreeningQuestion(
            'Q1',
            1,
            'Health',
            'Are you feeling well?',
            'text',
            false,
            false,
            [],
            [],
          ),
          const [],
        ),
      );
    }
    if (message.isNotEmpty || structured != null) {
      submittedPayloads++;
      throw const ApiError('The response was lost.');
    }
    return const AssistantReply(
      'Your screening is complete.',
      ScreeningSession('Submitted', true, 1, 1, 'Health', 1, 1, null, [
        (question: 'Are you feeling well?', answer: 'Yes'),
      ]),
    );
  }
}

void main() {
  testWidgets('public requests show API data and filters', (tester) async {
    final item = BloodRequest.fromJson({
      'bloodRequestId': 'r1',
      'hospitalName': 'Central Hospital',
      'bloodGroup': 'AB-',
      'unitsRequired': 3,
      'fulfilledUnits': 1,
      'status': 'Verified',
      'reason': 'Surgery',
    });
    await tester.pumpWidget(
      testApp(
        const PublicRequestsScreen(),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
          publicBloodRequestsProvider.overrideWith(
            (ref, filter) async => [item],
          ),
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Central Hospital'), findsOneWidget);
    expect(find.text('Expiring soon'), findsNothing);
    expect(find.textContaining('Open until'), findsNothing);
    expect(find.text('AB-'), findsWidgets);
    expect(find.text('Create request'), findsNothing);
  });

  testWidgets('embedded create request reuses the form and request history', (
    tester,
  ) async {
    await tester.pumpWidget(
      testApp(
        const CreateRequestScreen(embedded: true),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
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
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Create blood request'), findsOneWidget);
    expect(find.text('New blood request'), findsOneWidget);
    expect(find.text('My requests'), findsOneWidget);
    expect(find.byType(AppBar), findsNothing);
  });

  testWidgets('embedded create header stays at the top while data loads', (
    tester,
  ) async {
    final hospitals = Completer<List<HospitalChoice>>();
    final requests = Completer<List<BloodRequest>>();
    await tester.pumpWidget(
      testApp(
        const CreateRequestScreen(embedded: true),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
          hospitalChoicesProvider.overrideWith((ref) => hospitals.future),
          myBloodRequestsProvider.overrideWith((ref) => requests.future),
        ],
      ),
    );
    await tester.pump();

    expect(find.byType(CircularProgressIndicator), findsWidgets);
    expect(
      tester.getTopLeft(find.text('Create blood request')).dy,
      lessThan(100),
    );
  });

  testWidgets('screening renders structured choices and unknown date', (
    tester,
  ) async {
    final values = <String, dynamic>{};
    await tester.pumpWidget(
      testApp(
        StatefulBuilder(
          builder: (context, setState) => Scaffold(
            body: ScreeningInputs(
              parts: const [
                ScreeningPart(
                  'OK',
                  'Are you well?',
                  'yes_no',
                  ['Yes', 'No'],
                  true,
                  false,
                  false,
                  false,
                  null,
                  null,
                  null,
                  null,
                ),
                ScreeningPart(
                  'DATE',
                  'Last donation',
                  'date',
                  [],
                  true,
                  true,
                  false,
                  false,
                  null,
                  null,
                  null,
                  null,
                ),
              ],
              values: values,
              onChanged: (id, value) => setState(() => values[id] = value),
            ),
          ),
        ),
      ),
    );
    expect(find.text('Are you well?'), findsOneWidget);
    expect(find.text('I don\'t remember'), findsOneWidget);
    await tester.tap(find.text('Yes'));
    await tester.pump();
    expect(values['OK'], 'Yes');
  });

  testWidgets(
    'failed screening answer is retained and retry reconciles first',
    (tester) async {
      final repository = _FailThenConfirmScreeningRepository();
      await tester.pumpWidget(
        testApp(
          const ScreeningInterviewScreen(acceptanceId: 'a1'),
          overrides: [
            screeningRepositoryProvider.overrideWithValue(repository),
            tabAttentionProvider.overrideWith(_NoTabAttention.new),
          ],
        ),
      );
      await tester.pumpAndSettle();

      final input = find.byType(TextField);
      await tester.enterText(input, 'Yes');
      await tester.tap(find.byType(IconButton));
      await tester.pumpAndSettle();

      expect(find.text('Retry answer'), findsOneWidget);
      expect(tester.widget<TextField>(input).controller!.text, 'Yes');
      expect(repository.submittedPayloads, 1);

      await tester.tap(find.text('Retry answer'));
      await tester.pumpAndSettle();

      expect(repository.submittedPayloads, 1);
      expect(repository.calls, 3);
      expect(
        find.text('Your answers were sent to the doctor.'),
        findsOneWidget,
      );
    },
  );
}
