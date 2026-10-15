import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../auth/auth_controller.dart';
import '../config/constants.dart';
import '../providers.dart';
import '../routing/app_router.dart';
import '../../features/admin/attention_controller.dart';
import 'notification_rules.dart';
import 'phone_notifications.dart';

/// Connects phone notifications to the session: after sign-in it explains and asks for platform permission once,
/// starts Android-only background polling, and polls every 30 s while the app is open; sign-out stops everything.
/// Tapping a notification opens its screen.
class PhoneNotificationsGuard extends ConsumerStatefulWidget {
  const PhoneNotificationsGuard({
    super.key,
    required this.child,
    this.notifier,
  });

  final Widget child;

  /// Overridden in tests.
  final PhoneNotifications? notifier;

  @override
  ConsumerState<PhoneNotificationsGuard> createState() =>
      _PhoneNotificationsGuardState();
}

class _PhoneNotificationsGuardState
    extends ConsumerState<PhoneNotificationsGuard>
    with WidgetsBindingObserver {
  Timer? _timer;
  bool _active = false;
  bool _polling = false;
  bool _pollPending = false;
  bool _askingPermission = false;
  bool _resumed = true;

  PhoneNotifications get _n => widget.notifier ?? PhoneNotifications.instance;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _resumed =
        WidgetsBinding.instance.lifecycleState == null ||
        WidgetsBinding.instance.lifecycleState == AppLifecycleState.resumed;
    _n.onOpen = _open;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _sync(ref.read(authControllerProvider));
      final launch = _n.launchRoute;
      if (launch != null) {
        _n.launchRoute = null;
        // Opened from a notification while closed: go there once signed in
        Future.delayed(const Duration(seconds: 2), () => _open(launch));
      }
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _timer?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState lifecycle) {
    _resumed = lifecycle == AppLifecycleState.resumed;
    if (_resumed) {
      _startForegroundPolling();
    } else {
      _timer?.cancel();
      _timer = null;
      _pollPending = false;
    }
  }

  void _startForegroundPolling() {
    _timer?.cancel();
    _timer = null;
    if (!_active || !_resumed) return;
    _poll();
    _timer = Timer.periodic(const Duration(seconds: 30), (_) => _poll());
  }

  void _open(String route) {
    if (!ref.read(authControllerProvider).isSignedIn) return;
    ref.read(routerProvider).push(route);
  }

  Future<void> _sync(AuthState auth) async {
    final signedIn = auth.isSignedIn && auth.user?.isSuspended != true;
    if (signedIn && !_active) {
      _active = true;
      await _askOnce();
      await _n.start(isAdmin: auth.user!.hasRole(Roles.admin));
      _startForegroundPolling();
    } else if (!signedIn && _active) {
      _active = false;
      _timer?.cancel();
      _timer = null;
      await _n.stop();
    }
  }

  Future<void> _poll() async {
    if (_polling) {
      _pollPending = true;
      return;
    }
    final token = ref.read(tokenStorageProvider).token;
    if (token == null) return;
    _polling = true;
    try {
      final admin = ref.read(adminAttentionProvider);
      await _n.poll(
        token: token,
        adminAttention: admin == null
            ? null
            : AttentionSnapshot(
                registrations: admin.pendingRegistrations,
                appeals: admin.pendingAppeals,
                complaints: admin.pendingComplaints,
                newActivity: admin.newActivity,
              ),
        fetchAdminAttention: false,
      );
    } catch (_) {
      // Phone notifications are optional; the in-app list still works
    } finally {
      _polling = false;
      if (_pollPending && mounted && _active && _resumed) {
        _pollPending = false;
        Future.microtask(_poll);
      }
    }
  }

  /// Explains why before the OS shows its permission prompt (asked once; later from the profile screen).
  Future<void> _askOnce() async {
    if (_askingPermission) return;
    _askingPermission = true;
    try {
      final prefs = await SharedPreferences.getInstance();
      if (prefs.getBool(PhoneNotifications.askedKey) == true) return;
      if (await _n.areEnabled()) {
        await prefs.setBool(PhoneNotifications.askedKey, true);
        return;
      }
      final context = rootNavigatorKey.currentContext;
      if (context == null || !context.mounted) return;
      final allow = await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          icon: const Icon(Icons.notifications_active_outlined),
          title: const Text('Phone notifications'),
          content: Text(
            defaultTargetPlatform == TargetPlatform.iOS
                ? 'LifeLink can notify you about messages from the administrator, alerts and decisions while the app is open. Background polling remains Android-only because iOS cannot guarantee the same 15-minute schedule.'
                : 'LifeLink can notify you about messages from the administrator, alerts and decisions, even when the app is closed. It checks about every 15 minutes and never keeps your session signed in.',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(false),
              child: const Text('Not now'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(ctx).pop(true),
              child: const Text('Allow'),
            ),
          ],
        ),
      );
      if (allow == true) {
        await _n.requestPermission();
      } else {
        await prefs.setBool(PhoneNotifications.askedKey, true);
      }
    } finally {
      _askingPermission = false;
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(authControllerProvider, (_, next) => _sync(next));
    return widget.child;
  }
}
