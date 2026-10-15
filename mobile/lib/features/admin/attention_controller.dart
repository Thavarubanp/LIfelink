import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import 'admin_repository.dart';

final adminAttentionProvider =
    NotifierProvider<AdminAttentionController, AdminAttention?>(
      AdminAttentionController.new,
    );

/// Admin badge counts. The shared tab-attention controller owns the role's
/// single polling timer; admin actions can still refresh this state directly.
class AdminAttentionController extends Notifier<AdminAttention?> {
  bool _refreshing = false;
  bool _refreshPending = false;

  @override
  AdminAttention? build() {
    ref.watch(authControllerProvider.select((auth) => auth.user?.userId));
    return null;
  }

  Future<void> refresh() async {
    if (_refreshing) {
      _refreshPending = true;
      return;
    }
    _refreshing = true;
    try {
      final counts = await ref
          .read(adminRepositoryProvider)
          .attention(background: true);
      if (ref.mounted) state = counts;
    } catch (_) {
      // Badges are optional: keep the last counts on a failed refresh
    } finally {
      _refreshing = false;
      if (_refreshPending && ref.mounted) {
        _refreshPending = false;
        Future.microtask(refresh);
      }
    }
  }
}

/// One badge number from the attention counts (0 until loaded).
final attentionCountProvider = Provider.family<int, String>((ref, key) {
  final a = ref.watch(adminAttentionProvider);
  if (a == null) return 0;
  return switch (key) {
    'total' => a.total,
    'activity' => a.newActivity,
    'registrations' => a.pendingRegistrations,
    'appeals' => a.pendingAppeals,
    'complaints' => a.pendingComplaints,
    _ => 0,
  };
});
