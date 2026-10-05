import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';

/// GET /profiles/me: which profile the caller has ("doctor", "hospital" or "user") and its id.
class MyProfileRef {
  const MyProfileRef(this.type, this.id);
  final String type;
  final String id;
}

/// HospitalProfileDto (the fields the app shows).
class HospitalProfile {
  const HospitalProfile({
    required this.hospitalId,
    required this.name,
    required this.address,
    required this.city,
    required this.contactNumber,
    required this.email,
    required this.isVerified,
    required this.doctorCount,
    required this.packetShelfLifeDays,
    required this.expiryAlertDays,
  });

  factory HospitalProfile.fromJson(Map<String, dynamic> j) => HospitalProfile(
        hospitalId: str(j['hospitalId']),
        name: str(j['name']),
        address: str(j['address']),
        city: j['city']?.toString(),
        contactNumber: str(j['contactNumber']),
        email: str(j['email']),
        isVerified: boolOf(j['isVerified']),
        doctorCount: intOf(j['doctorCount']),
        packetShelfLifeDays: intOf(j['packetShelfLifeDays']),
        expiryAlertDays: intOf(j['expiryAlertDays']),
      );

  final String hospitalId;
  final String name;
  final String address;
  final String? city;
  final String contactNumber;
  final String email;
  final bool isVerified;
  final int doctorCount;
  final int packetShelfLifeDays;
  final int expiryAlertDays;
}

class ProfileRepository {
  ProfileRepository(this._api);

  final ApiClient _api;

  Future<MyProfileRef> me() async {
    final j = unwrapMap(await _api.get('/profiles/me'));
    return MyProfileRef(str(j['type']), str(j['id']));
  }

  Future<HospitalProfile> hospital(String id) async =>
      HospitalProfile.fromJson(unwrapMap(await _api.get('/profiles/hospital/$id')));
}

final profileRepositoryProvider = Provider<ProfileRepository>((ref) => ProfileRepository(ref.watch(apiClientProvider)));

/// The signed-in hospital staff member's own hospital (id, shelf life, expiry window).
final myHospitalProvider = FutureProvider.autoDispose<HospitalProfile>((ref) async {
  final repo = ref.watch(profileRepositoryProvider);
  final me = await repo.me();
  return repo.hospital(me.id);
});
