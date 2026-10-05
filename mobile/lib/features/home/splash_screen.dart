import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import '../auth/auth_widgets.dart';

/// Shown while the stored session is checked, or when the server cannot be reached at start.
class SplashScreen extends ConsumerWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    final unreachable = auth.status == AuthStatus.unreachable;
    return Scaffold(
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const BrandHeader(subtitle: 'Securing your session'),
              const SizedBox(height: 28),
              if (!unreachable) ...[
                const CircularProgressIndicator(color: AppColors.red600),
                const SizedBox(height: 12),
                const Text('Verifying LifeLink session...', style: TextStyle(color: AppColors.slate500)),
              ] else ...[
                InfoBanner(auth.message ?? 'The LifeLink server cannot be reached.', color: AppColors.rose600, icon: Icons.wifi_off),
                const SizedBox(height: 16),
                FilledButton.icon(
                  onPressed: () => ref.read(authControllerProvider.notifier).restore(),
                  icon: const Icon(Icons.refresh),
                  label: const Text('Try again'),
                ),
                TextButton(
                  onPressed: () => ref.read(authControllerProvider.notifier).logout(),
                  child: const Text('Sign out'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
