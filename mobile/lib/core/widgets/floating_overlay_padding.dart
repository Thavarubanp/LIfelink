import 'package:flutter/widgets.dart';

/// Reports the rendered height of the phone shell's bottom navigation region.
///
/// The height includes the bar's own safe-area padding, so overlay controls can
/// clear the real layout instead of duplicating NavigationBar's sizing rules.
class BottomNavigationBarHeightNotification extends Notification {
  const BottomNavigationBarHeightNotification(this.height);

  final double height;
}

class BottomNavigationBarSizeReporter extends StatefulWidget {
  const BottomNavigationBarSizeReporter({super.key, required this.child});

  final Widget child;

  @override
  State<BottomNavigationBarSizeReporter> createState() =>
      _BottomNavigationBarSizeReporterState();
}

class _BottomNavigationBarSizeReporterState
    extends State<BottomNavigationBarSizeReporter> {
  double? _lastHeight;
  bool _reportScheduled = false;

  void _scheduleReport() {
    if (_reportScheduled) return;
    _reportScheduled = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _reportScheduled = false;
      if (!mounted) return;
      final height = context.size?.height;
      if (height == null || height == _lastHeight) return;
      _lastHeight = height;
      BottomNavigationBarHeightNotification(height).dispatch(context);
    });
  }

  @override
  Widget build(BuildContext context) {
    _scheduleReport();
    return NotificationListener<SizeChangedLayoutNotification>(
      onNotification: (_) {
        _scheduleReport();
        return false;
      },
      child: SizeChangedLayoutNotifier(child: widget.child),
    );
  }
}

/// Extra scroll clearance reserved for a floating control drawn above a page.
class FloatingOverlayPadding extends InheritedWidget {
  const FloatingOverlayPadding({
    super.key,
    required this.bottom,
    required super.child,
  });

  final double bottom;

  static double bottomOf(BuildContext context) =>
      context
          .dependOnInheritedWidgetOfExactType<FloatingOverlayPadding>()
          ?.bottom ??
      0;

  @override
  bool updateShouldNotify(FloatingOverlayPadding oldWidget) =>
      bottom != oldWidget.bottom;
}
