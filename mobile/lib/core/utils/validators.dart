/// Form validators with the same rules (and messages) as the API DTOs.
class Validators {
  const Validators._();

  static final _email = RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$');
  static final _strongPassword = RegExp(r'^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^\da-zA-Z]).{8,}$');
  static final _phone = RegExp(r'^\d{10}$');
  static final _otp = RegExp(r'^\d{6}$');

  static String? required(String? value, String label) =>
      (value == null || value.trim().isEmpty) ? '$label is required.' : null;

  static String? name(String? value, String label) {
    final r = required(value, label);
    if (r != null) return r;
    return value!.trim().length > 100 ? '$label cannot exceed 100 characters.' : null;
  }

  static String? email(String? value) {
    final r = required(value, 'Email');
    if (r != null) return r;
    final v = value!.trim();
    if (v.length > 256) return 'Email cannot exceed 256 characters.';
    return _email.hasMatch(v) ? null : 'Invalid email address format.';
  }

  /// Same rule as RegisterRequestDto / ResetPasswordRequestDto / ChangePasswordRequestDto.
  static String? strongPassword(String? value) {
    if (value == null || value.isEmpty) return 'Password is required.';
    if (value.length < 8) return 'Password must be at least 8 characters long.';
    return _strongPassword.hasMatch(value)
        ? null
        : 'Use upper and lower case letters, a number and a symbol.';
  }

  static String? phone(String? value) {
    final r = required(value, 'Phone number');
    if (r != null) return r;
    return _phone.hasMatch(value!.trim()) ? null : 'Enter a 10-digit phone number.';
  }

  static String? otp(String? value) => _otp.hasMatch(value?.trim() ?? '') ? null : 'Enter the 6-digit code.';

  static String? maxLength(String? value, int max, String label) =>
      (value ?? '').length > max ? '$label cannot exceed $max characters.' : null;

  /// Whole number in [min, max] (max optional).
  static String? intRange(String? value, String label, {required int min, int? max}) {
    final n = int.tryParse(value?.trim() ?? '');
    if (n == null) return '$label must be a whole number.';
    if (n < min) return '$label must be at least $min.';
    if (max != null && n > max) return '$label must be at most $max.';
    return null;
  }
}
