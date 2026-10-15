import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/notifications/notification_rules.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import 'notifications_repository.dart';

/// The caller's notifications: unread first highlighted, visible item actions,
/// swipe to dismiss, and read all.
class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({
    super.key,
    this.title = 'Notifications',
    this.types,
    this.emptyMessage,
  });

  final String title;

  /// Only these notification types (e.g. the inventory analysis recommendations); null = all.
  final Set<String>? types;
  final String? emptyMessage;

  @override
  ConsumerState<NotificationsScreen> createState() =>
      _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen> {
  final Set<String> _readLocally = {};
  final Set<String> _dismissed = {};
  bool _readingAll = false;

  Future<void> _refresh() async {
    _readLocally.clear();
    _dismissed.clear();
    ref.invalidate(notificationsProvider);
    await ref
        .read(notificationsProvider.future)
        .catchError((_) => <AppNotification>[]);
    await ref.read(unreadCountProvider.notifier).refresh();
  }

  Future<void> _open(AppNotification n) async {
    await _markRead(n);
    if (!mounted) return;
    final route = routeForNotificationType(
      n.type,
      user: ref.read(authControllerProvider).user,
    );
    final navigate = route != AppRoutes.notifications;
    final openRoute = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  n.title,
                  style: const TextStyle(
                    fontSize: 17,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 4),
                Text(
                  '${Fmt.humanize(n.type)} · ${Fmt.sriLankaDateTime(n.createdAt)}',
                  style: const TextStyle(
                    fontSize: 12,
                    color: AppColors.slate500,
                  ),
                ),
                const SizedBox(height: 12),
                Text(n.message),
                if (navigate) ...[
                  const SizedBox(height: 16),
                  SizedBox(
                    width: double.infinity,
                    child: FilledButton.icon(
                      onPressed: () => Navigator.pop(ctx, true),
                      icon: const Icon(Icons.open_in_new),
                      label: const Text('Open related area'),
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
    if (openRoute == true && mounted) context.push(route);
  }

  Future<void> _markRead(AppNotification n) async {
    if (n.isRead || _readLocally.contains(n.id)) return;
    setState(() => _readLocally.add(n.id));
    ref.read(unreadCountProvider.notifier).decrement();
    try {
      await ref.read(notificationsRepositoryProvider).markRead(n.id);
      _invalidateUnreadCountKeepingLocalValue();
    } catch (e) {
      if (mounted) {
        setState(() => _readLocally.remove(n.id));
        ref.read(unreadCountProvider.notifier).increment();
        _invalidateUnreadCountKeepingLocalValue();
        showErrorSnack(context, e);
      }
    }
  }

  void _invalidateUnreadCountKeepingLocalValue() {
    final localCount = ref.read(unreadCountProvider);
    ref.invalidate(unreadCountProvider);
    ref.read(unreadCountProvider.notifier).setLocalCount(localCount);
  }

  Future<void> _dismiss(AppNotification n) async {
    final wasUnread = !n.isRead && !_readLocally.contains(n.id);
    setState(() => _dismissed.add(n.id));
    if (wasUnread) ref.read(unreadCountProvider.notifier).decrement();
    try {
      await ref.read(notificationsRepositoryProvider).dismiss(n.id);
      _invalidateUnreadCountKeepingLocalValue();
    } catch (e) {
      if (mounted) {
        setState(() => _dismissed.remove(n.id));
        if (wasUnread) ref.read(unreadCountProvider.notifier).increment();
        _invalidateUnreadCountKeepingLocalValue();
        showErrorSnack(context, e, title: 'Not dismissed');
      }
    }
  }

  Future<void> _readAll() async {
    if (_readingAll) return;
    setState(() => _readingAll = true);
    try {
      await ref.read(notificationsRepositoryProvider).markAllRead();
      await _refresh();
      if (mounted) {
        showSnack(
          context,
          'All notifications marked as read.',
          type: SnackType.success,
        );
      }
    } catch (e) {
      if (mounted) showErrorSnack(context, e);
    } finally {
      if (mounted) setState(() => _readingAll = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(notificationsProvider);
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.title),
        actions: [
          if (widget.types == null)
            IconButton(
              tooltip: 'Mark all as read',
              icon: _readingAll
                  ? const SizedBox(
                      width: 20,
                      height: 20,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.done_all),
              onPressed: _readingAll ? null : _readAll,
            ),
        ],
      ),
      body: RefreshableScroll(
        onRefresh: _refresh,
        child: AsyncView(
          value: value,
          onRetry: _refresh,
          data: (all) {
            final list = all
                .where((n) => !_dismissed.contains(n.id))
                .where(
                  (n) => widget.types == null || widget.types!.contains(n.type),
                )
                .toList();
            if (list.isEmpty) {
              return EmptyView(
                icon: Icons.notifications_none_rounded,
                message: widget.emptyMessage ?? 'You have no notifications.',
              );
            }
            return ContentWidth(
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: Column(
                  children: [
                    for (final n in list)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 8),
                        child: Dismissible(
                          key: ValueKey(n.id),
                          direction: DismissDirection.endToStart,
                          background: Container(
                            alignment: Alignment.centerRight,
                            padding: const EdgeInsets.only(right: 20),
                            decoration: BoxDecoration(
                              color: AppColors.rose600,
                              borderRadius: BorderRadius.circular(16),
                            ),
                            child: const Icon(
                              Icons.delete_outline,
                              color: Colors.white,
                            ),
                          ),
                          confirmDismiss: (_) => confirmDialog(
                            context,
                            title: 'Dismiss notification',
                            message: 'Remove "${n.title}" from your list?',
                            confirmLabel: 'Dismiss',
                          ),
                          onDismissed: (_) => _dismiss(n),
                          child: _NotificationTile(
                            n: n,
                            unread: !n.isRead && !_readLocally.contains(n.id),
                            onTap: () => _open(n),
                            onMarkRead: () => _markRead(n),
                            onDismiss: () => _dismiss(n),
                          ),
                        ),
                      ),
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

class _NotificationTile extends StatelessWidget {
  const _NotificationTile({
    required this.n,
    required this.unread,
    required this.onTap,
    required this.onMarkRead,
    required this.onDismiss,
  });

  final AppNotification n;
  final bool unread;
  final VoidCallback onTap;
  final VoidCallback onMarkRead;
  final VoidCallback onDismiss;

  @override
  Widget build(BuildContext context) => Card(
    child: InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(16),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Icon(
                unread
                    ? Icons.mark_email_unread_outlined
                    : Icons.drafts_outlined,
                color: unread ? AppColors.red600 : AppColors.slate400,
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: Padding(
                          padding: const EdgeInsets.only(top: 4),
                          child: Text(
                            n.title,
                            style: TextStyle(
                              fontWeight: unread
                                  ? FontWeight.w700
                                  : FontWeight.w500,
                            ),
                          ),
                        ),
                      ),
                      IconButton(
                        key: ValueKey('dismiss-notification-${n.id}'),
                        tooltip: 'Dismiss notification',
                        visualDensity: VisualDensity.compact,
                        onPressed: onDismiss,
                        icon: const Icon(Icons.close, size: 20),
                      ),
                    ],
                  ),
                  Text(n.message, maxLines: 2, overflow: TextOverflow.ellipsis),
                  const SizedBox(height: 4),
                  Text(
                    Fmt.sriLankaDateTime(n.createdAt),
                    style: const TextStyle(
                      fontSize: 12,
                      color: AppColors.slate500,
                    ),
                  ),
                  if (unread)
                    Align(
                      alignment: Alignment.centerRight,
                      child: TextButton.icon(
                        key: ValueKey('mark-read-notification-${n.id}'),
                        onPressed: onMarkRead,
                        icon: const Icon(Icons.done_all, size: 18),
                        label: const Text('Mark as read'),
                      ),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );
}
