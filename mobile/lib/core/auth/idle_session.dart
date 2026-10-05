import 'dart:async';
import 'dart:math';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/api_client.dart';
import '../config/constants.dart';
import '../providers.dart';
import '../routing/app_router.dart';
import '../routing/routes.dart';
import '../theme/app_theme.dart';
import '../utils/format.dart';
import 'auth_controller.dart';

/// Idle timeout for every signed-in role (same behaviour as the web app's IdleSessionManager):
/// after Session:IdleTimeoutMinutes without activity the user is signed out; Session:WarningMinutes before that a
/// warning counts down with "Stay signed in" and "Sign out". Touches report activity (at most every 30 s).
/// On the waiting-approval and governance screens the session is kept alive quietly instead.
/// The API enforces the same timeout on every request.
class IdleSessionGuard extends ConsumerStatefulWidget {
  const IdleSessionGuard({super.key, required this.child});

  final Widget child;

  @override
  ConsumerState<IdleSessionGuard> createState() => _IdleSessionGuardState();
}

class _IdleSessionGuardState extends ConsumerState<IdleSessionGuard> with WidgetsBindingObserver {
  Timer? _tick;
  Timer? _keepAlive;
  Duration? _remaining; // shown while the warning is open
  bool _heartbeatInFlight = false;
  bool _signingOut = false;
  bool _staying = false;
  int _lastTouchMs = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _tick = Timer.periodic(const Duration(seconds: 1), (_) => _onTick());
    ref.read(routerProvider).routerDelegate.addListener(_onRouteChanged);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    ref.read(routerProvider).routerDelegate.removeListener(_onRouteChanged);
    _tick?.cancel();
    _keepAlive?.cancel();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Back from the background: sign out at once if the idle time has passed meanwhile
    if (state == AppLifecycleState.resumed) _onTick();
  }

  String get _path {
    try {
      return ref.read(routerProvider).routerDelegate.currentConfiguration.uri.path;
    } catch (_) {
      return '';
    }
  }

  bool get _paused => AppRoutes.isWaitingPath(_path);

  void _onRouteChanged() {
    if (_paused) {
      _startKeepAlive();
    } else {
      _keepAlive?.cancel();
      _keepAlive = null;
    }
    _onTick();
  }

  void _startKeepAlive() {
    if (_keepAlive != null) return;
    final user = ref.read(authControllerProvider).user;
    if (user == null) return;
    final idleMs = (user.sessionIdleTimeoutMinutes * 60000).round();
    _heartbeat();
    _keepAlive = Timer.periodic(Duration(milliseconds: min(120000, idleMs ~/ 3)), (_) => _heartbeat());
  }

  Future<void> _heartbeat() async {
    if (_heartbeatInFlight) return;
    _heartbeatInFlight = true;
    try {
      await ref.read(authRepositoryProvider).recordActivity();
    } catch (_) {
      // A 401 (session already ended) is handled by the API client
    } finally {
      _heartbeatInFlight = false;
    }
  }

  void _onTick() {
    if (!mounted || _signingOut) return;
    final auth = ref.read(authControllerProvider);
    final user = auth.user;
    if (!auth.isSignedIn || user == null || _paused) {
      if (_remaining != null) setState(() => _remaining = null);
      return;
    }
    final activity = ref.read(sessionActivityProvider);
    final last = activity.lastConfirmedMs;
    if (last == 0) {
      _heartbeat(); // not known yet on this phone: ask the API to confirm now
      return;
    }
    final idleMs = (user.sessionIdleTimeoutMinutes * 60000).round();
    final warnMs = min((user.sessionWarningMinutes * 60000).round(), idleMs);
    final idle = activity.nowMs - last;
    if (idle >= idleMs) {
      _signOutForInactivity();
    } else if (idle >= idleMs - warnMs) {
      setState(() => _remaining = Duration(milliseconds: idleMs - idle));
    } else if (_remaining != null) {
      setState(() => _remaining = null);
    }
  }

  void _onTouch() {
    // While the warning is open only its buttons count
    if (_remaining != null || _paused || !ref.read(authControllerProvider).isSignedIn) return;
    final activity = ref.read(sessionActivityProvider);
    final now = activity.nowMs;
    if (now - _lastTouchMs < 1000) return;
    _lastTouchMs = now;
    if (now - activity.lastConfirmedMs >= AppConstants.heartbeatInterval.inMilliseconds) _heartbeat();
  }

  Future<void> _signOutForInactivity() async {
    if (_signingOut) return;
    _signingOut = true;
    setState(() => _remaining = null);
    await ref.read(authControllerProvider.notifier).logout(reason: SignOutReason.idle);
    _signingOut = false;
  }

  Future<void> _stay() async {
    setState(() => _staying = true);
    try {
      await ref.read(authRepositoryProvider).recordActivity();
    } catch (_) {
      // A 401 is handled by the API client
    } finally {
      if (mounted) setState(() => _staying = false);
      _onTick();
    }
  }

  @override
  Widget build(BuildContext context) {
    // Signed out elsewhere (401): close the warning
    ref.listen(authControllerProvider.select((s) => s.isSignedIn), (_, signedIn) {
      if (!signedIn) {
        _keepAlive?.cancel();
        _keepAlive = null;
        if (_remaining != null) setState(() => _remaining = null);
      } else if (_paused) {
        _startKeepAlive();
      }
    });

    return Listener(
      behavior: HitTestBehavior.translucent,
      onPointerDown: (_) => _onTouch(),
      child: Stack(
        children: [
          widget.child,
          if (_remaining != null) _warning(context, _remaining!),
        ],
      ),
    );
  }

  Widget _warning(BuildContext context, Duration remaining) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    return Positioned.fill(
      child: Material(
        color: Colors.black54,
        child: Center(
          child: Container(
            margin: const EdgeInsets.all(24),
            constraints: const BoxConstraints(maxWidth: 380),
            padding: const EdgeInsets.all(20),
            decoration: BoxDecoration(
              color: dark ? AppColors.slate900 : Colors.white,
              borderRadius: BorderRadius.circular(20),
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Row(
                  children: [
                    Icon(Icons.schedule, color: AppColors.amber600),
                    SizedBox(width: 8),
                    Text('Are you still there?', style: TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
                  ],
                ),
                const SizedBox(height: 10),
                Text('For your security you will be signed out in ${Fmt.countdown(remaining)} because of inactivity.'),
                const SizedBox(height: 18),
                FilledButton(
                  key: const Key('stay-signed-in'),
                  onPressed: _staying ? null : _stay,
                  child: Text(_staying ? 'Please wait...' : 'Stay signed in'),
                ),
                const SizedBox(height: 8),
                OutlinedButton(
                  onPressed: () => ref.read(authControllerProvider.notifier).logout(),
                  child: const Text('Sign out'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
