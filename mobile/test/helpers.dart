import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/core/api/api_client.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/core/auth/current_user.dart';
import 'package:lifelink_mobile/core/auth/session_activity.dart';
import 'package:lifelink_mobile/core/auth/token_storage.dart';
import 'package:lifelink_mobile/core/providers.dart';
import 'package:lifelink_mobile/core/theme/app_theme.dart';
import 'package:lifelink_mobile/features/notifications/notifications_repository.dart';

const testBaseUrl = 'http://test.local/api';

/// A clock the tests move by hand.
class FakeClock {
  FakeClock([DateTime? start]) : now = start ?? DateTime.utc(2026, 10, 5, 4, 30);
  DateTime now;
  DateTime call() => now;
  void advance(Duration d) => now = now.add(d);
}

/// An [ApiClient] whose HTTP layer is mocked, plus a log of the headers each request carried.
class MockedApi {
  MockedApi({String? token, FakeClock? clock})
      : clock = clock ?? FakeClock(),
        tokens = MemoryTokenStorage(token) {
    activity = SessionActivity(clock: this.clock.call);
    final dio = Dio();
    client = ApiClient(tokens: tokens, activity: activity, baseUrl: testBaseUrl, dio: dio);
    // Runs after the client's interceptor, so it sees the final headers
    dio.interceptors.add(InterceptorsWrapper(onRequest: (o, h) {
      sent.add(o);
      h.next(o);
    }));
    adapter = DioAdapter(dio: dio);
  }

  final FakeClock clock;
  final MemoryTokenStorage tokens;
  late final SessionActivity activity;
  late final ApiClient client;
  late final DioAdapter adapter;
  final List<RequestOptions> sent = [];

  List<Override> get overrides => [
        tokenStorageProvider.overrideWithValue(tokens),
        sessionActivityProvider.overrideWithValue(activity),
        apiClientProvider.overrideWithValue(client),
      ];
}

Map<String, dynamic> userJson({
  List<String> roles = const ['User'],
  bool suspended = false,
  bool mustChangePassword = false,
  String? hospitalApprovalStatus,
}) =>
    {
      'userId': '11111111-1111-1111-1111-111111111111',
      'firstName': 'Test',
      'lastName': 'Person',
      'email': 'test.person@example.test',
      'roles': roles,
      'accountStatus': suspended ? 'Suspended' : 'Active',
      'isSuspended': suspended,
      'mustChangePassword': mustChangePassword,
      'hospitalApprovalStatus': hospitalApprovalStatus,
      'sessionIdleTimeoutMinutes': 10,
      'sessionWarningMinutes': 1,
    };

CurrentUser testUser({
  List<String> roles = const ['User'],
  bool suspended = false,
  bool mustChangePassword = false,
  String? hospitalApprovalStatus,
}) =>
    CurrentUser.fromJson(userJson(
      roles: roles,
      suspended: suspended,
      mustChangePassword: mustChangePassword,
      hospitalApprovalStatus: hospitalApprovalStatus,
    ));

/// Auth controller with a fixed state (no restore, no API).
class FakeAuthController extends AuthController {
  FakeAuthController(this.initial);
  final AuthState initial;

  @override
  AuthState build() => initial;
}

/// Unread count without polling.
class FakeUnreadCount extends UnreadCountController {
  @override
  int build() => 0;
}

/// MaterialApp with the app theme around [child], inside a ProviderScope with [overrides].
Widget testApp(Widget child, {List<Override> overrides = const []}) => ProviderScope(
      overrides: overrides,
      retry: (_, _) => null,
      child: MaterialApp(theme: AppTheme.light(), home: child),
    );
