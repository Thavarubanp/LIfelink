import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart';

import '../../core/theme/app_theme.dart';
import 'notifications_repository.dart';

/// Puts the unread notification count on an icon.
class UnreadBadge extends ConsumerWidget {
  const UnreadBadge({super.key, required this.child, this.count});

  final Widget child;
  final ProviderListenable<int>? count;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(count ?? unreadCountProvider);
    return Badge(
      isLabelVisible: value > 0,
      backgroundColor: AppColors.red600,
      label: Text(value > 99 ? '99+' : '$value'),
      child: child,
    );
  }
}
