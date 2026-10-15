import 'package:flutter/material.dart';
import 'package:flutter/foundation.dart';
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
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final showIcons = constraints.maxWidth >= 340;
                  return SegmentedButton<ThemeMode>(
                    showSelectedIcon: false,
                    segments: [
                      ButtonSegment(
                        value: ThemeMode.system,
                        label: const Text('System'),
                        icon: showIcons
                            ? const Icon(Icons.phone_android)
                            : null,
                      ),
                      ButtonSegment(
                        value: ThemeMode.light,
                        label: const Text('Light'),
                        icon: showIcons
                            ? const Icon(Icons.light_mode_outlined)
                            : null,
                      ),
                      ButtonSegment(
                        value: ThemeMode.dark,
                        label: const Text('Dark'),
                        icon: showIcons
                            ? const Icon(Icons.dark_mode_outlined)
                            : null,
                      ),
                    ],
                    selected: {mode},
                    onSelectionChanged: (selection) => ref
                        .read(themeModeProvider.notifier)
                        .set(selection.first),
                  );
                },
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

String phoneNotificationsEnabledDescription(TargetPlatform platform) =>
    platform == TargetPlatform.iOS
    ? 'On: new messages and alerts while LifeLink is open'
    : 'On: new messages and alerts, also when the app is closed';

/// Phone notifications on/off; asks for platform notification permission again when it was refused.
class PhoneNotificationsTile extends StatefulWidget {
  const PhoneNotificationsTile({super.key});

  @override
  State<PhoneNotificationsTile> createState() => _PhoneNotificationsTileState();
}

class _PhoneNotificationsTileState extends State<PhoneNotificationsTile> {
  bool? _enabled;
  bool _requesting = false;

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
          ? phoneNotificationsEnabledDescription(defaultTargetPlatform)
          : 'Off: allow them to be told about messages and alerts',
    ),
    trailing: _enabled == false
        ? TextButton(
            onPressed: _requesting
                ? null
                : () async {
                    setState(() => _requesting = true);
                    try {
                      final allowed = await PhoneNotifications.instance
                          .requestPermission();
                      await _check();
                      if (!allowed && context.mounted) {
                        showSnack(
                          context,
                          defaultTargetPlatform == TargetPlatform.iOS
                              ? 'Notifications are blocked. Open Settings, select LifeLink, and allow Notifications.'
                              : 'Notifications are blocked. Allow them in Settings → Apps → LifeLink → Notifications.',
                          type: SnackType.warning,
                        );
                      }
                    } finally {
                      if (mounted) setState(() => _requesting = false);
                    }
                  },
            child: _requesting
                ? const SizedBox(
                    width: 18,
                    height: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Text('Allow'),
          )
        : null,
  );
}
