import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/core/api/api_client.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';

import '../helpers.dart';

void main() {
  group('ApiClient headers', () {
    test('sends the JWT and the activity header on a user-driven request', () async {
      final api = MockedApi(token: 'jwt-1');
      api.adapter.onGet('/Inventory', (s) => s.reply(200, {'success': true, 'data': []}));

      await api.client.get('/Inventory');

      final h = api.sent.single.headers;
      expect(h['Authorization'], 'Bearer jwt-1');
      expect(h['X-LifeLink-Activity'], '1');
    });

    test('background requests never carry the activity header', () async {
      final api = MockedApi(token: 'jwt-1');
      api.adapter.onGet('/notifications/unread-count', (s) => s.reply(200, {'count': 2}));

      await api.client.get('/notifications/unread-count', options: apiOptions(background: true));

      expect(api.sent.single.headers.containsKey('X-LifeLink-Activity'), isFalse);
      expect(api.activity.lastConfirmedMs, 0);
    });

    test('reports activity at most every 15 seconds, then again after the interval', () async {
      final api = MockedApi(token: 'jwt-1');
      api.adapter.onGet('/a', (s) => s.reply(200, {}));

      await api.client.get('/a');
      api.clock.advance(const Duration(seconds: 5));
      await api.client.get('/a');
      api.clock.advance(const Duration(seconds: 11));
      await api.client.get('/a');

      expect(api.sent.map((o) => o.headers.containsKey('X-LifeLink-Activity')).toList(), [true, false, true]);
      expect(api.activity.lastConfirmedMs, api.clock.now.millisecondsSinceEpoch);
    });

    test('reportActivity always reports (heartbeat)', () async {
      final api = MockedApi(token: 'jwt-1');
      api.adapter.onPost('/Auth/activity', (s) => s.reply(200, {}));

      await api.client.post('/Auth/activity', options: apiOptions(reportActivity: true));
      await api.client.post('/Auth/activity', options: apiOptions(reportActivity: true));

      expect(api.sent.every((o) => o.headers['X-LifeLink-Activity'] == '1'), isTrue);
    });

    test('no token: no Authorization and no activity header', () async {
      final api = MockedApi();
      api.adapter.onPost('/Auth/login', (s) => s.reply(200, {}), data: Matchers.any);

      await api.client.post('/Auth/login', body: {'email': 'a@example.test'});

      expect(api.sent.single.headers.containsKey('Authorization'), isFalse);
      expect(api.sent.single.headers.containsKey('X-LifeLink-Activity'), isFalse);
    });

    test('sends the idempotency key of the form', () async {
      final api = MockedApi(token: 'jwt-1');
      api.adapter.onPost('/Inventory/packets', (s) => s.reply(200, {'success': true, 'data': []}), data: Matchers.any);

      await api.client.post('/Inventory/packets', body: {}, options: apiOptions(idempotencyKey: 'key-123'));

      expect(api.sent.single.headers['Idempotency-Key'], 'key-123');
    });
  });

  group('ApiClient errors', () {
    test('401 on an authenticated request signs out with the idle reason from X-Session-Ended', () async {
      final api = MockedApi(token: 'jwt-1');
      SignOutReason? reason;
      api.client.onUnauthorized = (r) => reason = r;
      api.adapter.onGet('/Auth/me', (s) => s.reply(401, {'message': 'Session ended'}, headers: {
            'x-session-ended': ['idle'],
            'content-type': ['application/json'],
          }));

      await expectLater(api.client.get('/Auth/me'), throwsA(isA<ApiError>().having((e) => e.statusCode, 'status', 401)));
      expect(reason, SignOutReason.idle);
    });

    test('401 without the header is an expired session', () async {
      final api = MockedApi(token: 'jwt-1');
      SignOutReason? reason;
      api.client.onUnauthorized = (r) => reason = r;
      api.adapter.onGet('/Auth/me', (s) => s.reply(401, {}));

      await expectLater(api.client.get('/Auth/me'), throwsA(isA<ApiError>()));
      expect(reason, SignOutReason.expired);
    });

    test('409 keeps the API message and counts as confirmed activity', () async {
      final api = MockedApi(token: 'jwt-1');
      api.adapter.onPut('/transfers/1/approve', (s) => s.reply(409, {'message': 'Someone already accepted this transfer.'}),
          data: Matchers.any);

      final error = await api.client.put('/transfers/1/approve', body: {}).then<ApiError?>((_) => null, onError: (e) => e as ApiError);

      expect(error!.isConflict, isTrue);
      expect(error.message, 'Someone already accepted this transfer.');
      expect(api.activity.lastConfirmedMs, greaterThan(0));
    });

    test('connection failure is a network error and confirms no activity', () async {
      final api = MockedApi(token: 'jwt-1');
      api.adapter.onGet('/x', (s) => s.throws(0, DioException.connectionError(requestOptions: RequestOptions(path: '/x'), reason: 'down')));

      final error = await api.client.get('/x').then<ApiError?>((_) => null, onError: (e) => e as ApiError);

      expect(error!.isNetwork, isTrue);
      expect(error.message, ApiError.noConnection);
      expect(api.activity.lastConfirmedMs, 0);
    });
  });
}
