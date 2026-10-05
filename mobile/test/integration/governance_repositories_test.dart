import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/core/notifications/phone_notifications.dart';
import 'package:lifelink_mobile/features/activity/activity_log.dart';
import 'package:lifelink_mobile/features/admin/admin_repository.dart';
import 'package:lifelink_mobile/features/admin/appeals/admin_appeals.dart';
import 'package:lifelink_mobile/features/admin/complaints/admin_complaints.dart';
import 'package:lifelink_mobile/features/admin/directory/admin_actions.dart';
import 'package:lifelink_mobile/features/admin/oversight/admin_activity_screen.dart';
import 'package:lifelink_mobile/features/admin/registrations/registration_repository.dart';
import 'package:lifelink_mobile/features/governance/governance_repository.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../helpers.dart';

/// Admin and governance repositories against a mocked HTTP layer, with the API's real response shapes.
void main() {
  test('attention counts are a background request; dashboard unwraps ApiResponse', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet('/Admin/attention-counts', (s) => s.reply(200, {'newBloodRequests': 1, 'pendingAppeals': 2, 'bloodRequestsSeenAt': '2026-10-05T04:00:00Z'}))
      ..onGet('/Admin/dashboard', (s) => s.reply(200, {'success': true, 'data': {'totalHospitals': 3, 'pendingAppeals': 2}}));
    final repo = AdminRepository(api.client);

    final a = await repo.attention(background: true);
    expect(a.pendingAppeals, 2);
    expect(a.bloodRequestsSeenAt, DateTime.utc(2026, 10, 5, 4));
    expect(api.sent.last.headers.containsKey('X-LifeLink-Activity'), isFalse);
    expect((await repo.dashboard()).totalHospitals, 3);
  });

  test('registrations: approve and reject send the last seen entry', () async {
    final api = MockedApi(token: 'jwt');
    final h = AdminHospital.fromJson({
      'hospitalId': 'h1',
      'approvalStatus': 'AwaitingAdminReview',
      'approvalHistory': [{'id': 'e9', 'type': 'HospitalReply'}],
    });
    api.adapter
      ..onPut('/Admin/hospitals/h1/approve', (s) => s.reply(409, {'message': 'The hospital replied again.'}), data: {'lastSeenEntryId': 'e9'})
      ..onPut('/Admin/hospitals/h1/reject', (s) => s.reply(200, {'success': true}),
          data: {'reason': 'Licence expired', 'reportDocumentName': null, 'reportDocumentUrl': null, 'lastSeenEntryId': 'e9'});
    final repo = RegistrationRepository(api.client);

    await expectLater(repo.approve(h), throwsA(predicate((e) => e.toString() == 'The hospital replied again.')));
    await repo.reject(h, 'Licence expired', null);
  });

  test('appeals: decisions and replies use the web payloads', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet('/Admin/appeals', (s) => s.reply(200, {'success': true, 'data': [{'appealId': 'a1', 'status': 'PENDING', 'canReject': true}]}),
          queryParameters: {'status': 'PENDING'})
      ..onPut('/Admin/appeals/a1/permanently-block', (s) => s.reply(200, {'success': true}), data: {'adminResponse': 'Repeated fraud'})
      ..onPut('/Admin/appeals/a1/reply', (s) => s.reply(200, {'success': true}),
          data: {'notes': 'Send proof', 'attachmentUrl': null, 'attachmentName': null});
    final repo = AdminAppealsRepository(api.client);

    expect((await repo.list('PENDING')).single.canReject, isTrue);
    await repo.decide('a1', AppealDecision.block.path, 'Repeated fraud');
    await repo.reply('a1', 'Send proof', null);
  });

  test('complaints: detail and admin reply', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet('/Admin/complaints/c1', (s) => s.reply(200, {
            'success': true,
            'data': {
              'complaintId': 'c1',
              'status': 'OPEN',
              'awaitingAdminReply': true,
              'activityReports': [{'reportId': 'r1', 'title': 'Weekly report', 'hospitalName': 'Fake General'}],
              'auditLogs': [{'auditId': 'l1', 'fromAdmin': true, 'isReply': true, 'notes': 'Looking into it'}],
            },
          }))
      ..onPut('/Admin/complaints/c1/review', (s) => s.reply(200, {'success': true}),
          data: {'notes': 'Resolved with the hospital', 'attachmentUrl': null, 'attachmentName': null});
    final repo = AdminComplaintsRepository(api.client);

    final c = await repo.byId('c1');
    expect(c.reports.single.title, 'Weekly report');
    expect(c.thread().length, 2);
    await repo.reply('c1', 'Resolved with the hospital', null);
  });

  test('oversight, activity logs, messages, suspension', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onPut('/Admin/transfers/t1/suspend', (s) => s.reply(200, {}), data: {'reason': 'Under review'})
      ..onPut('/Admin/attention/blood-requests/seen', (s) => s.reply(200, {}))
      ..onGet('/Admin/users/u1/activity-log', (s) => s.reply(200, {
            'items': [{'id': 'x', 'summary': 'Accepted a request', 'entityType': 'Donation', 'occurredAt': '2026-10-05T04:30:00Z'}],
            'total': 1,
            'page': 1,
            'pageSize': 10,
            'types': ['Donation'],
          }), queryParameters: {'page': 1, 'pageSize': 10, 'type': 'Donation'})
      ..onPost('/Admin/messages', (s) => s.reply(200, {}),
          data: {'userId': null, 'hospitalId': 'h1', 'subject': 'Documents', 'message': 'Please upload your licence.'})
      ..onPut('/Admin/users/u1/suspend', (s) => s.reply(200, {}), data: {'reason': 'Fake documents', 'suspendedUntil': null});

    final oversight = OversightRepository(api.client);
    await oversight.suspend(OversightKind.transfer, 't1', 'Under review');
    await oversight.markSeen(OversightKind.request);

    final page = await ActivityRepository(api.client).ofUser('u1', const ActivityQuery(type: 'Donation'));
    expect(page.items.single.summary, 'Accepted a request');

    final accounts = AdminAccountRepository(api.client);
    await accounts.message(hospitalId: 'h1', subject: 'Documents', message: 'Please upload your licence.');
    await accounts.suspend('users', 'u1', 'Fake documents', null);
  });

  test('governance status for a suspended user', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onGet('/governance/status', (s) => s.reply(200, {
          'success': true,
          'data': {
            'isSuspended': true,
            'suspensionReason': 'Fake documents',
            'suspendedEntity': 'User',
            'canAppeal': false,
            'allAppeals': [
              {'appealId': 'a1', 'status': 'PENDING', 'submittedAt': '2026-10-04T04:00:00Z'},
              {'appealId': 'a2', 'status': 'REJECTED', 'submittedAt': '2026-10-05T04:00:00Z', 'canAppellantReply': true},
            ],
            'profile': {'name': 'Test Person', 'email': 'test.person@example.test', 'role': 'User'},
          },
        }));
    final s = await GovernanceRepository(api.client).status();
    expect(s.suspensionReason, 'Fake documents');
    expect(s.newestFirst.first.id, 'a2');
    expect(s.newestFirst.first.canAppellantReply, isTrue);
  });

  group('background notification poll', () {
    setUp(() => SharedPreferences.setMockInitialValues({}));

    test('sends no activity header; the first run only records the newest', () async {
      final dio = Dio(BaseOptions(baseUrl: testBaseUrl));
      final sent = <RequestOptions>[];
      dio.interceptors.add(InterceptorsWrapper(onRequest: (o, h) {
        sent.add(o);
        h.next(o);
      }));
      DioAdapter(dio: dio).onGet('/notifications/my', (s) => s.reply(200, [
            {'notificationId': 'n1', 'title': 'Message from Administrator', 'notificationType': 'AdminMessage', 'createdAt': '2026-10-05T04:30:00Z'},
          ]));

      expect(await PhoneNotifications.instance.poll(token: 'jwt', dio: dio), isTrue);
      expect(sent.single.headers.containsKey('X-LifeLink-Activity'), isFalse);
      final prefs = await SharedPreferences.getInstance();
      expect(prefs.getString('notif_last_seen_id'), 'n1');
    });

    test('a 401 (session ended) stops the task', () async {
      final dio = Dio(BaseOptions(baseUrl: testBaseUrl));
      DioAdapter(dio: dio).onGet('/notifications/my', (s) => s.reply(401, {'message': 'expired'}));
      expect(await PhoneNotifications.instance.poll(token: 'jwt', dio: dio), isFalse);
    });
  });
}
