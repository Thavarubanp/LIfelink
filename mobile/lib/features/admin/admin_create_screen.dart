import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../blood_requests/public_requests_screen.dart';

/// The admin's donor-capable area: shortcuts first, then the shared public
/// blood-request list.
class AdminCreateScreen extends StatelessWidget {
  const AdminCreateScreen({super.key});

  @override
  Widget build(BuildContext context) => PublicRequestsScreen(
    title: 'Create and donate',
    leading: SectionCard(
      title: 'Donor actions',
      icon: Icons.add_circle_outline,
      child: Column(
        children: [
          ListTile(
            key: const Key('admin-create-request'),
            contentPadding: EdgeInsets.zero,
            leading: const Icon(
              Icons.add_circle_outline,
              color: AppColors.red600,
            ),
            title: const Text('Create blood request'),
            subtitle: const Text('Ask the network for blood'),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => context.push(AppRoutes.createRequest),
          ),
          const Divider(),
          ListTile(
            key: const Key('admin-my-acceptances'),
            contentPadding: EdgeInsets.zero,
            leading: const Icon(
              Icons.volunteer_activism_outlined,
              color: AppColors.red600,
            ),
            title: const Text('My acceptances'),
            subtitle: const Text('Screening and donation participation'),
            trailing: const Icon(Icons.chevron_right),
            onTap: () => context.push(AppRoutes.adminAcceptances),
          ),
        ],
      ),
    ),
  );
}
