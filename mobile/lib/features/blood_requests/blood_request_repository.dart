import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_error.dart';
import '../../core/api/json.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/providers.dart';
import 'blood_request_models.dart';

class DonorHomeData {
  const DonorHomeData({required this.requests, required this.acceptances, required this.profile});

  final List<BloodRequest> requests;
  final List<DonorAcceptance> acceptances;
  final DonorProfileSummary profile;

  List<BloodRequest> get activeRequests => requests.where((r) => r.isActive).toList();
  List<DonorAcceptance> get activeAcceptances => acceptances.where((a) => a.isActive).toList();
  int get completedDonations => acceptances.where((a) => a.status == 'Matched').length;
}

/// Donor/patient request API. Later request parts extend this same repository.
class BloodRequestRepository {
  BloodRequestRepository(this._api);

  final ApiClient _api;

  Future<List<BloodRequest>> myRequests() async =>
      unwrapList(await _api.get('/BloodRequests/my')).map(BloodRequest.fromJson).toList();

  Future<List<DonorAcceptance>> myAcceptances() async =>
      unwrapList(await _api.get('/Acceptances/my')).map(DonorAcceptance.fromJson).toList();

  Future<DonorProfileSummary> donorProfile(String userId) async =>
      DonorProfileSummary.fromJson(unwrapMap(await _api.get('/profiles/user/$userId')));

  Future<DonorHomeData> home(String userId) async {
    final requests = myRequests();
    final acceptances = myAcceptances();
    final profile = donorProfile(userId);
    final result = DonorHomeData(
      requests: await requests,
      acceptances: await acceptances,
      profile: await profile,
    );
    result.requests.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
    result.acceptances.sort((a, b) => (b.acceptedAt ?? DateTime(0)).compareTo(a.acceptedAt ?? DateTime(0)));
    return result;
  }
}

final bloodRequestRepositoryProvider =
    Provider<BloodRequestRepository>((ref) => BloodRequestRepository(ref.watch(apiClientProvider)));

final donorHomeProvider = FutureProvider.autoDispose<DonorHomeData>((ref) {
  final user = ref.watch(authControllerProvider).user;
  if (user == null) throw const ApiError('Your account could not be loaded. Please sign in again.');
  return ref.watch(bloodRequestRepositoryProvider).home(user.userId);
});
