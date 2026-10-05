import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/api_client.dart';
import '../api/api_error.dart';
import '../providers.dart';
import 'current_user.dart';

enum AuthStatus { unknown, unreachable, signedOut, signedIn }

class AuthState {
  const AuthState._(this.status, {this.user, this.reason, this.idleMinutes, this.message});

  const AuthState.unknown() : this._(AuthStatus.unknown);
  const AuthState.signedIn(CurrentUser user) : this._(AuthStatus.signedIn, user: user);
  const AuthState.signedOut({SignOutReason? reason, double? idleMinutes})
      : this._(AuthStatus.signedOut, reason: reason, idleMinutes: idleMinutes);
  const AuthState.unreachable(String message) : this._(AuthStatus.unreachable, message: message);

  final AuthStatus status;
  final CurrentUser? user;

  /// Why the last session ended (shown once on the sign-in screen).
  final SignOutReason? reason;
  final double? idleMinutes;
  final String? message;

  bool get isSignedIn => status == AuthStatus.signedIn && user != null;

  /// The sign-in screen message for [reason], like the web app's login page.
  String? get signOutMessage => switch (reason) {
        SignOutReason.idle =>
          'You were signed out after ${_minutes(idleMinutes)} of inactivity. Please sign in again.',
        SignOutReason.expired => 'Your session has expired. Please sign in again.',
        _ => null,
      };

  static String _minutes(double? m) {
    if (m == null) return 'a period';
    final n = m == m.roundToDouble() ? m.toInt().toString() : m.toString();
    return '$n minute${m == 1 ? '' : 's'}';
  }
}

final authControllerProvider = NotifierProvider<AuthController, AuthState>(AuthController.new);

/// Sign-in state for the whole app: restores the session on start, signs in and out, and reacts to 401s.
class AuthController extends Notifier<AuthState> {
  @override
  AuthState build() {
    ref.read(apiClientProvider).onUnauthorized = _onUnauthorized;
    Future.microtask(restore);
    return const AuthState.unknown();
  }

  /// Auto-login: a stored token is checked with /Auth/me.
  Future<void> restore() async {
    state = const AuthState.unknown();
    final tokens = ref.read(tokenStorageProvider);
    final token = await tokens.load();
    if (!ref.mounted) return;
    if (token == null || token.isEmpty) {
      state = const AuthState.signedOut();
      return;
    }
    try {
      final user = await ref.read(authRepositoryProvider).me();
      if (ref.mounted) state = AuthState.signedIn(user);
    } on ApiError catch (e) {
      if (!ref.mounted) return;
      if (e.isNetwork) {
        // Keep the token: the account may still be signed in once the server is reachable
        state = AuthState.unreachable(e.message);
      } else if (state.status != AuthStatus.signedOut) {
        await _clear(null);
      }
    }
  }

  Future<void> login(String email, String password) async {
    final activity = ref.read(sessionActivityProvider);
    // The API starts the session's idle clock while handling this request
    final sentAt = activity.nowMs;
    final result = await ref.read(authRepositoryProvider).login(email, password);
    if (!ref.mounted) return;
    await ref.read(tokenStorageProvider).save(result.token);
    activity.markConfirmed(sentAt);
    try {
      final user = result.user ?? await ref.read(authRepositoryProvider).me();
      if (ref.mounted) state = AuthState.signedIn(user);
    } catch (e) {
      if (ref.mounted) await _clear(null);
      rethrow;
    }
  }

  /// Signs out on the server (best effort) and clears everything stored on the phone.
  Future<void> logout({SignOutReason reason = SignOutReason.signedOut}) async {
    if (ref.read(tokenStorageProvider).token != null) {
      try {
        await ref.read(authRepositoryProvider).logout(reason: reason == SignOutReason.idle ? 'idle' : null);
      } catch (_) {
        // The API ends idle or expired sessions on its own
      }
    }
    if (ref.mounted) await _clear(reason);
  }

  /// Reloads the signed-in account (e.g. after a password change or an approval).
  Future<CurrentUser?> refreshUser() async {
    final user = await ref.read(authRepositoryProvider).me();
    if (ref.mounted) state = AuthState.signedIn(user);
    return user;
  }

  void _onUnauthorized(SignOutReason reason) {
    if (state.status == AuthStatus.signedIn) _clear(reason);
  }

  Future<void> _clear(SignOutReason? reason) async {
    final idle = state.user?.sessionIdleTimeoutMinutes;
    final tokens = ref.read(tokenStorageProvider);
    final activity = ref.read(sessionActivityProvider);
    await tokens.clear();
    activity.reset();
    if (ref.mounted) state = AuthState.signedOut(reason: reason, idleMinutes: idle);
  }
}
