import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_error.dart';
import '../../core/api/json.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/providers.dart';
import 'blood_request_models.dart';

class DonorHomeData {
  const DonorHomeData({
    required this.requests,
    required this.acceptances,
    required this.profile,
  });

  final List<BloodRequest> requests;
  final List<DonorAcceptance> acceptances;
  final DonorProfileSummary profile;

  List<BloodRequest> get activeRequests =>
      requests.where((r) => r.isActive).toList();
  List<DonorAcceptance> get activeAcceptances =>
      acceptances.where((a) => a.isActive).toList();
  int get completedDonations =>
      acceptances.where((a) => a.status == 'Matched').length;
}

/// Donor/patient request API. Later request parts extend this same repository.
class BloodRequestRepository {
  BloodRequestRepository(this._api);

  final ApiClient _api;

  Future<List<BloodRequest>> myRequests() async =>
      unwrapList(await _api.get('/BloodRequests/my'))
          .map(BloodRequest.fromJson)
          .toList();

  Future<List<DonorAcceptance>> myAcceptances() async =>
      unwrapList(await _api.get('/Acceptances/my'))
          .map(DonorAcceptance.fromJson)
          .toList();

  Future<List<BloodRequest>> publicRequests({String? bloodGroup}) async =>
      unwrapList(
        await _api.get(
          '/BloodRequests/public',
          query: {'bloodGroup': bloodGroup},
        ),
      ).map(BloodRequest.fromJson).toList();

  Future<BloodRequest> byId(String id) async =>
      BloodRequest.fromJson(unwrapMap(await _api.get('/BloodRequests/$id')));

  Future<DonorAcceptance> accept(String requestId, String bloodGroup) async =>
      DonorAcceptance.fromJson(
        unwrapMap(
          await _api.post(
            '/Acceptances',
            body: {'bloodRequestId': requestId, 'donorBloodGroup': bloodGroup},
          ),
        ),
      );

  Future<List<HospitalChoice>> hospitals() async =>
      unwrapList(await _api.get('/Hospitals', query: {'isVerified': true}))
          .where((j) => j['isVerified'] == true && j['isSuspended'] != true)
          .map(HospitalChoice.fromJson)
          .toList();

  Future<List<DoctorChoice>> doctors() async =>
      unwrapList(await _api.get('/Doctors'))
          .where(
            (j) =>
                j['isActive'] == true &&
                j['userId'] != null &&
                j['mustChangePassword'] != true &&
                j['deletedAt'] == null,
          )
          .map(DoctorChoice.fromJson)
          .toList();

  Future<void> create({
    required String hospitalId,
    required String bloodGroup,
    required int units,
    required String priority,
    required String reason,
    String? doctorId,
    required String idempotencyKey,
  }) => _api.post(
    '/BloodRequests',
    body: {
      'hospitalId': hospitalId,
      'bloodGroup': bloodGroup,
      'unitsRequired': units,
      'priority': priority,
      'reason': reason,
      'doctorId': doctorId,
    },
    options: apiOptions(idempotencyKey: idempotencyKey),
  );

  Future<void> update(
    String id, {
    required String bloodGroup,
    required int units,
  }) => _api.put(
    '/BloodRequests/$id',
    body: {'bloodGroup': bloodGroup, 'unitsRequired': units},
  );

  Future<void> cancel(String id) => _api.put('/BloodRequests/$id/cancel');
  Future<void> delete(String id) => _api.delete('/BloodRequests/$id');

  Future<DonorProfileSummary> donorProfile(String userId) async =>
      DonorProfileSummary.fromJson(
        unwrapMap(await _api.get('/profiles/user/$userId')),
      );

  Future<DonorHomeData> home(String userId) async {
    final requests = myRequests();
    final acceptances = myAcceptances();
    final profile = donorProfile(userId);
    final result = DonorHomeData(
      requests: await requests,
      acceptances: await acceptances,
      profile: await profile,
    );
    result.requests.sort(
      (a, b) =>
          (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)),
    );
    result.acceptances.sort(
      (a, b) =>
          (b.acceptedAt ?? DateTime(0)).compareTo(a.acceptedAt ?? DateTime(0)),
    );
    return result;
  }
}

final bloodRequestRepositoryProvider = Provider<BloodRequestRepository>(
  (ref) => BloodRequestRepository(ref.watch(apiClientProvider)),
);

final donorHomeProvider = FutureProvider.autoDispose<DonorHomeData>((ref) {
  final user = ref.watch(authControllerProvider).user;
  if (user == null) {
    throw const ApiError(
      'Your account could not be loaded. Please sign in again.',
    );
  }
  return ref.watch(bloodRequestRepositoryProvider).home(user.userId);
});

typedef PublicRequestFilter = ({String? bloodGroup});

final publicBloodRequestsProvider = FutureProvider.autoDispose
    .family<List<BloodRequest>, PublicRequestFilter>((ref, filter) async {
      final list = await ref
          .watch(bloodRequestRepositoryProvider)
          .publicRequests(bloodGroup: filter.bloodGroup);
      return list;
    });

final bloodRequestProvider = FutureProvider.autoDispose
    .family<BloodRequest, String>(
      (ref, id) => ref.watch(bloodRequestRepositoryProvider).byId(id),
    );

final donorProfileProvider = FutureProvider.autoDispose
    .family<DonorProfileSummary, String>(
      (ref, userId) =>
          ref.watch(bloodRequestRepositoryProvider).donorProfile(userId),
    );

final myBloodRequestsProvider = FutureProvider.autoDispose<List<BloodRequest>>((
  ref,
) async {
  final list = await ref.watch(bloodRequestRepositoryProvider).myRequests();
  list.sort(
    (a, b) =>
        (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)),
  );
  return list;
});

final hospitalChoicesProvider =
    FutureProvider.autoDispose<List<HospitalChoice>>(
      (ref) => ref.watch(bloodRequestRepositoryProvider).hospitals(),
    );

final doctorChoicesProvider = FutureProvider.autoDispose<List<DoctorChoice>>(
  (ref) => ref.watch(bloodRequestRepositoryProvider).doctors(),
);
