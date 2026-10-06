import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../inventory/hospital_home_screen.dart' show inventoryGroupPath;
import '../notifications/notifications_repository.dart';

/// The inventory analysis alert types (InventoryMonitor): the agent's recommendations for this hospital.
class RecommendationKind {
  const RecommendationKind(
    this.type,
    this.title,
    this.description,
    this.icon,
    this.color,
  );

  final String type;
  final String title;
  final String description;
  final IconData icon;
  final Color color;
}

const recommendationKinds = [
  RecommendationKind(
    'InventoryShortage',
    'Low stock at your hospital',
    'A blood group is below its minimum threshold. Request a transfer or add packets.',
    Icons.trending_down,
    AppColors.amber600,
  ),
  RecommendationKind(
    'InventoryShortageHelp',
    'Help requested',
    'Another hospital is low on a blood group you hold. Consider offering a transfer.',
    Icons.handshake_outlined,
    AppColors.blue600,
  ),
  RecommendationKind(
    'PacketsExpiringSoon',
    'Packets expiring soon',
    'Packets inside your expiry alert window. Remove, transfer or donate them first.',
    Icons.hourglass_bottom,
    AppColors.rose600,
  ),
];

final _bloodGroup = RegExp(r'(AB|A|B|O)[+-]');

/// The blood group a recommendation is about (from its title or message), if any.
String? recommendationBloodGroup(AppNotification n) =>
    _bloodGroup.firstMatch('${n.title} ${n.message}')?.group(0);

/// The agent's recommendations: the caller's notifications of the three inventory alert types, grouped by type.
class RecommendationsScreen extends ConsumerWidget {
  const RecommendationsScreen({super.key});

  Future<void> _open(
    BuildContext context,
    WidgetRef ref,
    AppNotification n,
    RecommendationKind kind,
  ) async {
    if (!n.isRead) {
      try {
        await ref.read(notificationsRepositoryProvider).markRead(n.id);
        ref.invalidate(notificationsProvider);
        ref.read(unreadCountProvider.notifier).refresh();
      } catch (_) {
        // Still show the details
      }
    }
    if (!context.mounted) return;
    final group = recommendationBloodGroup(n);
    await showModalBottomSheet<void>(
      context: context,
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(kind.icon, color: kind.color),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      n.title,
                      style: const TextStyle(
                        fontSize: 17,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              Text(
                '${kind.title} · ${Fmt.sriLankaDateTime(n.createdAt)}',
                style: const TextStyle(fontSize: 12, color: AppColors.slate500),
              ),
              const SizedBox(height: 12),
              Text(n.message),
              const SizedBox(height: 12),
              Text(kind.description, style: const TextStyle(fontSize: 12)),
              if (group != null) ...[
                const SizedBox(height: 12),
                FilledButton.icon(
                  onPressed: () {
                    Navigator.of(ctx).pop();
                    context.go(inventoryGroupPath(group));
                  },
                  icon: const Icon(Icons.inventory_2_outlined),
                  label: Text('Open $group in Inventory'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(notificationsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Recommendations')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(notificationsProvider);
          await ref
              .read(notificationsProvider.future)
              .catchError((_) => <AppNotification>[]);
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(notificationsProvider),
          data: (all) {
            final byKind = {
              for (final k in recommendationKinds)
                k: all.where((n) => n.type == k.type).toList(),
            };
            if (byKind.values.every((l) => l.isEmpty)) {
              return const EmptyView(
                icon: Icons.auto_awesome_outlined,
                title: 'No recommendations',
                message: 'The inventory analysis has not flagged anything for your hospital. Run it from Home.',
              );
            }
            return ContentWidth(
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text(
                      'Alerts from the inventory analysis (scheduled every 30 minutes or run by a hospital). '
                      'Repeated alerts are not sent again for 12 hours. The analysis only recommends; your staff decide.',
                      style: TextStyle(fontSize: 12, color: AppColors.slate500),
                    ),
                    const SizedBox(height: 12),
                    for (final entry in byKind.entries)
                      if (entry.value.isNotEmpty) ...[
                        SectionCard(
                          title: '${entry.key.title} (${entry.value.length})',
                          icon: entry.key.icon,
                          child: Column(
                            children: [
                              for (final n in entry.value)
                                ListTile(
                                  contentPadding: EdgeInsets.zero,
                                  onTap: () =>
                                      _open(context, ref, n, entry.key),
                                  leading: recommendationBloodGroup(n) == null
                                      ? Icon(
                                          entry.key.icon,
                                          color: entry.key.color,
                                        )
                                      : BloodGroupBadge(
                                          recommendationBloodGroup(n)!,
                                        ),
                                  title: Text(
                                    n.title,
                                    style: TextStyle(
                                      fontWeight: n.isRead
                                          ? FontWeight.w500
                                          : FontWeight.w700,
                                    ),
                                  ),
                                  subtitle: Text(
                                    '${n.message}\n${Fmt.sriLankaDateTime(n.createdAt)}',
                                    maxLines: 3,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                  isThreeLine: true,
                                ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 12),
                      ],
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}
