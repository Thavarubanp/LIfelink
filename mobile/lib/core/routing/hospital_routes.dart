import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../features/home/more_screen.dart';
import '../../features/home/role_shell.dart';
import '../../features/home/stub_screen.dart';
import 'app_router.dart';
import 'routes.dart';

/// Hospital staff routes (Step 1: Thavaruban). Verify requests and doctors are Step 3 (Ahamed).
List<RouteBase> hospitalRoutes() => [
      roleShell(
        tabs: const [
          ShellTab('Home', Icons.home_outlined, Icons.home),
          ShellTab('Inventory', Icons.inventory_2_outlined, Icons.inventory_2),
          ShellTab('Transfers', Icons.swap_horiz, Icons.swap_horiz),
          ShellTab('More', Icons.menu, Icons.menu, showUnread: true),
        ],
        branches: [
          (AppRoutes.hospitalHome, _part('Hospital home', 2)),
          (AppRoutes.hospitalInventory, _part('Blood inventory', 2)),
          (AppRoutes.hospitalTransfers, _part('Transfers', 4)),
          (
            AppRoutes.hospitalMore,
            const MoreScreen(entries: [
              MoreEntry('Emergencies', Icons.emergency_outlined, AppRoutes.hospitalEmergencies,
                  subtitle: 'Raise and follow hospital emergencies'),
              MoreEntry('Donate blood', Icons.volunteer_activism_outlined, AppRoutes.hospitalDonate,
                  subtitle: 'Donate packets to public requests'),
              MoreEntry('Scan packet', Icons.qr_code_scanner, AppRoutes.hospitalScan,
                  subtitle: 'Open a packet from its QR code'),
              MoreEntry('Recommendations', Icons.auto_awesome_outlined, AppRoutes.hospitalRecommendations,
                  subtitle: 'Inventory analysis alerts'),
              MoreEntry('Verify requests', Icons.fact_check_outlined, AppRoutes.hospitalVerifyRequests),
              MoreEntry('Doctors', Icons.medical_services_outlined, AppRoutes.hospitalDoctors),
            ]),
          ),
        ],
      ),
      GoRoute(path: AppRoutes.hospitalEmergencies, parentNavigatorKey: rootNavigatorKey, builder: (_, _) => _part('Emergencies', 4)),
      GoRoute(path: AppRoutes.hospitalDonate, parentNavigatorKey: rootNavigatorKey, builder: (_, _) => _part('Donate blood', 4)),
      GoRoute(path: AppRoutes.hospitalScan, parentNavigatorKey: rootNavigatorKey, builder: (_, _) => _part('Scan packet', 3)),
      GoRoute(
        path: AppRoutes.hospitalRecommendations,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => _part('Recommendations', 5),
      ),
      GoRoute(
        path: '/hospital/packets/:packetId',
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => _part('Packet', 3),
      ),
      GoRoute(
        path: AppRoutes.hospitalVerifyRequests,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const StubScreen(
            title: 'Verify blood requests', step: 3, owner: 'Ahamed MSA', description: 'Verify requests and assign a doctor.'),
      ),
      GoRoute(
        path: AppRoutes.hospitalDoctors,
        parentNavigatorKey: rootNavigatorKey,
        builder: (_, _) => const StubScreen(
            title: 'Doctors', step: 3, owner: 'Ahamed MSA', description: 'Add and manage your hospital\'s doctors.'),
      ),
    ];

/// Step 1 screens not built yet in this part (replaced part by part).
Widget _part(String title, int part) =>
    StubScreen(title: title, step: 1, owner: 'Thavaruban P', description: '$title arrives in Step 1, part $part.');
