/// Values shared with the web app and the API.
class AppConstants {
  const AppConstants._();

  static const bloodGroups = ['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'];

  /// Sri Lanka time is UTC+5:30 all year (no daylight saving).
  static const sriLankaOffset = Duration(hours: 5, minutes: 30);

  /// Idle-timeout defaults used until /Auth/me reports the server settings.
  static const defaultIdleMinutes = 10.0;
  static const defaultWarningMinutes = 1.0;

  /// A user-driven request reports activity at most this often (same as the web app).
  static const activityReportInterval = Duration(seconds: 15);

  /// Touch input sends a heartbeat at most this often (same as the web app).
  static const heartbeatInterval = Duration(seconds: 30);

  /// Background refresh of the unread notification count.
  static const unreadPollInterval = Duration(seconds: 30);
}

/// Role names exactly as the API returns them.
class Roles {
  const Roles._();

  static const user = 'User';
  static const hospitalStaff = 'HospitalStaff';
  static const doctor = 'Doctor';
  static const admin = 'Admin';
}
