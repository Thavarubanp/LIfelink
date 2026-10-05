import '../api/api_client.dart';
import '../api/api_error.dart';
import '../api/json.dart';
import 'current_user.dart';

/// Result of a successful sign-in.
class LoginResult {
  const LoginResult(this.token, this.user);
  final String token;
  final CurrentUser? user;
}

/// The same /Auth endpoints the web app uses.
class AuthRepository {
  AuthRepository(this._api);

  final ApiClient _api;

  Future<LoginResult> login(String email, String password) async {
    final body = await _api.post('/Auth/login', body: {'email': email.trim(), 'password': password});
    final map = body is Map ? Map<String, dynamic>.from(body) : <String, dynamic>{};
    final data = unwrapMap(body);
    final token = str(data['accessToken'] ?? data['token']);
    if (map['success'] != true || token.isEmpty) {
      throw ApiError(str(map['message'], 'Invalid email or password.'));
    }
    final user = data['user'];
    return LoginResult(token, user is Map ? CurrentUser.fromJson(Map<String, dynamic>.from(user)) : null);
  }

  Future<CurrentUser> me() async {
    final body = await _api.get('/Auth/me');
    final data = unwrapMap(body);
    if (body is Map && body['success'] == false || data.isEmpty) {
      throw const ApiError('Could not load your account.');
    }
    return CurrentUser.fromJson(data);
  }

  /// Ends the server-side session; reason 'idle' when signed out for inactivity.
  Future<void> logout({String? reason}) async {
    await _api.post('/Auth/logout', query: {'reason': reason}, options: apiOptions(background: true));
  }

  /// Heartbeat: the user is active (touch or "Stay signed in"); keeps the session alive.
  Future<void> recordActivity() async {
    await _api.post('/Auth/activity', options: apiOptions(reportActivity: true));
  }

  /// Donor/patient registration (hospital registration stays on the web app).
  Future<String> register(Map<String, dynamic> dto) async {
    final body = await _api.post('/Auth/register', body: dto);
    return str(body is Map ? body['message'] : null, 'Registration successful. You can now sign in.');
  }

  Future<String> forgotPassword(String email) async {
    final body = await _api.post('/Auth/forgot-password', body: {'email': email.trim()});
    return str(body is Map ? body['message'] : null, 'If the email is registered, a 6-digit code has been sent.');
  }

  Future<String> resendOtp(String email) async {
    final body = await _api.post('/Auth/resend-otp', body: {'email': email.trim()});
    return str(body is Map ? body['message'] : null, 'A new code has been sent.');
  }

  /// Returns the reset session token.
  Future<String> verifyOtp(String email, String otp) async {
    final body = await _api.post('/Auth/verify-otp', body: {'email': email.trim(), 'otp': otp});
    final data = unwrapMap(body);
    final token = str(data['resetSessionToken'] ?? (body is Map ? body['resetSessionToken'] : null));
    if (token.isEmpty) throw const ApiError('Verification failed. No reset token issued.');
    return token;
  }

  Future<String> resetPassword(String email, String token, String newPassword) async {
    final body = await _api.post('/Auth/reset-password',
        body: {'email': email.trim(), 'token': token, 'newPassword': newPassword});
    return str(body is Map ? body['message'] : null, 'Password reset. You can now sign in.');
  }

  Future<String> changePassword(String currentPassword, String newPassword) async {
    final body = await _api.post('/Auth/change-password',
        body: {'currentPassword': currentPassword, 'newPassword': newPassword});
    return str(body is Map ? body['message'] : null, 'Password changed.');
  }
}
