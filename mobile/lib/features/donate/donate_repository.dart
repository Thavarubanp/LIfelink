import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import '../profile/profile_repository.dart';

/// BloodRequestResponseDto (the fields the Donate Blood screen shows).
class PublicRequest {
  const PublicRequest({
    required this.id,
    required this.hospitalId,
    required this.hospitalName,
    required this.bloodGroup,
    required this.unitsRequired,
    required this.fulfilledUnits,
    required this.reservedUnits,
    required this.isAcceptingDonors,
    required this.priority,
    required this.reason,
    required this.expiryDate,
  });

  factory PublicRequest.fromJson(Map<String, dynamic> j) => PublicRequest(
        id: str(j['bloodRequestId']),
        hospitalId: str(j['hospitalId']),
        hospitalName: str(j['hospitalName']),
        bloodGroup: str(j['bloodGroup']),
        unitsRequired: intOf(j['unitsRequired']),
        fulfilledUnits: intOf(j['fulfilledUnits']),
        reservedUnits: intOf(j['reservedUnits']),
        isAcceptingDonors: j['isAcceptingDonors'] != false,
        priority: str(j['priority']),
        reason: str(j['reason']),
        expiryDate: parseDate(j['expiryDate']),
      );

  final String id;
  final String hospitalId;
  final String hospitalName;
  final String bloodGroup;
  final int unitsRequired;
  final int fulfilledUnits;
  final int reservedUnits;
  final bool isAcceptingDonors;
  final String priority;
  final String reason;
  final DateTime? expiryDate;

  int get remainingUnits => (unitsRequired - fulfilledUnits).clamp(0, unitsRequired);

  /// Packets a hospital may still offer (not donated and not reserved).
  int get freeSlots => (unitsRequired - fulfilledUnits - reservedUnits).clamp(0, unitsRequired);

  bool get isCritical => priority.toUpperCase() == 'CRITICAL';
}

/// AcceptanceResponseDto as seen by hospital staff: a donation their hospital offered, with its packets.
class HospitalDonation {
  const HospitalDonation({
    required this.id,
    required this.bloodRequestId,
    required this.status,
    required this.acceptedAt,
    required this.rejectionReason,
    required this.hospitalName,
    required this.requestBloodGroup,
    required this.trackingNumbers,
    required this.requestDeleted,
  });

  factory HospitalDonation.fromJson(Map<String, dynamic> j) => HospitalDonation(
        id: str(j['acceptanceId']),
        bloodRequestId: str(j['bloodRequestId']),
        status: str(j['status']),
        acceptedAt: parseDate(j['acceptedAt']),
        rejectionReason: j['rejectionReason']?.toString(),
        hospitalName: str(j['hospitalName']),
        requestBloodGroup: str(j['requestBloodGroup']),
        trackingNumbers: j['packets'] is List
            ? (j['packets'] as List).whereType<Map>().map((p) => str(p['trackingNumber'])).toList()
            : const [],
        requestDeleted: boolOf(j['requestDeleted']),
      );

  final String id;
  final String bloodRequestId;
  final String status;
  final DateTime? acceptedAt;
  final String? rejectionReason;
  final String hospitalName;
  final String requestBloodGroup;
  final List<String> trackingNumbers;
  final bool requestDeleted;

  /// The web page's labels for a hospital donation.
  String get statusLabel => requestDeleted
      ? 'Closed'
      : switch (status) {
          'Accepted' => 'Awaiting doctor approval',
          'Matched' => 'Approved - donated',
          'Rejected' => 'Not approved',
          'Cancelled' => 'Withdrawn',
          _ => status,
        };
}

class DonateRepository {
  DonateRepository(this._api);

  final ApiClient _api;

  Future<List<PublicRequest>> publicRequests({String? bloodGroup}) async =>
      unwrapList(await _api.get('/BloodRequests/public', query: {'bloodGroup': bloodGroup})).map(PublicRequest.fromJson).toList();

  Future<List<HospitalDonation>> myDonations() async =>
      unwrapList(await _api.get('/Acceptances/my')).map(HospitalDonation.fromJson).toList();

  /// Donate selected Available packets to a public request (held until the assigned doctor decides).
  Future<void> donate(String bloodRequestId, List<String> packetIds) =>
      _api.post('/Acceptances/hospital', body: {'bloodRequestId': bloodRequestId, 'packetIds': packetIds});

  /// Withdraw a donation still awaiting the doctor; the packets come back.
  Future<void> withdraw(String acceptanceId) => _api.put('/Acceptances/$acceptanceId/cancel');
}

final donateRepositoryProvider = Provider<DonateRepository>((ref) => DonateRepository(ref.watch(apiClientProvider)));

/// Public requests from other hospitals (optionally one blood group).
final publicRequestsProvider = FutureProvider.autoDispose.family<List<PublicRequest>, String?>((ref, group) async {
  final me = await ref.watch(myHospitalProvider.future);
  final list = await ref.watch(donateRepositoryProvider).publicRequests(bloodGroup: group);
  return list.where((r) => r.hospitalId != me.hospitalId).toList();
});

final myDonationsProvider = FutureProvider.autoDispose<List<HospitalDonation>>(
  (ref) => ref.watch(donateRepositoryProvider).myDonations(),
);
