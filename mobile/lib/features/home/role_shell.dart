import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart';
import 'package:go_router/go_router.dart';

import '../../core/widgets/common.dart';
import '../../core/widgets/floating_overlay_padding.dart';
import '../attention/tab_attention_controller.dart';
import '../notifications/unread_badge.dart';
import 'lifelink_app_bar.dart';

/// One bottom-navigation tab of a role's shell.
class ShellTab {
  const ShellTab(
    this.label,
    this.icon,
    this.selectedIcon, {
    this.showUnread = false,
    this.badge,
    this.attentionKey,
  });

  final String label;
  final IconData icon;
  final IconData selectedIcon;

  /// Shows the unread notification count on this tab (the "More" tab, which lists Notifications).
  final bool showUnread;

  /// Another count to show on this tab (e.g. the admin's attention counts).
  final ProviderListenable<int>? badge;

  /// A local seen marker to update when this destination is opened.
  final TabAttentionKey? attentionKey;
}

/// The role's main frame: bottom navigation on phones, a navigation rail on tablets.
class RoleShell extends ConsumerWidget {
  const RoleShell({super.key, required this.shell, required this.tabs});

  final StatefulNavigationShell shell;
  final List<ShellTab> tabs;

  void _go(WidgetRef ref, int index) {
    ref.read(tabAttentionProvider.notifier).markSeen(tabs[index].attentionKey);
    shell.goBranch(index, initialLocation: index == shell.currentIndex);
  }

  void _goHome() => shell.goBranch(0, initialLocation: true);

  Widget _icon(ShellTab tab, bool selected) {
    final icon = Icon(selected ? tab.selectedIcon : tab.icon);
    if (tab.badge != null) {
      return UnreadBadge(count: tab.badge, child: icon);
    }
    return tab.showUnread ? UnreadBadge(child: icon) : icon;
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Keeps exactly one role-wide badge polling controller alive with the shell.
    ref.watch(tabAttentionProvider);
    ref
        .read(tabAttentionProvider.notifier)
        .markSeen(tabs[shell.currentIndex].attentionKey);
    if (isWide(context)) {
      return Scaffold(
        appBar: LifeLinkAppBar(onBrandTap: _goHome),
        body: Row(
          children: [
            SafeArea(
              child: NavigationRail(
                selectedIndex: shell.currentIndex,
                onDestinationSelected: (index) => _go(ref, index),
                labelType: NavigationRailLabelType.all,
                destinations: [
                  for (final t in tabs)
                    NavigationRailDestination(
                      icon: _icon(t, false),
                      selectedIcon: _icon(t, true),
                      label: Text(t.label),
                    ),
                ],
              ),
            ),
            const VerticalDivider(width: 1),
            Expanded(child: shell),
          ],
        ),
      );
    }
    return Scaffold(
      appBar: LifeLinkAppBar(onBrandTap: _goHome),
      body: shell,
      bottomNavigationBar: BottomNavigationBarSizeReporter(
        child: NavigationBar(
          selectedIndex: shell.currentIndex,
          onDestinationSelected: (index) => _go(ref, index),
          destinations: [
            for (final t in tabs)
              NavigationDestination(
                icon: _icon(t, false),
                selectedIcon: _icon(t, true),
                label: t.label,
              ),
          ],
        ),
      ),
    );
  }
}
