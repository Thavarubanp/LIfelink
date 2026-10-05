import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/auth/change_password_screen.dart';
import '../../features/auth/forgot_password_screen.dart';
import '../../features/auth/login_screen.dart';
import '../../features/auth/register_screen.dart';
import '../../features/governance/governance_status_screen.dart';
import '../../features/governance/waiting_approval_screen.dart';
import '../../features/home/more_screen.dart';
import '../../features/home/role_shell.dart';
import '../../features/home/splash_screen.dart';
import '../../features/home/stub_screen.dart';
import '../../features/notifications/notifications_screen.dart';
import '../../features/profile/profile_screen.dart';
import '../auth/auth_controller.dart';
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
    redirect: (context, state) => resolveRedirect(ref.read(authControllerProvider), state.uri.path),
    routes: [
      GoRoute(path: AppRoutes.splash, builder: (_, _) => const SplashScreen()),
      GoRoute(path: AppRoutes.login, builder: (_, _) => const LoginScreen()),
      GoRoute(path: AppRoutes.register, builder: (_, _) => const RegisterScreen()),
      GoRoute(path: AppRoutes.forgotPassword, builder: (_, _) => const ForgotPasswordScreen()),
      GoRoute(path: AppRoutes.changePassword, builder: (_, _) => const ChangePasswordScreen()),
      GoRoute(path: AppRoutes.waitingApproval, builder: (_, _) => const WaitingApprovalScreen()),
      GoRoute(path: AppRoutes.governanceStatus, builder: (_, _) => const GovernanceStatusScreen()),
      GoRoute(path: AppRoutes.notifications, builder: (_, _) => const NotificationsScreen()),
      GoRoute(path: AppRoutes.profile, builder: (_, _) => const ProfileScreen()),

      // Hospital staff (Step 1)
      ...hospitalRoutes(),

      // Donor / patient (Step 2: Vidya)
      _shell(
        tabs: const [
          ShellTab('Home', Icons.home_outlined, Icons.home),
          ShellTab('Requests', Icons.bloodtype_outlined, Icons.bloodtype),
          ShellTab('Donations', Icons.volunteer_activism_outlined, Icons.volunteer_activism),
          ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
        ],
        branches: [
          (AppRoutes.donorHome, _step2('Donor home', 'Your requests, donations and eligibility at a glance.')),
          (AppRoutes.donorRequests, _step2('Blood requests', 'Available requests, accepting and your own requests.')),
          (AppRoutes.donorAcceptances, _step2('My donations', 'Screening interview, answers and doctor decisions.')),
          (
            AppRoutes.donorMore,
            const MoreScreen(entries: [
              MoreEntry('Complaints', Icons.report_outlined, AppRoutes.donorComplaints),
              MoreEntry('Appeals', Icons.gavel_outlined, AppRoutes.donorAppeals),
            ]),
          ),
        ],
      ),
      _stubRoute(AppRoutes.createRequest, _step2('Create blood request', 'Create a blood request (including Critical).')),
      _stubRoute(AppRoutes.donorComplaints, _step2('Complaints', 'File and follow your complaints.')),
      _stubRoute(AppRoutes.donorAppeals, _step2('Appeals', 'Your appeal threads.')),

      // Doctor (Step 3: Ahamed)
      _shell(
        tabs: const [
          ShellTab('Requests', Icons.assignment_outlined, Icons.assignment),
          ShellTab('Reports', Icons.fact_check_outlined, Icons.fact_check),
          ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
        ],
        branches: [
          (AppRoutes.doctorHome, _step3('Assigned requests', 'Approve or reject the blood requests assigned to you.')),
          (AppRoutes.doctorReports, _step3('Screening reports', 'Review donor screening report versions.')),
          (
            AppRoutes.doctorMore,
            const MoreScreen(entries: [
              MoreEntry('Hospital donations', Icons.local_hospital_outlined, AppRoutes.doctorHospitalDonations),
            ]),
          ),
        ],
      ),
      _stubRoute(AppRoutes.doctorHospitalDonations, _step3('Hospital donations', 'Approve or reject hospital donations.')),

      // Admin (Step 4: Mayureshan)
      _shell(
        tabs: const [
          ShellTab('Home', Icons.dashboard_outlined, Icons.dashboard),
          ShellTab('Attention', Icons.flag_outlined, Icons.flag),
          ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
        ],
        branches: [
          (AppRoutes.adminHome, _step4('Admin dashboard', 'Statistics and items that need attention.')),
          (AppRoutes.adminAttention, _step4('Needs attention', 'Pending registrations, appeals and complaints.')),
          (
            AppRoutes.adminMore,
            const MoreScreen(entries: [
              MoreEntry('Hospital registrations', Icons.domain_add_outlined, AppRoutes.adminRegistrations),
              MoreEntry('Appeals', Icons.gavel_outlined, AppRoutes.adminAppeals),
              MoreEntry('Complaints', Icons.report_outlined, AppRoutes.adminComplaints),
              MoreEntry('Activity log', Icons.history, AppRoutes.adminActivity),
              MoreEntry('Donor features', Icons.bloodtype_outlined, AppRoutes.donorRequests),
            ]),
          ),
        ],
      ),
      _stubRoute(AppRoutes.adminRegistrations, _step4('Hospital registrations', 'Approve or return pending hospitals.')),
      _stubRoute(AppRoutes.adminAppeals, _step4('Appeals', 'Reply to and decide appeals.')),
      _stubRoute(AppRoutes.adminComplaints, _step4('Complaints', 'Reply to complaints.')),
      _stubRoute(AppRoutes.adminActivity, _step4('Activity log', 'Everyone\'s activity, with filters.')),
    ],
  );
  ref.onDispose(router.dispose);
  return router;
});

Widget _step2(String title, String description) => StubScreen(title: title, step: 2, owner: 'Vidya R', description: description);
Widget _step3(String title, String description) => StubScreen(title: title, step: 3, owner: 'Ahamed MSA', description: description);
Widget _step4(String title, String description) => StubScreen(title: title, step: 4, owner: 'Mayureshan P', description: description);

GoRoute _stubRoute(String path, Widget screen) => GoRoute(path: path, builder: (_, _) => screen);

/// A role's bottom-navigation shell: one branch (with its own navigation stack) per tab.
/// [builders] replaces a branch's fixed screen when it needs the route state (e.g. query parameters).
StatefulShellRoute _shell({
  required List<ShellTab> tabs,
  required List<(String, Widget)> branches,
  Map<String, Widget Function(GoRouterState state)> builders = const {},
}) =>
    StatefulShellRoute.indexedStack(
      builder: (context, state, shell) => RoleShell(shell: shell, tabs: tabs),
      branches: [
        for (final (path, screen) in branches)
          StatefulShellBranch(
            routes: [GoRoute(path: path, builder: (_, state) => builders[path]?.call(state) ?? screen)],
          ),
      ],
    );

/// Shared by hospital_routes.dart.
StatefulShellRoute roleShell({
  required List<ShellTab> tabs,
  required List<(String, Widget)> branches,
  Map<String, Widget Function(GoRouterState state)> builders = const {},
}) =>
    _shell(tabs: tabs, branches: branches, builders: builders);
