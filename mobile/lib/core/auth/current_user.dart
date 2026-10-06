import '../api/json.dart';
import '../config/constants.dart';

/// The signed-in account as returned by /Auth/login and /Auth/me (CurrentUserDto).
class CurrentUser {
  const CurrentUser({
    required this.userId,
    required this.firstName,
    required this.lastName,
    required this.email,
    required this.roles,
    this.accountStatus = '',
    this.isSuspended = false,
    this.mustChangePassword = false,
    this.hospitalApprovalStatus,
    this.sessionIdleTimeoutMinutes = AppConstants.defaultIdleMinutes,
    this.sessionWarningMinutes = AppConstants.defaultWarningMinutes,
  });

  factory CurrentUser.fromJson(Map<String, dynamic> json) {
    final roles = json['roles'];
    final idle = doubleOf(json['sessionIdleTimeoutMinutes']);
    final warning = json['sessionWarningMinutes'];
    return CurrentUser(
      userId: str(json['userId']),
      firstName: str(json['firstName']),
      lastName: str(json['lastName']),
      email: str(json['email']),
      roles: roles is List
          ? roles.map((r) => r.toString()).toList()
          : roles is String
          ? [roles]
          : const [],
      accountStatus: str(json['accountStatus']),
      isSuspended: boolOf(json['isSuspended']),
      mustChangePassword: boolOf(json['mustChangePassword']),
      hospitalApprovalStatus: json['hospitalApprovalStatus']?.toString(),
      sessionIdleTimeoutMinutes: idle > 0
          ? idle
          : AppConstants.defaultIdleMinutes,
      sessionWarningMinutes: warning is num && warning >= 0
          ? warning.toDouble()
          : AppConstants.defaultWarningMinutes,
    );
  }

  final String userId;
  final String firstName;
  final String lastName;
  final String email;
  final List<String> roles;
  final String accountStatus;
  final bool isSuspended;
  final bool mustChangePassword;
  final String? hospitalApprovalStatus;
  final double sessionIdleTimeoutMinutes;
  final double sessionWarningMinutes;

  String get fullName => '$firstName $lastName'.trim();

  /// A user with no role rows is a plain User (same as the API's login fallback).
  List<String> get effectiveRoles => roles.isEmpty ? const [Roles.user] : roles;

  bool hasRole(String role) => effectiveRoles.contains(role);

  /// Accounts that may participate as donors. Administrative authority does
  /// not grant medical authority; the API still enforces every donor rule.
  bool get isDonorCapable =>
      (hasRole(Roles.user) || hasRole(Roles.admin)) &&
      !hasRole(Roles.hospitalStaff) &&
      !hasRole(Roles.doctor);

  /// Hospital staff whose hospital registration is not Approved may only see the waiting screen.
  bool get isUnapprovedHospitalStaff =>
      hasRole(Roles.hospitalStaff) &&
      !hasRole(Roles.admin) &&
      hospitalApprovalStatus != 'Approved';

  /// Primary role for the home screen: Admin > HospitalStaff > Doctor > User (same order as the web app).
  String get primaryRole {
    if (hasRole(Roles.admin)) return Roles.admin;
    if (hasRole(Roles.hospitalStaff)) return Roles.hospitalStaff;
    if (hasRole(Roles.doctor)) return Roles.doctor;
    return Roles.user;
  }

  String get roleLabel => switch (primaryRole) {
    Roles.admin => 'Administrator',
    Roles.hospitalStaff => 'Hospital staff',
    Roles.doctor => 'Doctor',
    _ => 'Donor / patient',
  };
}
