import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';

/// TransferRequestResponseDto.
class Transfer {
  const Transfer({
    required this.id,
    required this.senderHospitalId,
    required this.senderHospitalName,
    required this.receiverHospitalId,
    required this.receiverHospitalName,
    required this.bloodGroup,
    required this.unitsRequested,
    required this.status,
    required this.notes,
    required this.transferType,
    required this.createdByHospitalId,
    required this.rejectionReason,
    required this.packetTrackingNumbers,
    required this.requestedAt,
    required this.approvedAt,
    this.createdAt,
    required this.isSuspended,
    required this.suspensionReason,
  });

  factory Transfer.fromJson(Map<String, dynamic> j) => Transfer(
    id: str(j['transferRequestId']),
    senderHospitalId: str(j['senderHospitalId']),
    senderHospitalName: str(j['senderHospitalName']),
    receiverHospitalId: str(j['receiverHospitalId']),
    receiverHospitalName: str(j['receiverHospitalName']),
    bloodGroup: str(j['bloodGroup']),
    unitsRequested: intOf(j['unitsRequested']),
    status: str(j['status']),
    notes: str(j['notes']),
    transferType: str(j['transferType']),
    createdByHospitalId: str(j['createdByHospitalId']),
    rejectionReason: j['rejectionReason']?.toString(),
    packetTrackingNumbers: stringList(j['packetTrackingNumbers']),
    requestedAt: parseDate(j['requestedAt']),
    approvedAt: parseDate(j['approvedAt']),
    createdAt: parseDate(j['createdAt']),
    isSuspended: boolOf(j['isSuspended']),
    suspensionReason: j['suspensionReason']?.toString(),
  );

  final String id;
  final String senderHospitalId;
  final String senderHospitalName;
  final String receiverHospitalId;
  final String receiverHospitalName;
  final String bloodGroup;
  final int unitsRequested;
  final String status;
  final String notes;
  final String transferType; // Request | Offer
  final String createdByHospitalId;
  final String? rejectionReason;
  final List<String> packetTrackingNumbers;
  final DateTime? requestedAt;
  final DateTime? approvedAt;
  final DateTime? createdAt;
  final bool isSuspended;
  final String? suspensionReason;

  bool get isOffer => transferType == 'Offer';
  bool get isPending => status == 'Pending';

  /// The hospital that must answer: the receiver of an offer, the sender (asked for blood) of a request.
  String get counterpartHospitalId =>
      isOffer ? receiverHospitalId : senderHospitalId;
}

/// The three lists of the web page, for the hospital [me].
class TransferLists {
  TransferLists(List<Transfer> all, String me)
    : incoming = all
          .where((t) => t.isPending && t.counterpartHospitalId == me)
          .toList(),
      outgoing = all
          .where((t) => t.isPending && t.createdByHospitalId == me)
          .toList(),
      history = all.where((t) => !t.isPending).toList();

  final List<Transfer> incoming;
  final List<Transfer> outgoing;
  final List<Transfer> history;
}

/// HospitalSummaryDto (the fields the pickers need).
class HospitalSummary {
  const HospitalSummary({
    required this.hospitalId,
    required this.name,
    required this.city,
    required this.isVerified,
    required this.isSuspended,
  });

  factory HospitalSummary.fromJson(Map<String, dynamic> j) => HospitalSummary(
    hospitalId: str(j['hospitalId']),
    name: str(j['name']),
    city: j['city']?.toString(),
    isVerified: boolOf(j['isVerified']),
    isSuspended: boolOf(j['isSuspended']),
  );

  final String hospitalId;
  final String name;
  final String? city;
  final bool isVerified;
  final bool isSuspended;
}

class TransferCounterpart {
  const TransferCounterpart({
    required this.hospitalId,
    required this.hospitalName,
    required this.bloodGroup,
    required this.transferableUnits,
  });

  factory TransferCounterpart.fromJson(Map<String, dynamic> j) =>
      TransferCounterpart(
        hospitalId: str(j['hospitalId']),
        hospitalName: str(j['hospitalName']),
        bloodGroup: str(j['bloodGroup']),
        transferableUnits: intOf(j['transferableUnits']),
      );

  final String hospitalId, hospitalName, bloodGroup;
  final int transferableUnits;
}

/// /api/transfers (the acting hospital always comes from the signed-in account).
class TransferRepository {
  TransferRepository(this._api);

  final ApiClient _api;

  Future<List<Transfer>> all() async =>
      unwrapList(await _api.get('/transfers')).map(Transfer.fromJson).toList();

  Future<Transfer> byId(String id) async =>
      Transfer.fromJson(unwrapMap(await _api.get('/transfers/$id')));

  /// Request (units) or Offer (the chosen packets, held until the other hospital answers).
  Future<void> create({
    required String transferType,
    required String counterpartHospitalId,
    required String bloodGroup,
    required int unitsRequested,
    required String notes,
    List<String>? packetIds,
    required String idempotencyKey,
  }) => _api.post(
    '/transfers',
    body: {
      'transferType': transferType,
      'counterpartHospitalId': counterpartHospitalId,
      'bloodGroup': bloodGroup,
      'unitsRequested': unitsRequested,
      'notes': notes,
      'packetIds': packetIds,
    },
    options: apiOptions(idempotencyKey: idempotencyKey),
  );

  /// The counterpart accepts; a sender accepting a request passes exactly the requested number of packets.
  Future<void> approve(String id, [List<String> packetIds = const []]) =>
      _api.put('/transfers/$id/approve', body: {'packetIds': packetIds});

  Future<void> reject(String id, String reason) =>
      _api.put('/transfers/$id/reject', body: {'reason': reason});

  /// The creator withdraws a pending transfer (kept in history as Cancelled).
  Future<void> cancel(String id) => _api.delete('/transfers/$id');

  /// Approved hospitals (suspended ones are filtered out by the caller).
  Future<List<HospitalSummary>> verifiedHospitals() async =>
      unwrapList(await _api.get('/Hospitals', query: {'isVerified': true}))
          .map(HospitalSummary.fromJson)
          .toList();

  /// Safe planning projection: no counterpart inventory rows or packet details.
  Future<List<TransferCounterpart>> counterparts(String bloodGroup) async =>
      unwrapList(
        await _api.get(
          '/transfers/counterparts',
          query: {'bloodGroup': bloodGroup},
        ),
      ).map(TransferCounterpart.fromJson).toList();
}

final transferRepositoryProvider = Provider<TransferRepository>(
  (ref) => TransferRepository(ref.watch(apiClientProvider)),
);

final transfersProvider = FutureProvider.autoDispose<List<Transfer>>((
  ref,
) async {
  final list = await ref.watch(transferRepositoryProvider).all();
  list.sort(
    (a, b) =>
        (b.requestedAt ?? DateTime(0)).compareTo(a.requestedAt ?? DateTime(0)),
  );
  return list;
});
