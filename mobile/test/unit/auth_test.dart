import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/core/api/api_client.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/core/auth/auth_repository.dart';
import 'package:lifelink_mobile/core/providers.dart';

import '../helpers.dart';

void main() {
  test('donor capability includes User and Admin but excludes clinical/staff roles', () {
    expect(testUser(roles: ['User']).isDonorCapable, isTrue);
    expect(testUser(roles: ['Admin']).isDonorCapable, isTrue);
    expect(testUser(roles: ['User', 'Admin']).isDonorCapable, isTrue);
    expect(testUser(roles: ['HospitalStaff']).isDonorCapable, isFalse);
    expect(testUser(roles: ['Doctor']).isDonorCapable, isFalse);
    expect(testUser(roles: ['Admin', 'Doctor']).isDonorCapable, isFalse);
  });

  group('AuthRepository', () {
    test('login returns the token and the user', () async {
      final api = MockedApi();
      api.adapter.onPost(
        '/Auth/login',
        (s) => s.reply(200, {
          'success': true,
          'message': 'Login successful',
          'data': {
            'accessToken': 'jwt-abc',
            'expiresAt': '2026-10-05T06:30:00Z',
            'user': userJson(
              roles: ['HospitalStaff'],
              hospitalApprovalStatus: 'Approved',
            ),
          },
        }),
        data: {'email': 'staff@example.test', 'password': 'Secret#123'},
      );

      final result = await AuthRepository(api.client)
          .login(' staff@example.test ', 'Secret#123');

      expect(result.token, 'jwt-abc');
      expect(result.user!.primaryRole, 'HospitalStaff');
      expect(result.user!.isUnapprovedHospitalStaff, isFalse);
      expect(result.user!.sessionIdleTimeoutMinutes, 10);
    });

    test('a failed login shows the API message', () async {
      final api = MockedApi();
      api.adapter.onPost(
        '/Auth/login',
        (s) => s.reply(401, {
          'success': false,
          'message': 'Invalid email or password.',
        }),
        data: Matchers.any,
      );

      expect(
        () => AuthRepository(api.client).login('x@example.test', 'bad'),
        throwsA(
          isA<ApiError>().having(
            (e) => e.message,
            'message',
            'Invalid email or password.',
          ),
        ),
      );
    });

    test('verify OTP returns the reset session token', () async {
      final api = MockedApi();
      api.adapter.onPost(
        '/Auth/verify-otp',
        (s) => s.reply(200, {
          'success': true,
          'data': {'resetSessionToken': 'reset-1'},
        }),
        data: Matchers.any,
      );

      expect(
        await AuthRepository(api.client).verifyOtp('a@example.test', '123456'),
        'reset-1',
      );
    });
  });

  group('AuthController', () {
    ProviderContainer containerFor(MockedApi api) {
      final c = ProviderContainer(
        overrides: api.overrides,
        retry: (_, _) => null,
      );
      addTearDown(c.dispose);
      return c;
    }

    test('no stored token: signed out', () async {
      final api = MockedApi();
      final c = containerFor(api);
      c.read(authControllerProvider);
      await Future<void>.delayed(Duration.zero);
      expect(c.read(authControllerProvider).status, AuthStatus.signedOut);
    });

    test('stored token: auto-login with /Auth/me', () async {
      final api = MockedApi(token: 'jwt-stored');
      api.adapter.onGet(
        '/Auth/me',
        (s) => s.reply(200, {'success': true, 'data': userJson()}),
      );
      final c = containerFor(api);

      await c.read(authControllerProvider.notifier).restore();

      expect(c.read(authControllerProvider).isSignedIn, isTrue);
    });

    test(
      'server unreachable at start keeps the token and offers a retry',
      () async {
        final api = MockedApi(token: 'jwt-stored');
        final c = containerFor(api);
        api.adapter.onGet('/Auth/me', (s) => s.reply(502, '<!DOCTYPE html>'));

        await c.read(authControllerProvider.notifier).restore();

        expect(c.read(authControllerProvider).status, AuthStatus.unreachable);
        expect(api.tokens.token, 'jwt-stored');
      },
    );

    test('login stores the token; logout clears everything', () async {
      final api = MockedApi();
      api.adapter
        ..onPost(
          '/Auth/login',
          (s) => s.reply(200, {
            'success': true,
            'data': {'accessToken': 'jwt-new', 'user': userJson()},
          }),
          data: Matchers.any,
        )
        ..onPost('/Auth/logout', (s) => s.reply(200, {'success': true}));
      final c = containerFor(api);

      await c
          .read(authControllerProvider.notifier)
          .login('donor@example.test', 'Secret#123');
      expect(api.tokens.token, 'jwt-new');
      expect(c.read(sessionActivityProvider).lastConfirmedMs, greaterThan(0));

      await c.read(authControllerProvider.notifier).logout();
      expect(api.tokens.token, isNull);
      expect(c.read(sessionActivityProvider).lastConfirmedMs, 0);
      expect(c.read(authControllerProvider).status, AuthStatus.signedOut);
    });

    test(
      'a 401 while signed in signs out with the reason and message',
      () async {
        final api = MockedApi();
        api.adapter
          ..onPost(
            '/Auth/login',
            (s) => s.reply(200, {
              'success': true,
              'data': {'accessToken': 'jwt-new', 'user': userJson()},
            }),
            data: Matchers.any,
          )
          ..onGet(
            '/Inventory',
            (s) => s.reply(
              401,
              {},
              headers: {
                'x-session-ended': ['idle'],
                'content-type': ['application/json'],
              },
            ),
          );
        final c = containerFor(api);
        await c
            .read(authControllerProvider.notifier)
            .login('donor@example.test', 'Secret#123');

        await expectLater(
          c.read(apiClientProvider).get('/Inventory'),
          throwsA(isA<ApiError>()),
        );
        await Future<void>.delayed(Duration.zero);

        final state = c.read(authControllerProvider);
        expect(state.status, AuthStatus.signedOut);
        expect(state.reason, SignOutReason.idle);
        expect(state.signOutMessage, contains('10 minutes of inactivity'));
        expect(api.tokens.token, isNull);
      },
    );
  });
}
