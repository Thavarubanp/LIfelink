import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/doctor/doctor_home_screen.dart';
import '../../features/doctor/screening_reports_screen.dart';
import '../../features/home/more_screen.dart';
import '../../features/home/role_shell.dart';
import '../../features/verification/request_donors_screen.dart';
import 'app_router.dart';
import 'routes.dart';

/// Doctor routes (Step 3: Ahamed). Bottom navigation: Requests · Reports · More.
List<RouteBase> doctorRoutes() => [
      roleShell(
        tabs: const [
          ShellTab('Requests', Icons.assignment_outlined, Icons.assignment),
          ShellTab('Reports', Icons.fact_check_outlined, Icons.fact_check),
          ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
        ],
        branches: [
          (AppRoutes.doctorHome, const DoctorHomeScreen()),
          (AppRoutes.doctorReports, const ScreeningReportsScreen()),
          (
            AppRoutes.doctorMore,
            const MoreScreen(entries: [
              MoreEntry('Hospital donations', Icons.local_hospital_outlined, AppRoutes.doctorHospitalDonations,
                  subtitle: 'Approve or reject hospital donations'),
            ]),
          ),
        ],
      ),
      GoRoute(
        path: '/doctor/reports/:acceptanceId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => ScreeningReportDetailScreen(acceptanceId: state.pathParameters['acceptanceId']!),
      ),
      GoRoute(
        path: '/doctor/requests/:requestId/donors',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, state) => RequestDonorsScreen(requestId: state.pathParameters['requestId']!, isDoctor: true),
      ),
      GoRoute(
        path: AppRoutes.doctorHospitalDonations,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const HospitalDonationsScreen(),
      ),
    ];
