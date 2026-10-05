import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';

/// Hospital staff whose hospital registration is not approved yet. Registration (documents, the conversation
/// with the administrator, replies) stays on the web app; here the staff member can check the status again.
class WaitingApprovalScreen extends ConsumerStatefulWidget {
  const WaitingApprovalScreen({super.key});

  @override
  ConsumerState<WaitingApprovalScreen> createState() => _WaitingApprovalScreenState();
}

class _WaitingApprovalScreenState extends ConsumerState<WaitingApprovalScreen> {
  bool _checking = false;

  Future<void> _check() async {
    setState(() => _checking = true);
    try {
      final user = await ref.read(authControllerProvider.notifier).refreshUser();
      if (mounted && user?.isUnapprovedHospitalStaff == true) {
        showSnack(context, 'Your hospital is still waiting for approval.');
      }
    } catch (e) {
      if (mounted) showSnack(context, ApiError.from(e).message, type: SnackType.error);
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final status = ref.watch(authControllerProvider).user?.hospitalApprovalStatus ?? 'Pending';
    final rejected = status == 'Rejected';
    return Scaffold(
      appBar: AppBar(title: const Text('Hospital registration')),
      body: ContentWidth(
        maxWidth: 520,
        child: ListView(
          padding: const EdgeInsets.all(24),
          children: [
            const Icon(Icons.hourglass_top_rounded, size: 56, color: AppColors.amber600),
            const SizedBox(height: 12),
            Center(child: StatusBadge(status, variant: rejected ? BadgeVariant.danger : BadgeVariant.warning)),
            const SizedBox(height: 16),
            Text(
              rejected
                  ? 'The administrator returned your hospital registration with a report.'
                  : 'Your hospital registration is waiting for the administrator.',
              textAlign: TextAlign.center,
              style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            const Text(
              'Open the LifeLink web app to read the administrator\'s messages, reply or correct your documents. '
              'The mobile app opens once the hospital is approved.',
              textAlign: TextAlign.center,
              style: TextStyle(color: AppColors.slate500),
            ),
            const SizedBox(height: 24),
            BusyButton(label: 'Check again', icon: Icons.refresh, busy: _checking, onPressed: _check),
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
