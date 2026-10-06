import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_repository.dart';
import 'package:lifelink_mobile/features/complaints/complaints_repository.dart';
import 'package:lifelink_mobile/features/screening/screening_repository.dart';

import '../helpers.dart';

void main() {
  test(
    'public requests send filters and accepting returns the acceptance',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onGet(
          '/BloodRequests/public',
          (s) => s.reply(200, [
            {
              'bloodRequestId': 'r1',
              'bloodGroup': 'A+',
              'unitsRequired': 2,
              'status': 'Verified',
            },
          ]),
          queryParameters: {'bloodGroup': 'A+'},
        )
        ..onPost(
          '/Acceptances',
          (s) => s.reply(200, {
            'acceptanceId': 'a1',
            'bloodRequestId': 'r1',
            'status': 'Accepted',
          }),
          data: {'bloodRequestId': 'r1', 'donorBloodGroup': 'A+'},
        );
      final repo = BloodRequestRepository(api.client);
      expect((await repo.publicRequests(bloodGroup: 'A+')).single.id, 'r1');
      expect((await repo.accept('r1', 'A+')).id, 'a1');
    },
  );

  test('accept eligibility errors preserve the API message', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onPost(
      '/Acceptances',
      (s) => s.reply(400, {'message': 'You are not yet eligible to donate.'}),
      data: {'bloodRequestId': 'r1', 'donorBloodGroup': 'O+'},
    );
    await expectLater(
      BloodRequestRepository(api.client).accept('r1', 'O+'),
      throwsA(
        isA<ApiError>().having(
          (e) => e.message,
          'message',
          contains('not yet eligible'),
        ),
      ),
    );
  });

  test(
    'request create, edit, cancel and delete use the React endpoints',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onPost(
          '/BloodRequests',
          (s) => s.reply(201, {'bloodRequestId': 'r1'}),
          data: {
            'hospitalId': 'h1',
            'bloodGroup': 'B+',
            'unitsRequired': 2,
            'priority': 'High',
            'reason': 'Surgery',
            'doctorId': null,
          },
        )
        ..onPut(
          '/BloodRequests/r1',
          (s) => s.reply(200, {'bloodRequestId': 'r1'}),
          data: {'bloodGroup': 'O+', 'unitsRequired': 3},
        )
        ..onPut(
          '/BloodRequests/r1/cancel',
          (s) => s.reply(200, {'bloodRequestId': 'r1'}),
        )
        ..onDelete('/BloodRequests/r1', (s) => s.reply(204, null));
      final repo = BloodRequestRepository(api.client);
      await repo.create(
        hospitalId: 'h1',
        bloodGroup: 'B+',
        units: 2,
        priority: 'High',
        reason: 'Surgery',
        idempotencyKey: 'form-1',
      );
      expect(api.sent.first.headers['Idempotency-Key'], 'form-1');
      await repo.update('r1', bloodGroup: 'O+', units: 3);
      await repo.cancel('r1');
      await repo.delete('r1');
    },
  );

  test('assistant chat includes acceptance and structured answers', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onPost(
      '/assistant/chat',
      (s) => s.reply(200, {
        'reply': 'Saved',
        'screening': {'status': 'ScreeningPending'},
      }),
      data: {
        'message': '',
        'history': [],
        'acceptanceId': 'a1',
        'structured': {'BLOOD': 'O-'},
      },
    );
    final reply = await ScreeningRepository(api.client)
        .chat('a1', structured: {'BLOOD': 'O-'});
    expect(reply.reply, 'Saved');
  });

  test(
    'acceptance answers update and withdrawal use workflow endpoints',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onGet(
          '/Acceptances/a1/screening-answers',
          (s) => s.reply(200, {
            'acceptanceId': 'a1',
            'status': 'Pending',
            'canEdit': true,
            'answers': {'CONFIRM_TRUE': 'Yes'},
            'sections': [],
          }),
        )
        ..onPut(
          '/Acceptances/a1/screening-answers',
          (s) => s.reply(200, {'acceptanceId': 'a1'}),
          data: {
            'answers': {'CONFIRM_TRUE': 'Yes'},
          },
        )
        ..onPut(
          '/Acceptances/a1/cancel',
          (s) => s.reply(200, {'acceptanceId': 'a1'}),
        );
      final repo = ScreeningRepository(api.client);
      expect((await repo.answers('a1')).canEdit, isTrue);
      await repo.updateAnswers('a1', {'CONFIRM_TRUE': 'Yes'});
      await repo.withdraw('a1');
    },
  );

  test(
    'suspended donor withdrawal uses only the narrow workflow endpoints',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onGet(
          '/Acceptances/my-active-withdrawals',
          (s) => s.reply(200, [
            {
              'acceptanceId': 'a1',
              'bloodRequestId': 'r1',
              'status': 'Verified',
            },
          ]),
        )
        ..onPut(
          '/Acceptances/a1/suspended-withdraw',
          (s) => s.reply(200, {'acceptanceId': 'a1'}),
        );
      final repo = ScreeningRepository(api.client);
      expect((await repo.activeWithdrawals()).single.status, 'Verified');
      await repo.suspendedWithdraw('a1');
      expect(api.sent.map((r) => r.path), [
        '/Acceptances/my-active-withdrawals',
        '/Acceptances/a1/suspended-withdraw',
      ]);
    },
  );

  test(
    'complaints create with idempotency and send attachment reply',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onPost(
          '/Complaints',
          (s) => s.reply(201, {
            'data': {'complaintId': 'c1'},
          }),
          data: {
            'complaintType': 'Technical Issue',
            'subject': 'Broken page',
            'description': 'The page does not load.',
            'hospitalId': null,
            'targetUserId': null,
          },
        )
        ..onPost(
          '/Complaints/c1/reply',
          (s) => s.reply(200, {'success': true}),
          data: {
            'notes': 'More details',
            'attachmentUrl': null,
            'attachmentName': null,
          },
        );
      final repo = ComplaintsRepository(api.client);
      await repo.create(
        type: 'Technical Issue',
        subject: 'Broken page',
        description: 'The page does not load.',
        idempotencyKey: 'key-1',
      );
      expect(api.sent.first.headers['Idempotency-Key'], 'key-1');
      await repo.reply('c1', 'More details', null);
    },
  );
}
