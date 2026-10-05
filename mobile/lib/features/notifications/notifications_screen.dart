import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import 'notifications_repository.dart';

/// The caller's notifications: unread first highlighted, tap to read, swipe or menu to dismiss, read all.
class NotificationsScreen extends ConsumerStatefulWidget {
  const NotificationsScreen({super.key, this.title = 'Notifications', this.types, this.emptyMessage});

  final String title;

  /// Only these notification types (e.g. the inventory analysis recommendations); null = all.
  final Set<String>? types;
  final String? emptyMessage;

  @override
  ConsumerState<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends ConsumerState<NotificationsScreen> {
  final Set<String> _readLocally = {};
  final Set<String> _dismissed = {};

  Future<void> _refresh() async {
    _readLocally.clear();
    _dismissed.clear();
    ref.invalidate(notificationsProvider);
    await ref.read(notificationsProvider.future).catchError((_) => <AppNotification>[]);
    await ref.read(unreadCountProvider.notifier).refresh();
  }

  Future<void> _open(AppNotification n) async {
    if (!n.isRead && !_readLocally.contains(n.id)) {
      setState(() => _readLocally.add(n.id));
      try {
        await ref.read(notificationsRepositoryProvider).markRead(n.id);
        ref.read(unreadCountProvider.notifier).refresh();
      } catch (e) {
        if (mounted) {
          setState(() => _readLocally.remove(n.id));
          showErrorSnack(context, e);
        }
      }
    }
    if (!mounted) return;
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(n.title, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              const SizedBox(height: 4),
              Text('${Fmt.humanize(n.type)} · ${Fmt.sriLankaDateTime(n.createdAt)}',
                  style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
              const SizedBox(height: 12),
              Text(n.message),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _dismiss(AppNotification n) async {
    setState(() => _dismissed.add(n.id));
    try {
      await ref.read(notificationsRepositoryProvider).dismiss(n.id);
      ref.read(unreadCountProvider.notifier).refresh();
    } catch (e) {
      if (mounted) {
        setState(() => _dismissed.remove(n.id));
        showErrorSnack(context, e, title: 'Not dismissed');
      }
    }
  }

  Future<void> _readAll() async {
    try {
      await ref.read(notificationsRepositoryProvider).markAllRead();
      await _refresh();
      if (mounted) showSnack(context, 'All notifications marked as read.', type: SnackType.success);
    } catch (e) {
      if (mounted) showErrorSnack(context, e);
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
            IconButton(tooltip: 'Mark all as read', icon: const Icon(Icons.done_all), onPressed: _readAll),
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
                .where((n) => widget.types == null || widget.types!.contains(n.type))
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
                            decoration: BoxDecoration(color: AppColors.rose600, borderRadius: BorderRadius.circular(16)),
                            child: const Icon(Icons.delete_outline, color: Colors.white),
                          ),
                          confirmDismiss: (_) => confirmDialog(context,
                              title: 'Dismiss notification', message: 'Remove "${n.title}" from your list?', confirmLabel: 'Dismiss'),
                          onDismissed: (_) => _dismiss(n),
                          child: _NotificationTile(
                            n: n,
                            unread: !n.isRead && !_readLocally.contains(n.id),
                            onTap: () => _open(n),
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
  const _NotificationTile({required this.n, required this.unread, required this.onTap});

  final AppNotification n;
  final bool unread;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Card(
        child: ListTile(
          onTap: onTap,
          leading: Icon(
            unread ? Icons.mark_email_unread_outlined : Icons.drafts_outlined,
            color: unread ? AppColors.red600 : AppColors.slate400,
          ),
          title: Text(n.title, style: TextStyle(fontWeight: unread ? FontWeight.w700 : FontWeight.w500)),
          subtitle: Text(
            '${n.message}\n${Fmt.sriLankaDateTime(n.createdAt)}',
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
          ),
          isThreeLine: true,
        ),
      );
}
