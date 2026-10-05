import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/attachments/attachment.dart';
import '../../core/providers.dart';
import '../appeals/appeal_models.dart';

/// GovernanceStatusDto: the caller's suspension, appeal threads and what they may do.
class GovernanceStatus {
  const GovernanceStatus({
    required this.isSuspended,
    required this.suspensionReason,
    required this.suspendedUntil,
    required this.isPermanentlyBlocked,
    required this.suspendedEntity,
    required this.appeals,
    required this.canAppeal,
    required this.isReadOnlyViewer,
    required this.profileName,
    required this.profileEmail,
    required this.profileRole,
    required this.hospitalName,
  });

  factory GovernanceStatus.fromJson(Map<String, dynamic> j) {
    final profile = j['profile'] is Map ? Map<String, dynamic>.from(j['profile'] as Map) : <String, dynamic>{};
    return GovernanceStatus(
      isSuspended: boolOf(j['isSuspended']),
      suspensionReason: j['suspensionReason']?.toString(),
      suspendedUntil: parseDate(j['suspendedUntil']),
      isPermanentlyBlocked: boolOf(j['isPermanentlyBlocked']),
      suspendedEntity: str(j['suspendedEntity'], 'None'),
      appeals: (j['allAppeals'] is List ? j['allAppeals'] as List : const [])
          .whereType<Map>()
          .map((e) => Appeal.fromJson(Map<String, dynamic>.from(e)))
          .toList(),
      canAppeal: boolOf(j['canAppeal']),
      isReadOnlyViewer: boolOf(j['isReadOnlyViewer']),
      profileName: str(profile['name']),
      profileEmail: str(profile['email']),
      profileRole: str(profile['role']),
      hospitalName: profile['hospitalName']?.toString(),
    );
  }

  final bool isSuspended;
  final String? suspensionReason;
  final DateTime? suspendedUntil;
  final bool isPermanentlyBlocked;
  final String suspendedEntity; // User | Hospital | None
  final List<Appeal> appeals;
  final bool canAppeal;

  /// Doctors of a suspended hospital: they can read the threads but not appeal or reply.
  final bool isReadOnlyViewer;
  final String profileName;
  final String profileEmail;
  final String profileRole;
  final String? hospitalName;

  bool get isHospital => suspendedEntity == 'Hospital';

  /// Newest first (the API lists oldest first).
  List<Appeal> get newestFirst => appeals.reversed.toList();
}

/// Appeal rules from CreateAppealDto: 10–2000 characters.
String? appealReasonError(String text) {
  final t = text.trim();
  if (t.length < 10) return 'Please explain your appeal in at least 10 characters.';
  if (t.length > 2000) return 'The reason cannot exceed 2000 characters.';
  return null;
}

/// /api/governance/status and /api/Appeals (same calls as the web's governance pages).
class GovernanceRepository {
  GovernanceRepository(this._api);

  final ApiClient _api;

  Future<GovernanceStatus> status() async => GovernanceStatus.fromJson(unwrapMap(await _api.get('/governance/status')));

  Future<void> submitAppeal(String reason, Attachment? a) =>
      _api.post('/Appeals', body: {'reason': reason, 'attachmentUrl': a?.url, 'attachmentName': a?.name});

  /// The appellant's reply, allowed after an administrator message.
  Future<void> reply(String appealId, String notes, Attachment? a) =>
      _api.post('/Appeals/$appealId/reply', body: {'notes': notes, 'attachmentUrl': a?.url, 'attachmentName': a?.name});

  Future<List<Appeal>> myAppeals() async => unwrapList(await _api.get('/Appeals/my')).map(Appeal.fromJson).toList();
}

final governanceRepositoryProvider = Provider<GovernanceRepository>((ref) => GovernanceRepository(ref.watch(apiClientProvider)));

final governanceStatusProvider = FutureProvider.autoDispose<GovernanceStatus>((ref) => ref.watch(governanceRepositoryProvider).status());

final myAppealsProvider = FutureProvider.autoDispose<List<Appeal>>((ref) async {
  final list = await ref.watch(governanceRepositoryProvider).myAppeals();
  list.sort((a, b) => (b.submittedAt ?? DateTime(0)).compareTo(a.submittedAt ?? DateTime(0)));
  return list;
});
