import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/dashboard_metric_card.dart';
import '../../core/widgets/floating_overlay_padding.dart';
import '../../core/widgets/state_views.dart';
import '../analysis/analysis_panel.dart';
import '../emergencies/emergency_repository.dart';
import '../home/shell_page_header.dart';
import '../notifications/notifications_repository.dart';
import '../profile/profile_repository.dart';
import 'inventory_models.dart';
import 'inventory_repository.dart';

/// Hospital home (the web dashboard): stock cards per blood group (tap: that group in Inventory), the critical
/// emergencies and low-stock cards (tap: details), and the inventory analysis panel.
class HospitalHomeScreen extends ConsumerWidget {
  const HospitalHomeScreen({super.key});

  Future<void> _refresh(WidgetRef ref) async {
    ref.invalidate(hospitalInventoryProvider);
    ref.invalidate(criticalEmergenciesProvider);
    await Future.wait([
      ref
          .read(hospitalInventoryProvider.future)
          .catchError((_) => <InventoryItem>[]),
      ref
          .read(criticalEmergenciesProvider.future)
          .catchError((_) => <EmergencyRequest>[]),
    ]);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final hospital = ref.watch(myHospitalProvider);
    final inventory = ref.watch(hospitalInventoryProvider);
    final emergencies = ref.watch(criticalEmergenciesProvider);

    return Scaffold(
      body: RefreshIndicator(
        color: AppColors.red600,
        onRefresh: () => _refresh(ref),
        child: ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: EdgeInsets.fromLTRB(
            16,
            16,
            16,
            16 + FloatingOverlayPadding.bottomOf(context),
          ),
          children: [
            ContentWidth(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ShellPageHeader(title: hospital.value?.name ?? 'Hospital'),
                  const SizedBox(height: 16),
                  _actions(context),
                  const SizedBox(height: 16),
                  _kpis(context, ref, inventory, emergencies),
                  const SizedBox(height: 16),
                  InventoryAnalysisPanel(
                    onRunComplete: () {
                      ref.invalidate(hospitalInventoryProvider);
                      ref.invalidate(notificationsProvider);
                      ref.read(unreadCountProvider.notifier).refresh();
                    },
                  ),
                  const SizedBox(height: 16),
                  SectionCard(
                    title: 'Stock by blood group',
                    icon: Icons.water_drop_outlined,
                    trailing: TextButton(
                      onPressed: () => context.go(AppRoutes.hospitalInventory),
                      child: const Text('Inventory'),
                    ),
                    child: AsyncView(
                      value: inventory,
                      onRetry: () => ref.invalidate(hospitalInventoryProvider),
                      loadingMessage: 'Loading stock levels...',
                      data: (list) => list.isEmpty
                          ? const EmptyView(
                              icon: Icons.inventory_2_outlined,
                              message: 'No blood groups yet. Add packets or a blood group in Inventory.',
                            )
                          : _StockGrid(items: list),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _actions(BuildContext context) {
    final actions = [
      _QuickAction(
        buttonKey: const Key('quick-action-emergencies'),
        label: 'Emergencies',
        icon: Icons.bolt,
        onTap: () => context.push(AppRoutes.hospitalEmergencies),
        emphasized: true,
      ),
      _QuickAction(
        buttonKey: const Key('quick-action-create-request'),
        label: 'Create blood request',
        icon: Icons.add_circle_outline,
        onTap: () => context.push(AppRoutes.createRequest),
      ),
      _QuickAction(
        buttonKey: const Key('quick-action-donate'),
        label: 'Donate blood',
        icon: Icons.volunteer_activism_outlined,
        onTap: () => context.push(AppRoutes.hospitalDonate),
      ),
      _QuickAction(
        buttonKey: const Key('quick-action-verify'),
        label: 'Verify requests',
        icon: Icons.fact_check_outlined,
        onTap: () => context.push(AppRoutes.hospitalVerifyRequests),
      ),
    ];
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = constraints.maxWidth >= 600 ? 4 : 2;
        const gap = 10.0;
        final width = (constraints.maxWidth - gap * (columns - 1)) / columns;
        return Wrap(
          spacing: gap,
          runSpacing: gap,
          children: [
            for (final action in actions) SizedBox(width: width, child: action),
          ],
        );
      },
    );
  }

  Widget _kpis(
    BuildContext context,
    WidgetRef ref,
    AsyncValue<List<InventoryItem>> inventory,
    AsyncValue<List<EmergencyRequest>> emergencies,
  ) {
    final items = inventory.value ?? const <InventoryItem>[];
    final total = items.fold<int>(0, (sum, i) => sum + i.unitsAvailable);
    final low = items.where((i) => i.isLowStock).toList();
    final critical = emergencies.value ?? const <EmergencyRequest>[];
    final inventoryUnavailable = inventory.hasError && !inventory.hasValue;
    final emergenciesUnavailable =
        emergencies.hasError && !emergencies.hasValue;
    final cards = [
      _KpiCard(
        label: 'Blood bank stock',
        value: inventoryUnavailable
            ? 'Unavailable'
            : inventory.isLoading && !inventory.hasValue
            ? null
            : '$total units',
        caption: inventoryUnavailable
            ? 'Could not load · tap to retry'
            : 'Across ${items.length} blood groups',
        color: inventoryUnavailable ? AppColors.rose600 : AppColors.emerald600,
        onTap: inventoryUnavailable
            ? () => ref.invalidate(hospitalInventoryProvider)
            : null,
      ),
      _KpiCard(
        key: const Key('kpi-emergencies'),
        label: 'Critical emergencies',
        value: emergenciesUnavailable
            ? 'Unavailable'
            : emergencies.isLoading && !emergencies.hasValue
            ? null
            : '${critical.length}',
        caption: emergenciesUnavailable
            ? 'Could not load · tap to retry'
            : 'Active broadcasts · details',
        color: AppColors.rose600,
        onTap: emergenciesUnavailable
            ? () => ref.invalidate(criticalEmergenciesProvider)
            : () => _showEmergencies(context, critical),
      ),
      _KpiCard(
        key: const Key('kpi-low-stock'),
        label: 'Low stock',
        value: inventoryUnavailable
            ? 'Unavailable'
            : inventory.isLoading && !inventory.hasValue
            ? null
            : '${low.length}',
        caption: inventoryUnavailable
            ? 'Could not load · tap to retry'
            : 'Below threshold · details',
        color: inventoryUnavailable ? AppColors.rose600 : AppColors.amber600,
        onTap: inventoryUnavailable
            ? () => ref.invalidate(hospitalInventoryProvider)
            : () => _showLowStock(context, low),
      ),
    ];
    return DashboardMetricGrid(
      narrowColumns: 1,
      wideColumns: 3,
      breakpoint: 600,
      children: cards,
    );
  }

  void _showEmergencies(
    BuildContext context,
    List<EmergencyRequest> list,
  ) => showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    builder: (ctx) => _DetailSheet(
      title: 'Critical emergencies (${list.length})',
      footer: TextButton(
        onPressed: () {
          Navigator.of(ctx).pop();
          context.push(AppRoutes.hospitalEmergencies);
        },
        child: const Text('Open emergencies'),
      ),
      children: list.isEmpty
          ? [
              const Padding(
                padding: EdgeInsets.all(16),
                child: Text('There are no active critical emergencies.'),
              ),
            ]
          : [
              for (final e in list)
                ListTile(
                  leading: BloodGroupBadge(e.bloodGroup),
                  title: Text('${e.hospitalName} · ${e.unitsRequired} unit(s)'),
                  subtitle: Text(
                    '${e.reason.isEmpty ? '' : '${e.reason}\n'}${Fmt.sriLankaDateTime(e.createdAt)} · ${e.status}',
                  ),
                  isThreeLine: e.reason.isNotEmpty,
                ),
            ],
    ),
  );

  void _showLowStock(BuildContext context, List<InventoryItem> list) =>
      showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        builder: (ctx) => _DetailSheet(
          title: 'Low stock (${list.length})',
          subtitle: 'A blood group is low when its available units are below its minimum threshold.',
          children: list.isEmpty
              ? [
                  const Padding(
                    padding: EdgeInsets.all(16),
                    child: Text('No blood group is below its threshold.'),
                  ),
                ]
              : [
                  for (final i in list)
                    ListTile(
                      leading: const Icon(
                        Icons.warning_amber_rounded,
                        color: AppColors.amber500,
                      ),
                      title: Row(
                        children: [
                          BloodGroupBadge(i.bloodGroup),
                          const SizedBox(width: 8),
                          Text('${i.unitsAvailable} unit(s)'),
                        ],
                      ),
                      subtitle: Text('Threshold ${i.minimumThreshold}'),
                      trailing: const Text(
                        'View',
                        style: TextStyle(
                          color: AppColors.red600,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      onTap: () {
                        Navigator.of(ctx).pop();
                        context.go(inventoryGroupPath(i.bloodGroup));
                      },
                    ),
                ],
        ),
      );
}

class _QuickAction extends StatelessWidget {
  const _QuickAction({
    required this.buttonKey,
    required this.label,
    required this.icon,
    required this.onTap,
    this.emphasized = false,
  });

  final Key buttonKey;
  final String label;
  final IconData icon;
  final VoidCallback onTap;
  final bool emphasized;

  @override
  Widget build(BuildContext context) => SizedBox(
    height: 76,
    child: emphasized
        ? FilledButton(
            key: buttonKey,
            onPressed: onTap,
            child: _content(Colors.white),
          )
        : OutlinedButton(
            key: buttonKey,
            onPressed: onTap,
            child: _content(Theme.of(context).colorScheme.onSurface),
          ),
  );

  Widget _content(Color color) => Column(
    mainAxisAlignment: MainAxisAlignment.center,
    children: [
      Icon(icon, color: color),
      const SizedBox(height: 5),
      FittedBox(
        fit: BoxFit.scaleDown,
        child: Text(
          label,
          maxLines: 1,
          softWrap: false,
          style: TextStyle(color: color, fontWeight: FontWeight.w600),
        ),
      ),
    ],
  );
}

/// Inventory tab opened on one blood group.
String inventoryGroupPath(String group) =>
    '${AppRoutes.hospitalInventory}?group=${Uri.encodeQueryComponent(group)}';

class _KpiCard extends StatelessWidget {
  const _KpiCard({
    super.key,
    required this.label,
    required this.value,
    required this.caption,
    required this.color,
    this.onTap,
  });

  final String label;
  final String? value;
  final String caption;
  final Color color;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => DashboardMetricCard(
    label: label,
    value: value ?? '',
    detail: caption,
    icon: switch (label) {
      'Blood bank stock' => Icons.inventory_2_outlined,
      'Critical emergencies' => Icons.emergency_outlined,
      _ => Icons.warning_amber_outlined,
    },
    color: color,
    onTap: onTap,
    loading: value == null,
  );
}

class _StockGrid extends StatelessWidget {
  const _StockGrid({required this.items});

  final List<InventoryItem> items;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, c) {
      final columns = c.maxWidth >= 600 ? 4 : 2;
      const gap = 10.0;
      final width = (c.maxWidth - gap * (columns - 1)) / columns;
      return Wrap(
        spacing: gap,
        runSpacing: gap,
        children: [
          for (final i in items)
            SizedBox(
              width: width,
              child: _StockTile(item: i),
            ),
        ],
      );
    },
  );
}

class _StockTile extends StatelessWidget {
  const _StockTile({required this.item});

  final InventoryItem item;

  @override
  Widget build(BuildContext context) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    final max = item.maximumCapacity > 0 ? item.maximumCapacity : 100;
    final fill = (item.unitsAvailable / max).clamp(0.0, 1.0);
    return Material(
      color: dark ? AppColors.slate800 : AppColors.slate50,
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: () => context.go(inventoryGroupPath(item.bloodGroup)),
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  BloodGroupBadge(item.bloodGroup),
                  const Spacer(),
                  StatusBadge(
                    item.isLowStock ? 'Low' : 'Optimal',
                    variant: item.isLowStock
                        ? BadgeVariant.warning
                        : BadgeVariant.success,
                  ),
                ],
              ),
              const SizedBox(height: 10),
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  Text(
                    '${item.unitsAvailable}',
                    style: const TextStyle(
                      fontSize: 18,
                      fontWeight: FontWeight.w800,
                    ),
                  ),
                  const Spacer(),
                  Text(
                    '/ $max units',
                    style: const TextStyle(
                      fontSize: 11,
                      color: AppColors.slate500,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(
                  value: fill,
                  minHeight: 6,
                  color: item.isLowStock
                      ? AppColors.amber500
                      : AppColors.emerald600,
                  backgroundColor: dark
                      ? AppColors.slate700
                      : AppColors.slate200,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _DetailSheet extends StatelessWidget {
  const _DetailSheet({
    required this.title,
    required this.children,
    this.subtitle,
    this.footer,
  });

  final String title;
  final String? subtitle;
  final List<Widget> children;
  final Widget? footer;

  @override
  Widget build(BuildContext context) => SafeArea(
    child: ConstrainedBox(
      constraints: BoxConstraints(
        maxHeight: MediaQuery.sizeOf(context).height * 0.8,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
            child: Text(
              title,
              style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700),
            ),
          ),
          if (subtitle != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              child: Text(
                subtitle!,
                style: const TextStyle(fontSize: 12, color: AppColors.slate500),
              ),
            ),
          Flexible(child: ListView(shrinkWrap: true, children: children)),
          ?footer,
        ],
      ),
    ),
  );
}
