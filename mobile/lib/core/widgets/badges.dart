import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

enum BadgeVariant { success, warning, danger, info, primary, neutral, blood }

/// Small coloured status label (the web app's Badge).
class StatusBadge extends StatelessWidget {
  const StatusBadge(this.label, {super.key, this.variant = BadgeVariant.neutral, this.icon});

  final String label;
  final BadgeVariant variant;
  final IconData? icon;

  static Color colorOf(BadgeVariant v) => switch (v) {
        BadgeVariant.success => AppColors.emerald600,
        BadgeVariant.warning => AppColors.amber600,
        BadgeVariant.danger => AppColors.rose600,
        BadgeVariant.info => AppColors.blue600,
        BadgeVariant.primary => AppColors.violet600,
        BadgeVariant.blood => AppColors.red600,
        BadgeVariant.neutral => AppColors.slate500,
      };

  @override
  Widget build(BuildContext context) {
    final color = colorOf(variant);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: color.withValues(alpha: 0.35)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[Icon(icon, size: 12, color: color), const SizedBox(width: 4)],
          Text(label, style: TextStyle(color: color, fontSize: 11.5, fontWeight: FontWeight.w700)),
        ],
      ),
    );
  }
}

/// Blood group chip, e.g. "O-".
class BloodGroupBadge extends StatelessWidget {
  const BloodGroupBadge(this.group, {super.key, this.large = false});

  final String group;
  final bool large;

  @override
  Widget build(BuildContext context) => Container(
        padding: EdgeInsets.symmetric(horizontal: large ? 12 : 8, vertical: large ? 6 : 3),
        decoration: BoxDecoration(color: AppColors.red600, borderRadius: BorderRadius.circular(8)),
        child: Text(
          group,
          style: TextStyle(color: Colors.white, fontWeight: FontWeight.w800, fontSize: large ? 16 : 12),
        ),
      );
}

/// "Suspended by admin" label with the reason on long-press.
class SuspendedBadge extends StatelessWidget {
  const SuspendedBadge({super.key, this.reason});

  final String? reason;

  @override
  Widget build(BuildContext context) => Tooltip(
        message: reason?.isNotEmpty == true ? reason! : 'Suspended by the administrator',
        child: const StatusBadge('Suspended by admin', variant: BadgeVariant.danger, icon: Icons.block),
      );
}
