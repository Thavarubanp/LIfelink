import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';
import 'package:lifelink_mobile/core/api/idempotency.dart';
import 'package:lifelink_mobile/core/api/json.dart';

DioException _response(int status, Object? data) {
  final options = RequestOptions(path: '/x');
  return DioException.badResponse(statusCode: status, requestOptions: options, response: Response(requestOptions: options, statusCode: status, data: data));
}

void main() {
  group('ApiError.from', () {
    test('400 ValidationProblemDetails: first field error, camelCase keys', () {
      final e = ApiError.from(_response(400, {
        'title': 'One or more validation errors occurred.',
        'errors': {
          'Email': ['Invalid email address format.'],
          'request.FirstName': ['First name is required.'],
        },
      }));
      expect(e.message, 'Invalid email address format.');
      expect(e.fieldErrors, {'email': 'Invalid email address format.', 'firstName': 'First name is required.'});
    });

    test('ApiResponse message wins', () {
      expect(ApiError.from(_response(400, {'success': false, 'message': 'Units must be 1-20.'})).message, 'Units must be 1-20.');
      expect(ApiError.from(_response(403, {'message': 'Only the hospital that raised this emergency can update it.'})).isForbidden, isTrue);
    });

    test('fallback messages per status', () {
      expect(ApiError.from(_response(409, null)).message, contains('same moment'));
      expect(ApiError.from(_response(500, null)).message, contains('HTTP 500'));
      expect(ApiError.from(_response(502, '<!DOCTYPE html>')).isNetwork, isTrue);
    });

    test('timeouts and connection errors', () {
      final o = RequestOptions(path: '/x');
      expect(ApiError.from(DioException.receiveTimeout(timeout: Duration.zero, requestOptions: o)).message, ApiError.timedOut);
      expect(ApiError.from(DioException.connectionError(requestOptions: o, reason: 'x')).message, ApiError.noConnection);
    });
  });

  group('json helpers', () {
    test('unwrap handles both response shapes', () {
      expect(unwrapList({'success': true, 'message': 'ok', 'data': [{'a': 1}]}), [{'a': 1}]);
      expect(unwrapList([{'a': 2}]), [{'a': 2}]);
      expect(unwrapMap({'success': true, 'data': {'count': 3}}), {'count': 3});
      expect(unwrapMap({'count': 4}), {'count': 4});
      expect(unwrapList(null), isEmpty);
    });

    test('dates without an offset are UTC', () {
      expect(parseDate('2026-10-05T04:30:00'), DateTime.utc(2026, 10, 5, 4, 30));
      expect(parseDate('2026-10-05T04:30:00Z'), DateTime.utc(2026, 10, 5, 4, 30));
      expect(parseDate('2026-10-05T10:00:00+05:30'), DateTime.utc(2026, 10, 5, 4, 30));
      expect(parseDate(null), isNull);
    });
  });

  test('idempotency keys are random UUID v4 values', () {
    final a = newIdempotencyKey();
    expect(a, matches(RegExp(r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')));
    expect(newIdempotencyKey(), isNot(a));
  });
}
