import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/routing/app_router.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/floating_overlay_padding.dart';
import 'assistant_sheet.dart';

class AssistantLauncher extends ConsumerStatefulWidget {
  const AssistantLauncher({super.key, required this.child});

  final Widget child;

  @override
  ConsumerState<AssistantLauncher> createState() => _AssistantLauncherState();
}

class _AssistantLauncherState extends ConsumerState<AssistantLauncher> {
  OverlayEntry? _entry;
  bool _visible = false;
  bool _sheetOpen = false;
  double _bottom = 16;
  double? _bottomNavigationHeight;

  @override
  void dispose() {
    _entry?.remove();
    _entry?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final router = ref.watch(routerProvider);
    final auth = ref.watch(authControllerProvider);
    return ValueListenableBuilder<RouteInformation>(
      valueListenable: router.routeInformationProvider,
      builder: (context, route, _) {
        final path = route.uri.path;
        final keyboardOpen = MediaQuery.viewInsetsOf(context).bottom > 0;
        final visible = _isVisible(auth, path) && !keyboardOpen;
        final stacked = AppRoutes.hasPageFloatingAction(path);
        final scrollClearance = visible ? (stacked ? 64.0 : 56.0) : 0.0;
        _configure(
          visible: visible,
          bottom: _bottomFor(context, path, stacked),
        );

        return NotificationListener<BottomNavigationBarHeightNotification>(
          onNotification: (notification) {
            if (_bottomNavigationHeight != notification.height) {
              setState(() => _bottomNavigationHeight = notification.height);
            }
            return false;
          },
          child: FloatingOverlayPadding(
            bottom: scrollClearance,
            child: widget.child,
          ),
        );
      },
    );
  }

  void _configure({required bool visible, required double bottom}) {
    if (_visible == visible && _bottom == bottom) return;
    _visible = visible;
    _bottom = bottom;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      _ensureEntry();
      _entry?.markNeedsBuild();
    });
  }

  void _ensureEntry() {
    if (_entry != null) return;
    final overlay = rootNavigatorKey.currentState?.overlay;
    if (overlay == null) return;
    _entry = OverlayEntry(
      builder: (overlayContext) {
        if (!_visible || _sheetOpen) return const SizedBox.shrink();
        return Positioned(
          key: const Key('assistant-launcher-position'),
          right: 24,
          bottom: _bottom,
          child: FloatingActionButton.small(
            key: const Key('assistant-launcher'),
            heroTag: 'lifelink-assistant-launcher',
            tooltip: 'Open LifeLink Assistant',
            backgroundColor: AppColors.red600,
            foregroundColor: Colors.white,
            onPressed: () => _open(overlayContext),
            child: const Icon(Icons.chat_bubble_outline),
          ),
        );
      },
    );
    overlay.insert(_entry!);
  }

  Future<void> _open(BuildContext overlayContext) async {
    _sheetOpen = true;
    _entry?.markNeedsBuild();
    await showAssistantSheet(overlayContext);
    if (!mounted) return;
    _sheetOpen = false;
    _entry?.markNeedsBuild();
  }

  bool _isVisible(AuthState auth, String path) {
    final user = auth.user;
    if (!auth.isSignedIn || user == null || user.isSuspended) return false;
    if (user.isUnapprovedHospitalStaff ||
        (user.hasRole(Roles.doctor) && user.mustChangePassword)) {
      return false;
    }
    return path != AppRoutes.splash &&
        path != AppRoutes.changePassword &&
        path != AppRoutes.waitingApproval &&
        !AppRoutes.publicPaths.contains(path);
  }

  double _bottomFor(BuildContext context, String path, bool stacked) {
    final safeBottom = MediaQuery.paddingOf(context).bottom;
    final shellBottom = AppRoutes.hasBottomNavigation(path)
        ? _bottomNavigationHeight ??
              safeBottom + (NavigationBarTheme.of(context).height ?? 80)
        : safeBottom;
    const pageInset = 16.0;
    const primaryFabHeightAndGap = 68.0;
    return shellBottom + pageInset + (stacked ? primaryFabHeightAndGap : 0);
  }
}
