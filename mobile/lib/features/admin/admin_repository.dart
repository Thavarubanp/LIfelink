import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';

/// AdminDashboardStatsDto: the nine dashboard numbers.
class AdminStats {
  const AdminStats({
    required this.totalDonorPatients,
    required this.totalHospitals,
    required this.totalDoctors,
    required this.activeRequests,
    required this.pendingComplaints,
    required this.pendingHospitalApprovals,
    required this.pendingAppeals,
    required this.activeSuspendedUsers,
    required this.activeSuspendedHospitals,
  });

  factory AdminStats.fromJson(Map<String, dynamic> j) => AdminStats(
        totalDonorPatients: intOf(j['totalDonorPatients']),
        totalHospitals: intOf(j['totalHospitals']),
        totalDoctors: intOf(j['totalDoctors']),
        activeRequests: intOf(j['activeRequests']),
        pendingComplaints: intOf(j['pendingComplaints']),
        pendingHospitalApprovals: intOf(j['pendingHospitalApprovals']),
        pendingAppeals: intOf(j['pendingAppeals']),
        activeSuspendedUsers: intOf(j['activeSuspendedUsers']),
        activeSuspendedHospitals: intOf(j['activeSuspendedHospitals']),
      );

  final int totalDonorPatients;
  final int totalHospitals;
  final int totalDoctors;
  final int activeRequests;
  final int pendingComplaints;
  final int pendingHospitalApprovals;
  final int pendingAppeals;
  final int activeSuspendedUsers;
  final int activeSuspendedHospitals;
}

/// AdminAttentionDto: badge counts and when the admin last opened each Activity log tab.
class AdminAttention {
  const AdminAttention({
    this.newBloodRequests = 0,
    this.newTransfers = 0,
    this.bloodRequestsSeenAt,
    this.transfersSeenAt,
    this.pendingRegistrations = 0,
    this.pendingAppeals = 0,
    this.pendingComplaints = 0,
  });

  factory AdminAttention.fromJson(Map<String, dynamic> j) => AdminAttention(
        newBloodRequests: intOf(j['newBloodRequests']),
        newTransfers: intOf(j['newTransfers']),
        bloodRequestsSeenAt: parseDate(j['bloodRequestsSeenAt']),
        transfersSeenAt: parseDate(j['transfersSeenAt']),
        pendingRegistrations: intOf(j['pendingRegistrations']),
        pendingAppeals: intOf(j['pendingAppeals']),
        pendingComplaints: intOf(j['pendingComplaints']),
      );

  final int newBloodRequests;
  final int newTransfers;
  final DateTime? bloodRequestsSeenAt;
  final DateTime? transfersSeenAt;
  final int pendingRegistrations;
  final int pendingAppeals;
  final int pendingComplaints;

  /// Everything that waits for the admin (the Attention tab badge).
  int get total => newBloodRequests + newTransfers + pendingRegistrations + pendingAppeals + pendingComplaints;

  int get newActivity => newBloodRequests + newTransfers;
}

/// Short badge text: 0 hides the badge, more than 99 shows "99+" (same as the web sidebar).
String? badgeText(int n) => n <= 0 ? null : n > 99 ? '99+' : '$n';

/// /api/Admin endpoints for the dashboard and badges.
class AdminRepository {
  AdminRepository(this._api);

  final ApiClient _api;

  Future<AdminStats> dashboard() async => AdminStats.fromJson(unwrapMap(await _api.get('/Admin/dashboard')));

  /// Polled in the background: never counts as user activity for the idle timeout.
  Future<AdminAttention> attention({bool background = false}) async =>
      AdminAttention.fromJson(unwrapMap(await _api.get('/Admin/attention-counts', options: apiOptions(background: background))));
}

final adminRepositoryProvider = Provider<AdminRepository>((ref) => AdminRepository(ref.watch(apiClientProvider)));

final adminStatsProvider = FutureProvider.autoDispose<AdminStats>((ref) => ref.watch(adminRepositoryProvider).dashboard());
