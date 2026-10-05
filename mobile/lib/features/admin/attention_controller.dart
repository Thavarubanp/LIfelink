import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import 'admin_repository.dart';

final adminAttentionProvider = NotifierProvider<AdminAttentionController, AdminAttention?>(AdminAttentionController.new);

/// Admin badge counts (web: useAdminAttention): polled every 30 s as a background request (never extends the session)
/// while an admin is signed in, and refreshed at once after an admin action ([refresh]).
class AdminAttentionController extends Notifier<AdminAttention?> {
  Timer? _timer;

  @override
  AdminAttention? build() {
    final isAdmin = ref.watch(authControllerProvider.select((s) => s.isSignedIn && s.user!.hasRole(Roles.admin)));
    _timer?.cancel();
    ref.onDispose(() => _timer?.cancel());
    if (!isAdmin) return null;
    Future.microtask(refresh);
    _timer = Timer.periodic(const Duration(seconds: 30), (_) => refresh());
    return null;
  }

  Future<void> refresh() async {
    try {
      final counts = await ref.read(adminRepositoryProvider).attention(background: true);
      if (ref.mounted) state = counts;
    } catch (_) {
      // Badges are optional: keep the last counts on a failed refresh
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
