import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/admin/admin_home_screen.dart';
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
import 'app_router.dart';
import 'routes.dart';

/// Admin routes (Step 4: Mayureshan). Bottom navigation: Home · Attention · Activity · More.
List<RouteBase> adminRoutes() => [
      roleShell(
        tabs: [
          const ShellTab('Home', Icons.dashboard_outlined, Icons.dashboard),
          ShellTab('Attention', Icons.flag_outlined, Icons.flag, badge: attentionCountProvider('total')),
          ShellTab('Activity', Icons.history, Icons.history, badge: attentionCountProvider('activity')),
          const ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
        ],
        branches: [
          (AppRoutes.adminHome, const AdminHomeScreen()),
          (AppRoutes.adminAttention, const AttentionScreen()),
          (AppRoutes.adminActivity, const AdminActivityScreen()),
          (
            AppRoutes.adminMore,
            const MoreScreen(entries: [
              MoreEntry('Hospital registrations', Icons.domain_add_outlined, AppRoutes.adminRegistrations),
              MoreEntry('Appeals', Icons.gavel_outlined, AppRoutes.adminAppeals),
              MoreEntry('Complaints', Icons.report_outlined, AppRoutes.adminComplaints),
              MoreEntry('Users', Icons.people_outline, AppRoutes.adminUsers),
              MoreEntry('Hospitals', Icons.local_hospital_outlined, AppRoutes.adminHospitals),
              MoreEntry('Donate blood', Icons.bloodtype_outlined, AppRoutes.donorRequests,
                  subtitle: 'Read-only public request browser'),
              MoreEntry('Create blood request', Icons.add_circle_outline, AppRoutes.createRequest),
            ]),
          ),
        ],
        builders: {
          AppRoutes.adminActivity: (state) => AdminActivityScreen(initialTab: state.uri.queryParameters['tab']),
        },
      ),
      GoRoute(
        path: AppRoutes.adminUsers,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const AdminUsersScreen(),
        routes: [
          GoRoute(
            path: ':userId',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) => AdminUserProfileScreen(userId: state.pathParameters['userId']!, actions: userAdminActions),
            routes: [
              GoRoute(
                path: 'activity',
                parentNavigatorKey: rootNavigatorKey,
                builder: (_, state) => userActivityScreen(state.pathParameters['userId']!),
              ),
            ],
          ),
        ],
      ),
      GoRoute(
        path: AppRoutes.adminHospitals,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const AdminHospitalsScreen(),
        routes: [
          GoRoute(
            path: ':hospitalId',
            parentNavigatorKey: rootNavigatorKey,
            builder: (_, state) => AdminHospitalProfileScreen(hospitalId: state.pathParameters['hospitalId']!, actions: hospitalAdminActions),
            routes: [
              GoRoute(
                path: 'activity',
                parentNavigatorKey: rootNavigatorKey,
                builder: (_, state) => hospitalActivityScreen(state.pathParameters['hospitalId']!),
              ),
            ],
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
            builder: (_, state) => RegistrationDetailScreen(hospitalId: state.pathParameters['hospitalId']!),
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
            builder: (_, state) => AdminAppealDetailScreen(appealId: state.pathParameters['appealId']!),
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
            builder: (_, state) => AdminComplaintDetailScreen(complaintId: state.pathParameters['complaintId']!),
          ),
        ],
      ),
    ];
