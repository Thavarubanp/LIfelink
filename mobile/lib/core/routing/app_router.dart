import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/change_password_screen.dart';
import '../../features/auth/forgot_password_screen.dart';
import '../../features/auth/login_screen.dart';
import '../../features/auth/register_screen.dart';
import '../../features/blood_requests/create_request_screen.dart';
import '../../features/blood_requests/donor_home_screen.dart';
import '../../features/blood_requests/public_requests_screen.dart';
import '../../features/blood_requests/request_detail_screen.dart';
import '../../features/complaints/complaints_screen.dart';
import '../../features/governance/governance_status_screen.dart';
import '../../features/governance/waiting_approval_screen.dart';
import '../../features/home/more_screen.dart';
import '../../features/home/role_shell.dart';
import '../../features/home/splash_screen.dart';
import '../../features/notifications/notifications_screen.dart';
import '../../features/profile/profile_screen.dart';
import '../../features/profile/public_profile_screens.dart';
import '../../features/profile/user_profile_screen.dart';
import '../../features/search/global_search_screen.dart';
import '../../features/screening/my_acceptances_screen.dart';
import '../../features/screening/screening_interview_screen.dart';
import '../../features/screening/suspended_withdrawals_screen.dart';
import '../auth/auth_controller.dart';
import '../../features/admin/directory/directory.dart' show myActivityScreen;
import 'admin_routes.dart';
import 'doctor_routes.dart';
import 'hospital_routes.dart';
import 'routes.dart';

final rootNavigatorKey = GlobalKey<NavigatorState>();

/// Re-runs the route guard whenever the sign-in state changes.
class _AuthRefresh extends ChangeNotifier {
  void notify() => notifyListeners();
}

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = _AuthRefresh();
  ref.listen(authControllerProvider, (_, _) => refresh.notify());
  ref.onDispose(refresh.dispose);

  final router = GoRouter(
    navigatorKey: rootNavigatorKey,
    initialLocation: AppRoutes.splash,
    refreshListenable: refresh,
    redirect: (context, state) =>
        resolveRedirect(ref.read(authControllerProvider), state.uri.path),
    routes: [
      GoRoute(path: AppRoutes.splash, builder: (_, _) => const SplashScreen()),
      GoRoute(path: AppRoutes.login, builder: (_, _) => const LoginScreen()),
      GoRoute(
        path: AppRoutes.register,
        builder: (_, _) => const RegisterScreen(),
      ),
      GoRoute(
        path: AppRoutes.forgotPassword,
        builder: (_, _) => const ForgotPasswordScreen(),
      ),
      GoRoute(
        path: AppRoutes.changePassword,
        builder: (_, _) => const ChangePasswordScreen(),
      ),
      GoRoute(
        path: AppRoutes.waitingApproval,
        builder: (_, _) => const WaitingApprovalScreen(),
      ),
      GoRoute(
        path: AppRoutes.governanceStatus,
        builder: (_, _) => const GovernanceStatusScreen(),
      ),
      GoRoute(
        path: AppRoutes.myAppeals,
        builder: (_, _) => const MyAppealsScreen(),
      ),
      GoRoute(
        path: AppRoutes.notifications,
        builder: (_, _) => const NotificationsScreen(),
      ),
      GoRoute(
        path: AppRoutes.profile,
        builder: (_, _) => const ProfileScreen(),
      ),
      GoRoute(
        path: AppRoutes.myActivity,
        builder: (_, _) => myActivityScreen(),
      ),
      GoRoute(
        path: AppRoutes.search,
        builder: (_, _) => const GlobalSearchScreen(),
      ),
      GoRoute(
        path: AppRoutes.suspendedWithdrawals,
        builder: (_, _) => const SuspendedWithdrawalsScreen(),
      ),
      GoRoute(
        path: '/profiles/user/:userId',
        builder: (_, state) =>
            UserProfileScreen(userId: state.pathParameters['userId']!),
      ),
      GoRoute(
        path: '/profiles/hospital/:hospitalId',
        builder: (_, state) => HospitalProfileScreen(
          hospitalId: state.pathParameters['hospitalId']!,
        ),
      ),
      GoRoute(
        path: '/profiles/doctor/:doctorId',
        builder: (_, state) => PublicDoctorProfileScreen(
          doctorId: state.pathParameters['doctorId']!,
        ),
      ),

      // Hospital staff (Step 1)
      ...hospitalRoutes(),

      // Donor / patient (Step 2: Vidya)
      _shell(
        tabs: const [
          ShellTab('Home', Icons.home_outlined, Icons.home),
          ShellTab('Requests', Icons.bloodtype_outlined, Icons.bloodtype),
          ShellTab(
            'Donations',
            Icons.volunteer_activism_outlined,
            Icons.volunteer_activism,
          ),
          ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
        ],
        branches: [
          (AppRoutes.donorHome, const DonorHomeScreen()),
          (AppRoutes.donorRequests, const PublicRequestsScreen()),
          (AppRoutes.donorAcceptances, const MyAcceptancesScreen()),
          (
            AppRoutes.donorMore,
            const MoreScreen(
              entries: [
                MoreEntry(
                  'Complaints',
                  Icons.report_outlined,
                  AppRoutes.donorComplaints,
                ),
                MoreEntry(
                  'Appeals',
                  Icons.gavel_outlined,
                  AppRoutes.donorAppeals,
                ),
              ],
            ),
          ),
        ],
      ),
      GoRoute(
        path: AppRoutes.createRequest,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => CreateRequestScreen(
          initialPriority: state.uri.queryParameters['priority'],
          initialBloodGroup: state.uri.queryParameters['bloodGroup'],
        ),
      ),
      GoRoute(
        path: '${AppRoutes.donorRequests}/:requestId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) =>
            RequestDetailScreen(requestId: state.pathParameters['requestId']!),
      ),
      GoRoute(
        path: '${AppRoutes.donorAcceptances}/:acceptanceId/screening',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => ScreeningInterviewScreen(
          acceptanceId: state.pathParameters['acceptanceId']!,
        ),
      ),
      GoRoute(
        path: AppRoutes.donorComplaints,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const ComplaintsScreen(),
      ),
      GoRoute(
        path: AppRoutes.donorAppeals,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const MyAppealsScreen(),
      ),

      // Doctor (Step 3: Ahamed)
      ...doctorRoutes(),

      // Admin (Step 4: Mayureshan)
      ...adminRoutes(),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});

/// A role's bottom-navigation shell: one branch (with its own navigation stack) per tab.
/// [builders] replaces a branch's fixed screen when it needs the route state (e.g. query parameters).
StatefulShellRoute _shell({
  required List<ShellTab> tabs,
  required List<(String, Widget)> branches,
  Map<String, Widget Function(GoRouterState state)> builders = const {},
}) => StatefulShellRoute.indexedStack(
  builder: (context, state, shell) => RoleShell(shell: shell, tabs: tabs),
  branches: [
    for (final (path, screen) in branches)
      StatefulShellBranch(
        routes: [
          GoRoute(
            path: path,
            builder: (_, state) => builders[path]?.call(state) ?? screen,
          ),
        ],
      ),
  ],
);

/// Shared by hospital_routes.dart.
StatefulShellRoute roleShell({
  required List<ShellTab> tabs,
  required List<(String, Widget)> branches,
  Map<String, Widget Function(GoRouterState state)> builders = const {},
}) => _shell(tabs: tabs, branches: branches, builders: builders);
