import 'package:dio/dio.dart';

import '../auth/session_activity.dart';
import '../auth/token_storage.dart';
import '../config/env.dart';
import 'api_error.dart';

/// Why the session ended: shown on the sign-in screen.
enum SignOutReason { signedOut, idle, expired }

/// Request options understood by the client (same meaning as in the web app):
/// - [background]: automatic refresh/polling; never counts as user activity for the idle timeout
/// - [reportActivity]: always report activity (heartbeat, "Stay signed in")
/// - [idempotencyKey]: one key per form submission; a double submit creates nothing twice
Options apiOptions({bool background = false, bool reportActivity = false, String? idempotencyKey, Duration? receiveTimeout}) =>
    Options(
      extra: {
        'background': background,
        'reportActivity': reportActivity,
        'idempotencyKey': ?idempotencyKey,
      },
      receiveTimeout: receiveTimeout,
    );

/// The one HTTP client for the LifeLink API: base URL, JWT, activity header, idempotency key, timeouts and
/// central 401 handling. Every method returns the response body or throws [ApiError].
class ApiClient {
  ApiClient({
    required this.tokens,
    required this.activity,
    String? baseUrl,
    Dio? dio,
  }) : dio = dio ?? Dio() {
    this.dio.options
      ..baseUrl = baseUrl ?? Env.apiBaseUrl
      ..connectTimeout = const Duration(seconds: 15)
      ..receiveTimeout = const Duration(seconds: 30)
      ..sendTimeout = const Duration(seconds: 30)
      ..headers = {'Content-Type': 'application/json', 'Accept': 'application/json'};
    this.dio.interceptors.add(InterceptorsWrapper(onRequest: _onRequest, onResponse: _onResponse, onError: _onError));
  }

  final Dio dio;
  final TokenStorage tokens;
  final SessionActivity activity;

  /// Called once when an authenticated request gets 401 (idle timeout, expired or ended session).
  void Function(SignOutReason reason)? onUnauthorized;

  void _onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final token = tokens.token;
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
      final background = options.extra['background'] == true;
      final report = options.extra['reportActivity'] == true;
      // User-driven requests keep the session alive (reported at most every 15 seconds)
      if (!background && (report || activity.takeMark())) {
        options.headers[SessionActivity.activityHeader] = '1';
        options.extra['activitySentAt'] = activity.nowMs;
      }
    }
    final key = options.extra['idempotencyKey'];
    if (key is String && key.isNotEmpty) options.headers['Idempotency-Key'] = key;
    handler.next(options);
  }

  void _onResponse(Response<dynamic> response, ResponseInterceptorHandler handler) {
    final sentAt = response.requestOptions.extra['activitySentAt'];
    if (sentAt is int) activity.markConfirmed(sentAt);
    handler.next(response);
  }

  void _onError(DioException error, ErrorInterceptorHandler handler) {
    final status = error.response?.statusCode;
    final sentAt = error.requestOptions.extra['activitySentAt'];
    if (sentAt is int) {
      // Any answer other than 401 means the API accepted the session (and recorded the activity)
      if (status != null && status != 401) {
        activity.markConfirmed(sentAt);
      } else {
        activity.releaseMark();
      }
    }
    if (status == 401 && error.requestOptions.headers.containsKey('Authorization')) {
      final ended = error.response?.headers.value(SessionActivity.sessionEndedHeader);
      onUnauthorized?.call(ended == 'idle' ? SignOutReason.idle : SignOutReason.expired);
    }
    handler.next(error);
  }

  Future<Object?> get(String path, {Map<String, dynamic>? query, Options? options}) =>
      _send(() => dio.get<Object?>(path, queryParameters: _clean(query), options: options));

  Future<Object?> post(String path, {Object? body, Map<String, dynamic>? query, Options? options}) =>
      _send(() => dio.post<Object?>(path, data: body, queryParameters: _clean(query), options: options));

  Future<Object?> put(String path, {Object? body, Map<String, dynamic>? query, Options? options}) =>
      _send(() => dio.put<Object?>(path, data: body, queryParameters: _clean(query), options: options));

  Future<Object?> patch(String path, {Object? body, Options? options}) =>
      _send(() => dio.patch<Object?>(path, data: body, options: options));

  Future<Object?> delete(String path, {Options? options}) => _send(() => dio.delete<Object?>(path, options: options));

  Future<Object?> _send(Future<Response<Object?>> Function() call) async {
    try {
      final response = await call();
      return response.data;
    } catch (e) {
      throw ApiError.from(e);
    }
  }

  static Map<String, dynamic>? _clean(Map<String, dynamic>? query) {
    if (query == null) return null;
    final result = Map<String, dynamic>.from(query)..removeWhere((_, v) => v == null || (v is String && v.isEmpty));
    return result.isEmpty ? null : result;
  }
}
