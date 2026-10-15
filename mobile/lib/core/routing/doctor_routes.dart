import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/doctor/doctor_home_screen.dart';
import '../../features/attention/tab_attention_controller.dart';
import '../../features/doctor/screening_models.dart';
import '../../features/doctor/screening_reports_screen.dart';
import '../../features/home/more_screen.dart';
import '../../features/home/role_shell.dart';
import '../../features/verification/request_donors_screen.dart';
import 'app_router.dart';
import 'routes.dart';

ReportTab _reportTab(GoRouterState state) =>
    switch (state.uri.queryParameters['view']) {
      'all' => ReportTab.all,
      'awaiting' => ReportTab.awaiting,
      'history' => ReportTab.history,
      _ => ReportTab.review,
    };

String? _requestStatus(GoRouterState state) {
  final value = state.uri.queryParameters['status'];
  if (value == 'all') return null;
  return value ?? 'Verified';
}

/// Doctor routes. Bottom navigation: Home · Requests · Reports · More.
List<RouteBase> doctorRoutes() => [
  roleShell(
    tabs: [
      const ShellTab('Home', Icons.home_outlined, Icons.home),
      ShellTab(
        'Requests',
        Icons.assignment_outlined,
        Icons.assignment,
        badge: tabAttentionCountProvider(TabAttentionKey.doctorRequests),
      ),
      ShellTab(
        'Reports',
        Icons.fact_check_outlined,
        Icons.fact_check,
        badge: tabAttentionCountProvider(TabAttentionKey.doctorReports),
      ),
      const ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
    ],
    branches: [
      (AppRoutes.doctorHome, const DoctorDashboardScreen()),
      (AppRoutes.doctorRequests, const DoctorRequestsScreen()),
      (AppRoutes.doctorReports, const ScreeningReportsScreen()),
      (
        AppRoutes.doctorMore,
        const MoreScreen(
          entries: [
            MoreEntry(
              'Hospital donations',
              Icons.local_hospital_outlined,
              AppRoutes.doctorHospitalDonations,
              subtitle: 'Approve or reject hospital donations',
            ),
          ],
        ),
      ),
    ],
    builders: {
      AppRoutes.doctorRequests: (state) =>
          DoctorRequestsScreen(initialStatus: _requestStatus(state)),
      AppRoutes.doctorReports: (state) =>
          ScreeningReportsScreen(initialTab: _reportTab(state)),
    },
  ),
  GoRoute(
    path: '/doctor/reports/:acceptanceId',
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, state) => ScreeningReportDetailScreen(
      acceptanceId: state.pathParameters['acceptanceId']!,
    ),
  ),
  GoRoute(
    path: '/doctor/requests/:requestId/donors',
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, state) => RequestDonorsScreen(
      requestId: state.pathParameters['requestId']!,
      isDoctor: true,
    ),
  ),
  GoRoute(
    path: AppRoutes.doctorHospitalDonations,
    parentNavigatorKey: rootNavigatorKey,
    builder: (_, _) => const HospitalDonationsScreen(),
  ),
];
