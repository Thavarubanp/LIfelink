import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/admin/admin_home_screen.dart';
import '../../features/admin/appeals/admin_appeals.dart';
import '../../features/admin/attention_controller.dart';
import '../../features/admin/complaints/admin_complaints.dart';
import '../../features/admin/registrations/registration_detail_screen.dart';
import '../../features/admin/registrations/registrations_screen.dart';
import '../../features/home/more_screen.dart';
import '../../features/home/role_shell.dart';
import '../../features/home/stub_screen.dart';
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
          (AppRoutes.adminActivity, _part('Activity log', 5)),
          (
            AppRoutes.adminMore,
            const MoreScreen(entries: [
              MoreEntry('Hospital registrations', Icons.domain_add_outlined, AppRoutes.adminRegistrations),
              MoreEntry('Appeals', Icons.gavel_outlined, AppRoutes.adminAppeals),
              MoreEntry('Complaints', Icons.report_outlined, AppRoutes.adminComplaints),
              MoreEntry('Donor features', Icons.bloodtype_outlined, AppRoutes.donorRequests),
            ]),
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

/// Step 4 screens not built yet in this part (replaced part by part).
Widget _part(String title, int part) =>
    StubScreen(title: title, step: 4, owner: 'Mayureshan P', description: '$title arrives in Step 4, part $part.');
