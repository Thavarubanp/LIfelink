import '../routing/routes.dart';

/// A notification as the poller sees it (from GET /notifications/my).
class PolledNotification {
  const PolledNotification({required this.id, required this.title, required this.message, required this.type, required this.createdAt});

  final String id;
  final String title;
  final String message;
  final String type;
  final DateTime? createdAt;
}

/// What the poller remembers between runs (stored on the phone, shared by the app and the background task).
class PollMarker {
  const PollMarker({this.lastSeenId, this.lastSeenAt});

  final String? lastSeenId;
  final DateTime? lastSeenAt;

  bool get isEmpty => lastSeenId == null && lastSeenAt == null;
}

/// The result of one poll: what to show and the new marker.
class PollResult {
  const PollResult(this.toShow, this.marker);
  final List<PolledNotification> toShow;
  final PollMarker marker;
}

/// Picks the notifications not shown yet. The first run only records where we are (no flood of old items);
/// afterwards everything newer than the last seen one is shown once, oldest first, at most [max].
PollResult newNotifications(List<PolledNotification> all, PollMarker marker, {int max = 5}) {
  final sorted = all.where((n) => n.createdAt != null).toList()..sort((a, b) => a.createdAt!.compareTo(b.createdAt!));
  if (sorted.isEmpty) return PollResult(const [], marker);
  final newest = sorted.last;
  final next = PollMarker(lastSeenId: newest.id, lastSeenAt: newest.createdAt);
  if (marker.isEmpty) return PollResult(const [], next);
  final fresh = sorted
      .where((n) => n.id != marker.lastSeenId && (marker.lastSeenAt == null || n.createdAt!.isAfter(marker.lastSeenAt!)))
      .toList();
  final shown = fresh.length > max ? fresh.sublist(fresh.length - max) : fresh;
  return PollResult(shown, next);
}

/// The admin's attention counts the poller compares between runs.
class AttentionSnapshot {
  const AttentionSnapshot({this.registrations = 0, this.appeals = 0, this.complaints = 0, this.newActivity = 0});

  factory AttentionSnapshot.fromList(List<int> v) =>
      AttentionSnapshot(registrations: v[0], appeals: v[1], complaints: v[2], newActivity: v[3]);

  final int registrations;
  final int appeals;
  final int complaints;
  final int newActivity;

  List<int> toList() => [registrations, appeals, complaints, newActivity];
}

/// One summary line for whatever went up since the last poll (null when nothing did).
String? attentionIncrease(AttentionSnapshot? before, AttentionSnapshot now) {
  if (before == null) return null;
  final parts = <String>[
    if (now.registrations > before.registrations) '${now.registrations - before.registrations} new hospital registration(s)',
    if (now.appeals > before.appeals) '${now.appeals - before.appeals} new appeal(s)',
    if (now.complaints > before.complaints) '${now.complaints - before.complaints} complaint(s) waiting for you',
    if (now.newActivity > before.newActivity) '${now.newActivity - before.newActivity} new request(s) or transfer(s)',
  ];
  return parts.isEmpty ? null : parts.join(', ');
}

/// The screen a tapped phone notification opens.
String routeForNotificationType(String type) {
  switch (type) {
    case 'InventoryShortage':
    case 'InventoryShortageHelp':
    case 'PacketsExpiringSoon':
      return AppRoutes.hospitalRecommendations;
    case 'UserSuspended':
    case 'HospitalSuspended':
    case 'UserReinstated':
    case 'HospitalReinstated':
    case 'AppealApproved':
    case 'AppealRejected':
    case 'Governance':
      return AppRoutes.governanceStatus;
    case 'AdminAttention':
      return AppRoutes.adminAttention;
    default:
      return AppRoutes.notifications;
  }
}
