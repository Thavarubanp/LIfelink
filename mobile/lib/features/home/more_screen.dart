import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../notifications/unread_badge.dart';

class MoreEntry {
  const MoreEntry(this.label, this.icon, this.path, {this.subtitle});

  final String label;
  final IconData icon;
  final String path;
  final String? subtitle;
}

/// The "More" tab: the role's other screens, notifications, profile and sign out.
class MoreScreen extends ConsumerWidget {
  const MoreScreen({super.key, required this.entries});

  final List<MoreEntry> entries;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    return Scaffold(
      appBar: AppBar(title: const Text('More')),
      body: ContentWidth(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            if (user != null)
              Card(
                child: ListTile(
                  leading: CircleAvatar(
                    backgroundColor: AppColors.red600,
                    child: Text(
                      (user.firstName.isNotEmpty ? user.firstName[0] : '?').toUpperCase(),
                      style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700),
                    ),
                  ),
                  title: Text(user.fullName, style: const TextStyle(fontWeight: FontWeight.w700)),
                  subtitle: Text('${user.roleLabel} · ${user.email}'),
                  trailing: const Icon(Icons.chevron_right),
                  onTap: () => context.push(AppRoutes.profile),
                ),
              ),
            const SizedBox(height: 12),
            Card(
              child: Column(
                children: [
                  for (final e in entries)
                    ListTile(
                      leading: Icon(e.icon, color: AppColors.red600),
                      title: Text(e.label),
                      subtitle: e.subtitle == null ? null : Text(e.subtitle!),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => context.push(e.path),
                    ),
                  if (user != null && !user.hasRole(Roles.admin))
                    ListTile(
                      leading: const Icon(Icons.gavel_outlined, color: AppColors.red600),
                      title: const Text('Account status and appeals'),
                      trailing: const Icon(Icons.chevron_right),
                      onTap: () => context.push(AppRoutes.governanceStatus),
                    ),
                  ListTile(
                    leading: const Icon(Icons.history, color: AppColors.red600),
                    title: const Text('My activity'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push(AppRoutes.myActivity),
                  ),
                  ListTile(
                    leading: const UnreadBadge(child: Icon(Icons.notifications_outlined, color: AppColors.red600)),
                    title: const Text('Notifications'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push(AppRoutes.notifications),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: () async {
                if (await confirmDialog(context, title: 'Sign out', message: 'Sign out of LifeLink on this phone?', confirmLabel: 'Sign out')) {
                  await ref.read(authControllerProvider.notifier).logout();
                }
              },
              icon: const Icon(Icons.logout),
              label: const Text('Sign out'),
            ),
          ],
        ),
      ),
    );
  }
}
