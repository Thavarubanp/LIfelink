import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/admin/admin_home_screen.dart';
import '../../features/admin/admin_create_screen.dart';
import '../../features/admin/appeals/admin_appeals.dart';
import '../../features/admin/attention_controller.dart';
import '../../features/admin/complaints/admin_complaints.dart';
import '../../features/admin/directory/admin_actions.dart';
import '../../features/admin/directory/directory.dart';
import '../../features/admin/oversight/admin_activity_screen.dart';
import '../../features/admin/registrations/registration_detail_screen.dart';
import '../../features/admin/registrations/registrations_screen.dart';
import '../../features/home/more_screen.dart';
import '../../features/home/role_shell.dart';
import '../../features/screening/my_acceptances_screen.dart';
import '../../features/screening/screening_interview_screen.dart';
import 'app_router.dart';
import 'routes.dart';

/// Admin routes (Step 4: Mayureshan). Bottom navigation: Home · Attention · Activity · More.
List<RouteBase> adminRoutes() => [
  roleShell(
    tabs: [
      const ShellTab('Home', Icons.dashboard_outlined, Icons.dashboard),
      ShellTab(
        'Attention',
        Icons.flag_outlined,
        Icons.flag,
        badge: attentionCountProvider('total'),
      ),
      const ShellTab('Create', Icons.add, Icons.add),
      ShellTab(
        'Activity',
        Icons.history,
        Icons.history,
        badge: attentionCountProvider('activity'),
      ),
      const ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
    ],
    branches: [
      (AppRoutes.adminHome, const AdminHomeScreen()),
      (AppRoutes.adminAttention, const AttentionScreen()),
      (AppRoutes.adminCreate, const AdminCreateScreen()),
      (AppRoutes.adminActivity, const AdminActivityScreen()),
      (
        AppRoutes.adminMore,
        const MoreScreen(
          entries: [
            MoreEntry(
              'Hospital registrations',
              Icons.domain_add_outlined,
              AppRoutes.adminRegistrations,
            ),
            MoreEntry('Appeals', Icons.gavel_outlined, AppRoutes.adminAppeals),
            MoreEntry(
              'Complaints',
              Icons.report_outlined,
              AppRoutes.adminComplaints,
            ),
            MoreEntry('Users', Icons.people_outline, AppRoutes.adminUsers),
            MoreEntry(
              'Hospitals',
              Icons.local_hospital_outlined,
              AppRoutes.adminHospitals,
            ),
          ],
        ),
      ),
    ],
    builders: {
      AppRoutes.adminActivity: (state) =>
          AdminActivityScreen(initialTab: state.uri.queryParameters['tab']),
    },
  ),
  GoRoute(
    path: AppRoutes.adminUsers,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, state) => AdminUsersScreen(
      initialFilter: state.uri.queryParameters['filter'] ?? 'all',
    ),
    routes: [
      GoRoute(
        path: ':userId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => AdminUserProfileScreen(
          userId: state.pathParameters['userId']!,
          actions: userAdminActions,
        ),
        routes: [
          GoRoute(
            path: 'activity',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) =>
                userActivityScreen(state.pathParameters['userId']!),
          ),
        ],
      ),
    ],
  ),
  GoRoute(
    path: AppRoutes.adminHospitals,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, state) => AdminHospitalsScreen(
      initialFilter: state.uri.queryParameters['filter'] ?? 'all',
    ),
    routes: [
      GoRoute(
        path: ':hospitalId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => AdminHospitalProfileScreen(
          hospitalId: state.pathParameters['hospitalId']!,
          actions: hospitalAdminActions,
        ),
        routes: [
          GoRoute(
            path: 'activity',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) =>
                hospitalActivityScreen(state.pathParameters['hospitalId']!),
          ),
        ],
      ),
    ],
  ),
  GoRoute(
    path: AppRoutes.adminDoctors,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, _) => const AdminDoctorsScreen(),
  ),
  GoRoute(
    path: AppRoutes.adminAcceptances,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, _) => const MyAcceptancesScreen(
      browsePath: AppRoutes.adminCreate,
      screeningPathPrefix: AppRoutes.adminAcceptances,
    ),
    routes: [
      GoRoute(
        path: ':acceptanceId/screening',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => ScreeningInterviewScreen(
          acceptanceId: state.pathParameters['acceptanceId']!,
          returnPath: AppRoutes.adminAcceptances,
        ),
      ),
    ],
  ),
  GoRoute(
    path: AppRoutes.adminRegistrations,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, _) => const RegistrationsScreen(),
    routes: [
      GoRoute(
        path: ':hospitalId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => RegistrationDetailScreen(
          hospitalId: state.pathParameters['hospitalId']!,
        ),
      ),
    ],
  ),
  GoRoute(
    path: AppRoutes.adminAppeals,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, _) => const AdminAppealsScreen(),
    routes: [
      GoRoute(
        path: ':appealId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => AdminAppealDetailScreen(
          appealId: state.pathParameters['appealId']!,
        ),
      ),
    ],
  ),
  GoRoute(
    path: AppRoutes.adminComplaints,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, _) => const AdminComplaintsScreen(),
    routes: [
      GoRoute(
        path: ':complaintId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => AdminComplaintDetailScreen(
          complaintId: state.pathParameters['complaintId']!,
        ),
      ),
    ],
  ),
];
