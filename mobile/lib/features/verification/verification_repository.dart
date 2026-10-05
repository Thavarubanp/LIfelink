import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import 'verification_models.dart';

/// Request verification, doctor decisions and donation handling: the same endpoints as the web's bloodRequestApi and
/// acceptanceApi.
class VerificationRepository {
  VerificationRepository(this._api);

  final ApiClient _api;

  /// Hospital staff: every request sent to the hospital (all statuses).
  Future<List<ClinicalRequest>> hospitalRequests() async =>
      unwrapList(await _api.get('/BloodRequests/hospital')).map(ClinicalRequest.fromJson).toList();

  /// Doctor: requests assigned to the signed-in doctor.
  Future<List<ClinicalRequest>> assignedRequests() async =>
      unwrapList(await _api.get('/BloodRequests/assigned')).map(ClinicalRequest.fromJson).toList();

  /// Hospital staff verify a Pending request and assign one of their doctors (mandatory).
  Future<void> verify(String requestId, String doctorId) => _api.put('/requests/$requestId/verify', body: {'doctorId': doctorId});

  /// Assigned doctor approves a Verified request (optional notes).
  Future<void> approve(String requestId, String notes) => _api.put('/requests/$requestId/approve', body: {'notes': notes});

  /// Hospital staff or the assigned doctor reject with a mandatory message.
  Future<void> reject(String requestId, String message) => _api.put('/requests/$requestId/reject', body: {'notes': message});

  /// Donors (with contact details) and donating hospitals of a request.
  Future<List<RequestAcceptance>> acceptances(String requestId) async =>
      unwrapList(await _api.get('/BloodRequests/$requestId/acceptances')).map(RequestAcceptance.fromJson).toList();

  /// Record that an approved donor donated, with the blood group tested at donation.
  Future<void> recordDonation(String requestId, String acceptanceId, String testedBloodGroup) =>
      _api.put('/BloodRequests/$requestId/finalize-selection', body: {
        'selectedAcceptanceIds': [acceptanceId],
        'testedBloodGroups': {acceptanceId: testedBloodGroup},
      });

  /// Doctor or hospital staff release an acceptance that cannot proceed (reason shown to the donor).
  Future<void> release(String acceptanceId, String reason) => _api.put('/Acceptances/$acceptanceId/release', body: {'reason': reason});

  /// The request's assigned doctor decides a hospital donation (a reason is required to reject).
  Future<void> approveHospitalDonation(String acceptanceId, String notes) =>
      _api.put('/Acceptances/$acceptanceId/hospital-approve', body: {'notes': notes});

  Future<void> rejectHospitalDonation(String acceptanceId, String reason) =>
      _api.put('/Acceptances/$acceptanceId/hospital-reject', body: {'reason': reason});
}

final verificationRepositoryProvider = Provider<VerificationRepository>((ref) => VerificationRepository(ref.watch(apiClientProvider)));

List<ClinicalRequest> _newestFirst(List<ClinicalRequest> list) =>
    list..sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));

final hospitalRequestsProvider =
    FutureProvider.autoDispose<List<ClinicalRequest>>((ref) async => _newestFirst(await ref.watch(verificationRepositoryProvider).hospitalRequests()));

final assignedRequestsProvider =
    FutureProvider.autoDispose<List<ClinicalRequest>>((ref) async => _newestFirst(await ref.watch(verificationRepositoryProvider).assignedRequests()));

final requestAcceptancesProvider = FutureProvider.autoDispose.family<List<RequestAcceptance>, String>(
  (ref, requestId) => ref.watch(verificationRepositoryProvider).acceptances(requestId),
);
