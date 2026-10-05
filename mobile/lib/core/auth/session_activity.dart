import '../config/constants.dart';

/// Idle-timeout bookkeeping, the same rules as the web app's sessionActivity.js.
///
/// The API ends a session after Session:IdleTimeoutMinutes without activity, and only requests carrying the
/// `X-LifeLink-Activity: 1` header count as activity. User-driven requests carry it (at most every 15 s);
/// background refreshes never do. The app counts down from the last time the API confirmed activity.
class SessionActivity {
  SessionActivity({DateTime Function()? clock}) : _clock = clock ?? DateTime.now;

  static const activityHeader = 'X-LifeLink-Activity';
  static const sessionEndedHeader = 'x-session-ended';

  final DateTime Function() _clock;
  int _lastConfirmedMs = 0;
  int _lastMarkedMs = 0;

  int get nowMs => _clock().millisecondsSinceEpoch;

  /// When the API last confirmed activity (ms since epoch); 0 when unknown.
  int get lastConfirmedMs => _lastConfirmedMs;

  /// Records that the API accepted activity sent at [sentAtMs] (keeps the latest).
  void markConfirmed(int sentAtMs) {
    if (sentAtMs > _lastConfirmedMs) _lastConfirmedMs = sentAtMs;
  }

  /// Whether a user-driven request sent now should carry the activity header.
  bool takeMark({Duration minInterval = AppConstants.activityReportInterval}) {
    final now = nowMs;
    final last = _lastMarkedMs > _lastConfirmedMs ? _lastMarkedMs : _lastConfirmedMs;
    if (now - last < minInterval.inMilliseconds) return false;
    _lastMarkedMs = now;
    return true;
  }

  /// A marked request failed before reaching the API: let the next one report again.
  void releaseMark() => _lastMarkedMs = 0;

  void reset() {
    _lastConfirmedMs = 0;
    _lastMarkedMs = 0;
  }
}
