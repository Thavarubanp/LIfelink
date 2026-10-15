import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/notifications/phone_notifications.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/theme_controller.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/floating_overlay_padding.dart';

class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final mode = ref.watch(themeModeProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Settings')),
      body: ContentWidth(
        child: ListView(
          padding: EdgeInsets.fromLTRB(
            16,
            16,
            16,
            16 + FloatingOverlayPadding.bottomOf(context),
          ),
          children: [
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
                onSelectionChanged: (selection) =>
                    ref.read(themeModeProvider.notifier).set(selection.first),
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
                  const Divider(),
                  const PhoneNotificationsTile(),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Phone notifications on/off; asks for the Android 13+ permission again when it was refused.
class PhoneNotificationsTile extends StatefulWidget {
  const PhoneNotificationsTile({super.key});

  @override
  State<PhoneNotificationsTile> createState() => _PhoneNotificationsTileState();
}

class _PhoneNotificationsTileState extends State<PhoneNotificationsTile> {
  bool? _enabled;

  @override
  void initState() {
    super.initState();
    _check();
  }

  Future<void> _check() async {
    var enabled = false;
    try {
      enabled = await PhoneNotifications.instance.areEnabled();
    } catch (_) {
      // The platform service can be unavailable before Android finishes attaching.
    }
    if (mounted) setState(() => _enabled = enabled);
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
              final allowed = await PhoneNotifications.instance
                  .requestPermission();
              await _check();
              if (!allowed && context.mounted) {
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
