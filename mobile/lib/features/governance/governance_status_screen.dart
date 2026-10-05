import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';

/// Suspended accounts land here (same rule as the web app). Step 4 (Mayureshan) adds the governance status
/// details and appeals behind this same route.
class GovernanceStatusScreen extends ConsumerWidget {
  const GovernanceStatusScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    return Scaffold(
      appBar: AppBar(title: const Text('Account status')),
      body: ContentWidth(
        maxWidth: 520,
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            const Icon(Icons.gpp_maybe_outlined, size: 56, color: AppColors.rose600),
            const SizedBox(height: 12),
            Text(
              user?.isSuspended == true ? 'Your account is suspended' : 'Account status',
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            const Text(
              'While suspended you can only read your status and appeal. Appeals arrive in the mobile app in Step 4; '
              'until then use the LifeLink web app.',
              textAlign: TextAlign.center,
              style: TextStyle(color: AppColors.slate500),
            ),
            const SizedBox(height: 24),
            OutlinedButton.icon(
              onPressed: () => ref.read(authControllerProvider.notifier).refreshUser(),
              icon: const Icon(Icons.refresh),
              label: const Text('Check again'),
            ),
            const SizedBox(height: 8),
            OutlinedButton.icon(
              onPressed: () => ref.read(authControllerProvider.notifier).logout(),
              icon: const Icon(Icons.logout),
              label: const Text('Sign out'),
            ),
          ],
        ),
      ),
    );
  }
}
