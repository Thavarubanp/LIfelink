import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';

/// EmergencyRequestResponseDto.
class EmergencyRequest {
  const EmergencyRequest({
    required this.id,
    required this.hospitalId,
    required this.hospitalName,
    required this.bloodGroup,
    required this.unitsRequired,
    required this.priority,
    required this.status,
    required this.reason,
    required this.createdAt,
    required this.updatedAt,
  });

  factory EmergencyRequest.fromJson(Map<String, dynamic> j) => EmergencyRequest(
        id: str(j['emergencyRequestId']),
        hospitalId: str(j['hospitalId']),
        hospitalName: str(j['hospitalName']),
        bloodGroup: str(j['bloodGroup']),
        unitsRequired: intOf(j['unitsRequired']),
        priority: str(j['priority']),
        status: str(j['status']),
        reason: str(j['reason']),
        createdAt: parseDate(j['createdAt']),
        updatedAt: parseDate(j['updatedAt']),
      );

  final String id;
  final String hospitalId;
  final String hospitalName;
  final String bloodGroup;
  final int unitsRequired;
  final String priority;
  final String status;
  final String reason;
  final DateTime? createdAt;
  final DateTime? updatedAt;

  bool get isCritical => priority.toUpperCase() == 'CRITICAL';
}

/// /api/emergencyrequests (hospital-to-hospital emergencies).
class EmergencyRepository {
  EmergencyRepository(this._api);

  final ApiClient _api;

  Future<List<EmergencyRequest>> all() async =>
      unwrapList(await _api.get('/emergencyrequests')).map(EmergencyRequest.fromJson).toList();

  Future<List<EmergencyRequest>> critical() async =>
      unwrapList(await _api.get('/emergencyrequests/critical')).map(EmergencyRequest.fromJson).toList();

  /// The hospital comes from the signed-in account on the server; one idempotency key per form.
  Future<void> create({
    required String bloodGroup,
    required int unitsRequired,
    required String priority,
    required String reason,
    required String idempotencyKey,
  }) =>
      _api.post(
        '/emergencyrequests',
        body: {'bloodGroup': bloodGroup, 'unitsRequired': unitsRequired, 'priority': priority, 'reason': reason},
        options: apiOptions(idempotencyKey: idempotencyKey),
      );

  /// Only the hospital that raised the emergency may approve, reject or complete it (checked by the API).
  Future<void> approve(String id) => _api.put('/emergencyrequests/$id/approve');
  Future<void> reject(String id) => _api.put('/emergencyrequests/$id/reject');
  Future<void> complete(String id) => _api.put('/emergencyrequests/$id/complete');
}

final emergencyRepositoryProvider = Provider<EmergencyRepository>((ref) => EmergencyRepository(ref.watch(apiClientProvider)));

final criticalEmergenciesProvider = FutureProvider.autoDispose<List<EmergencyRequest>>(
  (ref) => ref.watch(emergencyRepositoryProvider).critical(),
);
