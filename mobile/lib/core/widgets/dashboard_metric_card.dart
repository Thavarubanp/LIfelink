import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

/// A dashboard metric with consistent hierarchy, overflow handling, semantics,
/// and interaction feedback. Data and navigation remain owned by each screen.
class DashboardMetricCard extends StatelessWidget {
  const DashboardMetricCard({
    super.key,
    required this.label,
    required this.value,
    required this.detail,
    required this.icon,
    required this.color,
    this.interactionKey,
    this.onTap,
    this.loading = false,
    this.compact = false,
  });

  final String label;
  final String value;
  final String detail;
  final IconData icon;
  final Color color;
  final Key? interactionKey;
  final VoidCallback? onTap;
  final bool loading;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final accent = AppColors.readableAccent(context, color);
    final content = Padding(
      padding: EdgeInsets.all(compact ? AppSpacing.md : AppSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: compact ? 34 : 38,
                height: compact ? 34 : 38,
                decoration: BoxDecoration(
                  color: accent.withValues(alpha: 0.12),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Icon(icon, color: accent, size: compact ? 19 : 21),
              ),
              const SizedBox(width: AppSpacing.sm),
              Expanded(
                child: Text(
                  label,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.labelMedium?.copyWith(
                    color: scheme.onSurfaceVariant,
                    fontWeight: FontWeight.w700,
                    height: 1.2,
                  ),
                ),
              ),
              if (onTap != null)
                Icon(
                  Icons.chevron_right_rounded,
                  size: 20,
                  color: scheme.onSurfaceVariant,
                ),
            ],
          ),
          const Spacer(),
          if (loading)
            const SizedBox(
              width: 22,
              height: 22,
              child: CircularProgressIndicator(strokeWidth: 2.5),
            )
          else
            SizedBox(
              width: double.infinity,
              child: FittedBox(
                fit: BoxFit.scaleDown,
                alignment: Alignment.centerLeft,
                child: Text(
                  value,
                  maxLines: 1,
                  style: Theme.of(context).textTheme.headlineSmall
                      ?.copyWith(fontWeight: FontWeight.w800),
                ),
              ),
            ),
          const SizedBox(height: AppSpacing.xs),
          Text(
            detail,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: loading ? scheme.onSurfaceVariant : accent,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );

    return Semantics(
      button: onTap != null,
      label: '$label: ${loading ? 'Loading' : value}. $detail',
      child: Card(
        clipBehavior: Clip.antiAlias,
        child: onTap == null
            ? content
            : InkWell(key: interactionKey, onTap: onTap, child: content),
      ),
    );
  }
}

/// Equal-height metric layout with role-specific column counts.
class DashboardMetricGrid extends StatelessWidget {
  const DashboardMetricGrid({
    super.key,
    required this.children,
    this.narrowColumns = 2,
    this.wideColumns = 4,
    this.breakpoint = 700,
    this.compact = false,
    this.spacing = 10,
  });

  final List<Widget> children;
  final int narrowColumns;
  final int wideColumns;
  final double breakpoint;
  final bool compact;
  final double spacing;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final columns = constraints.maxWidth >= breakpoint
          ? wideColumns
          : narrowColumns;
      final scale = MediaQuery.textScalerOf(context).scale(1);
      final baseHeight = compact ? 138.0 : 148.0;
      final maxHeight = compact ? 218.0 : 228.0;
      final height = (baseHeight + (scale - 1) * 80).clamp(
        baseHeight,
        maxHeight,
      );
      return GridView.builder(
        shrinkWrap: true,
        physics: const NeverScrollableScrollPhysics(),
        gridDelegate: SliverGridDelegateWithFixedCrossAxisCount(
          crossAxisCount: columns,
          crossAxisSpacing: spacing,
          mainAxisSpacing: spacing,
          mainAxisExtent: height,
        ),
        itemCount: children.length,
        itemBuilder: (_, index) => children[index],
      );
    },
  );
}
