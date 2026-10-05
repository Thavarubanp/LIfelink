import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme/app_theme.dart';
import 'notifications_repository.dart';

/// Puts the unread notification count on an icon.
class UnreadBadge extends ConsumerWidget {
  const UnreadBadge({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final count = ref.watch(unreadCountProvider);
    return Badge(
      isLabelVisible: count > 0,
      backgroundColor: AppColors.red600,
      label: Text(count > 99 ? '99+' : '$count'),
      child: child,
    );
  }
}
