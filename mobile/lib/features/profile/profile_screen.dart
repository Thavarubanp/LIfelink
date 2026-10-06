import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/config/env.dart';
import '../../core/notifications/phone_notifications.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/theme_controller.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../doctors/doctor_profile_card.dart';
import 'profile_repository.dart';

/// The signed-in account, the hospital for hospital staff, theme and sign out.
class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    final mode = ref.watch(themeModeProvider);
    if (user == null) return const Scaffold(body: LoadingView());
    final isHospital = user.hasRole(Roles.hospitalStaff);

    return Scaffold(
      appBar: AppBar(title: const Text('Profile')),
      body: ContentWidth(
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            SectionCard(
              title: user.fullName,
              icon: Icons.person_outline,
              trailing: StatusBadge(user.roleLabel, variant: BadgeVariant.info),
              child: Wrap(
                spacing: 24,
                runSpacing: 12,
                children: [
                  LabeledValue('Email', user.email),
                  LabeledValue(
                    'Account status',
                    user.accountStatus.isEmpty ? 'Active' : user.accountStatus,
                  ),
                  LabeledValue(
                    'Idle sign-out',
                    '${user.sessionIdleTimeoutMinutes.round()} minutes',
                  ),
                ],
              ),
            ),
            if (user.isDonorCapable) ...[
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: () =>
                    context.push(AppRoutes.userProfile(user.userId)),
                icon: const Icon(Icons.manage_accounts_outlined),
                label: const Text('View and edit full profile'),
              ),
            ],
            if (user.hasRole(Roles.doctor)) ...[
              const SizedBox(height: 12),
              const DoctorProfileCard(),
            ],
            if (isHospital) ...[
              const SizedBox(height: 12),
              const _HospitalCard(),
            ],
            const SizedBox(height: 12),
            SectionCard(
              title: 'Appearance',
              icon: Icons.palette_outlined,
              child: SegmentedButton<ThemeMode>(
                segments: const [
                  ButtonSegment(
                    value: ThemeMode.system,
                    label: Text('System'),
                    icon: Icon(Icons.phone_android),
                  ),
                  ButtonSegment(
                    value: ThemeMode.light,
                    label: Text('Light'),
                    icon: Icon(Icons.light_mode_outlined),
                  ),
                  ButtonSegment(
                    value: ThemeMode.dark,
                    label: Text('Dark'),
                    icon: Icon(Icons.dark_mode_outlined),
                  ),
                ],
                selected: {mode},
                onSelectionChanged: (s) =>
                    ref.read(themeModeProvider.notifier).set(s.first),
              ),
            ),
            const SizedBox(height: 12),
            Card(
              child: Column(
                children: [
                  ListTile(
                    leading: const Icon(Icons.password),
                    title: const Text('Change password'),
                    trailing: const Icon(Icons.chevron_right),
                    onTap: () => context.push(AppRoutes.changePassword),
                  ),
                  const _PhoneNotificationsTile(),
                  ListTile(
                    leading: const Icon(Icons.dns_outlined),
                    title: const Text('Server'),
                    subtitle: Text(Env.apiBaseUrl),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),
            OutlinedButton.icon(
              onPressed: () async {
                if (await confirmDialog(
                  context,
                  title: 'Sign out',
                  message: 'Sign out of LifeLink on this phone?',
                  confirmLabel: 'Sign out',
                )) {
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

class _HospitalCard extends ConsumerWidget {
  const _HospitalCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(myHospitalProvider);
    return SectionCard(
      title: 'Hospital',
      icon: Icons.local_hospital_outlined,
      child: AsyncView(
        value: value,
        onRetry: () => ref.invalidate(myHospitalProvider),
        data: (h) => Wrap(
          spacing: 24,
          runSpacing: 12,
          children: [
            LabeledValue('Name', h.name),
            LabeledValue(
              'Address',
              [
                h.address,
                h.city,
              ].whereType<String>().where((s) => s.isNotEmpty).join(', '),
            ),
            LabeledValue('Contact', h.contactNumber),
            LabeledValue('Email', h.email),
            LabeledValue('Packet shelf life', '${h.packetShelfLifeDays} days'),
            LabeledValue('Expiry alert window', '${h.expiryAlertDays} days'),
            const Text(
              'Hospital details are changed on the LifeLink web app.',
              style: TextStyle(fontSize: 12),
            ),
          ],
        ),
      ),
    );
  }
}

/// Phone notifications on/off; asks for the Android 13+ permission again when it was refused.
class _PhoneNotificationsTile extends StatefulWidget {
  const _PhoneNotificationsTile();

  @override
  State<_PhoneNotificationsTile> createState() =>
      _PhoneNotificationsTileState();
}

class _PhoneNotificationsTileState extends State<_PhoneNotificationsTile> {
  bool? _enabled;

  @override
  void initState() {
    super.initState();
    _check();
  }

  Future<void> _check() async {
    final on = await PhoneNotifications.instance.areEnabled();
    if (mounted) setState(() => _enabled = on);
  }

  @override
  Widget build(BuildContext context) => ListTile(
    leading: const Icon(Icons.notifications_active_outlined),
    title: const Text('Phone notifications'),
    subtitle: Text(
      _enabled == null
          ? 'Checking...'
          : _enabled!
          ? 'On: new messages and alerts, also when the app is closed'
          : 'Off: allow them to be told about messages and alerts',
    ),
    trailing: _enabled == false
        ? TextButton(
            onPressed: () async {
              final ok = await PhoneNotifications.instance.requestPermission();
              await _check();
              if (!ok && context.mounted) {
                showSnack(
                  context,
                  'Notifications are blocked. Allow them in Settings → Apps → LifeLink → Notifications.',
                  type: SnackType.warning,
                );
              }
            },
            child: const Text('Allow'),
          )
        : null,
  );
}
