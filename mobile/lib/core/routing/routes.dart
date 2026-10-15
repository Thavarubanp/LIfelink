import '../auth/auth_controller.dart';
import '../auth/current_user.dart';
import '../config/constants.dart';

/// Every path in the app. Steps 2–4 replace the stub screens behind their paths; the paths stay the same.
class AppRoutes {
  const AppRoutes._();

  static const splash = '/';
  static const login = '/login';
  static const register = '/register';
  static const registerHospital = '/register-hospital';
  static const forgotPassword = '/forgot-password';
  static const changePassword = '/change-password';
  static const waitingApproval = '/waiting-approval';
  static const governanceStatus = '/governance/status';
  static const myAppeals = '/governance/appeals';
  static const notifications = '/notifications';
  static const profile = '/profile';
  static const settings = '/settings';
  static const myActivity = '/activity';
  static const search = '/search';
  static const suspendedWithdrawals = '/governance/withdrawals';
  static String userProfile(String userId) => '/profiles/user/$userId';
  static String hospitalProfile(String hospitalId) =>
      '/profiles/hospital/$hospitalId';
  static String doctorProfile(String doctorId) => '/profiles/doctor/$doctorId';

  // Hospital staff (Step 1: Thavaruban; verify/doctors: Step 3)
  static const hospitalHome = '/hospital';
  static const hospitalInventory = '/hospital/inventory';
  static const hospitalTransfers = '/hospital/transfers';
  static const hospitalMore = '/hospital/more';
  static const hospitalEmergencies = '/hospital/emergencies';
  static const hospitalDonate = '/hospital/donate';
  static const hospitalScan = '/hospital/scan';
  static const hospitalRecommendations = '/hospital/recommendations';
  static const hospitalVerifyRequests = '/hospital/verify-requests';
  static const hospitalDoctors = '/hospital/doctors';
  static String hospitalPacket(String packetId) =>
      '/hospital/packets/$packetId';

  // Donor / patient (Step 2: Vidya)
  static const donorHome = '/donor';
  static const donorRequests = '/donor/requests';
  static const donorCreate = '/donor/create';
  static const donorAcceptances = '/donor/acceptances';
  static const donorMore = '/donor/more';
  static const createRequest = '/donor/requests/create';
  static const donorComplaints = '/donor/complaints';
  static const donorAppeals = '/donor/appeals';
  static String requestDetail(String id) => '/donor/requests/$id';
  static String screening(String acceptanceId) =>
      '/donor/acceptances/$acceptanceId/screening';

  // Doctor (Step 3: Ahamed)
  static const doctorHome = '/doctor';
  static const doctorRequests = '/doctor/requests';
  static const doctorReports = '/doctor/reports';
  static const doctorMore = '/doctor/more';
  static const doctorHospitalDonations = '/doctor/hospital-donations';
  static String doctorReport(String acceptanceId) =>
      '/doctor/reports/$acceptanceId';
  static String doctorRequestDonors(String requestId) =>
      '/doctor/requests/$requestId/donors';
  static String hospitalRequestDonors(String requestId) =>
      '/hospital/requests/$requestId/donors';

  // Admin (Step 4: Mayureshan)
  static const adminHome = '/admin';
  static const adminAttention = '/admin/attention';
  static const adminMore = '/admin/more';
  static const adminRegistrations = '/admin/registrations';
  static const adminAppeals = '/admin/appeals';
  static const adminComplaints = '/admin/complaints';
  static const adminActivity = '/admin/activity';
  static const adminCreate = '/admin/create';
  static const adminAcceptances = '/admin/create/acceptances';
  static const adminUsers = '/admin/users';
  static const adminHospitals = '/admin/hospitals';
  static const adminDoctors = '/admin/doctors';
  static String adminScreening(String acceptanceId) =>
      '$adminAcceptances/$acceptanceId/screening';

  static const publicPaths = {
    login,
    register,
    registerHospital,
    forgotPassword,
  };

  /// The waiting page also accepts the just-created anonymous registration as
  /// route state. Signed-in routing for that page remains governed below.
  static const signedOutPaths = {...publicPaths, waitingApproval};

  static const _bottomNavigationPaths = {
    hospitalHome,
    hospitalInventory,
    hospitalTransfers,
    hospitalDoctors,
    hospitalMore,
    donorHome,
    donorRequests,
    donorCreate,
    donorAcceptances,
    donorMore,
    doctorHome,
    doctorRequests,
    doctorReports,
    doctorMore,
    adminHome,
    adminAttention,
    adminCreate,
    adminActivity,
    adminMore,
  };

  static bool hasBottomNavigation(String path) =>
      _bottomNavigationPaths.contains(path);

  /// Pages whose own primary action already occupies the bottom-right corner.
  /// The assistant mini-FAB stacks above it without moving the primary action.
  static bool hasPageFloatingAction(String path) =>
      path == hospitalInventory ||
      path == hospitalTransfers ||
      path == hospitalDoctors ||
      path == hospitalEmergencies ||
      path == donorComplaints ||
      path.startsWith('$adminComplaints/') ||
      RegExp(r'^/donor/acceptances/[^/]+/screening$').hasMatch(path);

  /// Home screen for the account's primary role.
  static String homeFor(CurrentUser user) => switch (user.primaryRole) {
    Roles.admin => adminHome,
    Roles.hospitalStaff => hospitalHome,
    Roles.doctor => doctorHome,
    _ => donorHome,
  };

  /// Roles allowed on a path (longest prefix wins); null = any signed-in account.
  static List<String>? rolesFor(String path) {
    const rules = <(String, List<String>)>[
      (createRequest, [Roles.user, Roles.hospitalStaff, Roles.admin]),
      (donorComplaints, [Roles.user, Roles.hospitalStaff]),
      (donorAcceptances, [Roles.user, Roles.admin]),
      (donorHome, [Roles.user, Roles.admin]),
      (hospitalHome, [Roles.hospitalStaff]),
      (doctorHome, [Roles.doctor]),
      (adminHome, [Roles.admin]),
    ];
    for (final (prefix, roles) in rules) {
      if (path == prefix || path.startsWith('$prefix/')) return roles;
    }
    return null;
  }

  /// Pages where a signed-in account only waits: no idle sign-out there, the session is kept alive quietly.
  static bool isWaitingPath(String path) =>
      path == waitingApproval || path.startsWith('/governance');
}

/// The route guard, in the same order as the web app's ProtectedRoute:
/// signed out → sign in; suspended → governance status; unapproved hospital → waiting screen;
/// doctor who must change the password → change password; wrong role → own home.
String? resolveRedirect(AuthState auth, String path) {
  if (auth.status == AuthStatus.unknown ||
      auth.status == AuthStatus.unreachable) {
    return path == AppRoutes.splash ? null : AppRoutes.splash;
  }

  final user = auth.user;
  if (!auth.isSignedIn || user == null) {
    return AppRoutes.signedOutPaths.contains(path) ? null : AppRoutes.login;
  }

  if (path == AppRoutes.splash || AppRoutes.publicPaths.contains(path)) {
    return AppRoutes.homeFor(user);
  }

  if (path == AppRoutes.suspendedWithdrawals && !user.isDonorCapable) {
    return user.isSuspended
        ? AppRoutes.governanceStatus
        : AppRoutes.homeFor(user);
  }

  if (user.isSuspended) {
    return path == AppRoutes.governanceStatus ||
            path == AppRoutes.myAppeals ||
            path == AppRoutes.suspendedWithdrawals
        ? null
        : AppRoutes.governanceStatus;
  }

  if (user.isUnapprovedHospitalStaff) {
    return path == AppRoutes.waitingApproval ? null : AppRoutes.waitingApproval;
  }
  if (path == AppRoutes.waitingApproval) return AppRoutes.homeFor(user);

  if (user.hasRole(Roles.doctor) && user.mustChangePassword) {
    return path == AppRoutes.changePassword ? null : AppRoutes.changePassword;
  }

  final allowed = AppRoutes.rolesFor(path);
  if (allowed != null && !allowed.any(user.hasRole)) {
    return AppRoutes.homeFor(user);
  }

  return null;
}
