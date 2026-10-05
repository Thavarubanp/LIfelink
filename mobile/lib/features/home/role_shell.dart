import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/widgets/common.dart';
import '../notifications/unread_badge.dart';

/// One bottom-navigation tab of a role's shell.
class ShellTab {
  const ShellTab(this.label, this.icon, this.selectedIcon, {this.showUnread = false});

  final String label;
  final IconData icon;
  final IconData selectedIcon;

  /// Shows the unread notification count on this tab (the "More" tab, which lists Notifications).
  final bool showUnread;
}

/// The role's main frame: bottom navigation on phones, a navigation rail on tablets.
class RoleShell extends StatelessWidget {
  const RoleShell({super.key, required this.shell, required this.tabs});

  final StatefulNavigationShell shell;
  final List<ShellTab> tabs;

  void _go(int index) => shell.goBranch(index, initialLocation: index == shell.currentIndex);

  Widget _icon(ShellTab tab, bool selected) {
    final icon = Icon(selected ? tab.selectedIcon : tab.icon);
    return tab.showUnread ? UnreadBadge(child: icon) : icon;
  }

  @override
  Widget build(BuildContext context) {
    if (isWide(context)) {
      return Scaffold(
        body: Row(
          children: [
            SafeArea(
              child: NavigationRail(
                selectedIndex: shell.currentIndex,
                onDestinationSelected: _go,
                labelType: NavigationRailLabelType.all,
                destinations: [
                  for (final t in tabs)
                    NavigationRailDestination(icon: _icon(t, false), selectedIcon: _icon(t, true), label: Text(t.label)),
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
      body: shell,
      bottomNavigationBar: NavigationBar(
        selectedIndex: shell.currentIndex,
        onDestinationSelected: _go,
        destinations: [
          for (final t in tabs) NavigationDestination(icon: _icon(t, false), selectedIcon: _icon(t, true), label: t.label),
        ],
      ),
    );
  }
}
