import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/providers.dart';

/// NotificationResponseDto.
class AppNotification {
  const AppNotification({
    required this.id,
    required this.title,
    required this.message,
    required this.type,
    required this.isRead,
    required this.createdAt,
  });

  factory AppNotification.fromJson(Map<String, dynamic> json) => AppNotification(
        id: str(json['notificationId']),
        title: str(json['title']),
        message: str(json['message']),
        type: str(json['notificationType']),
        isRead: boolOf(json['isRead']),
        createdAt: parseDate(json['createdAt']),
      );

  final String id;
  final String title;
  final String message;
  final String type;
  final bool isRead;
  final DateTime? createdAt;

  AppNotification copyWith({bool? isRead}) =>
      AppNotification(id: id, title: title, message: message, type: type, isRead: isRead ?? this.isRead, createdAt: createdAt);
}

/// The caller's own notifications (same endpoints as the web app).
class NotificationsRepository {
  NotificationsRepository(this._api);

  final ApiClient _api;

  Future<List<AppNotification>> my() async =>
      unwrapList(await _api.get('/notifications/my')).map(AppNotification.fromJson).toList();

  /// Polled in the background: never counts as user activity for the idle timeout.
  Future<int> unreadCount({bool background = false}) async {
    final body = unwrapMap(await _api.get('/notifications/unread-count', options: apiOptions(background: background)));
    return intOf(body['count']);
  }

  Future<void> markRead(String id) => _api.patch('/notifications/$id/read');

  Future<void> markAllRead() => _api.patch('/notifications/read-all');

  /// Dismisses (hides) one of the caller's notifications.
  Future<void> dismiss(String id) => _api.delete('/notifications/$id');
}

final notificationsRepositoryProvider =
    Provider<NotificationsRepository>((ref) => NotificationsRepository(ref.watch(apiClientProvider)));

final notificationsProvider = FutureProvider.autoDispose<List<AppNotification>>(
  (ref) => ref.watch(notificationsRepositoryProvider).my(),
);

final unreadCountProvider = NotifierProvider<UnreadCountController, int>(UnreadCountController.new);

/// Unread count, refreshed every 30 s in the background while signed in.
class UnreadCountController extends Notifier<int> {
  Timer? _timer;

  @override
  int build() {
    final signedIn = ref.watch(authControllerProvider.select((s) => s.isSignedIn && s.user?.isSuspended != true));
    _timer?.cancel();
    ref.onDispose(() => _timer?.cancel());
    if (!signedIn) return 0;
    Future.microtask(refresh);
    _timer = Timer.periodic(AppConstants.unreadPollInterval, (_) => refresh());
    return 0;
  }

  Future<void> refresh() async {
    try {
      final count = await ref.read(notificationsRepositoryProvider).unreadCount(background: true);
      if (ref.mounted) state = count;
    } catch (_) {
      // Keep the last count; a 401 is handled by the API client
    }
  }
}
