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

class UserProfile {
  const UserProfile({
    required this.userId,
    required this.firstName,
    required this.lastName,
    required this.email,
    required this.phoneNumber,
    required this.gender,
    required this.address,
    required this.isEmailPublic,
    required this.isPhonePublic,
    required this.isAddressPublic,
    required this.roles,
    required this.displayStatus,
    required this.bloodGroup,
    required this.bloodGroupConfirmed,
    required this.lastDonationDate,
    required this.nextEligibleDonationDate,
    required this.createdAt,
    required this.canEdit,
  });

  factory UserProfile.fromJson(Map<String, dynamic> j) => UserProfile(
    userId: str(j['userId']),
    firstName: str(j['firstName']),
    lastName: str(j['lastName']),
    email: j['email']?.toString(),
    phoneNumber: j['phoneNumber']?.toString(),
    gender: str(j['gender']),
    address: j['address']?.toString(),
    isEmailPublic: boolOf(j['isEmailPublic']),
    isPhonePublic: boolOf(j['isPhonePublic']),
    isAddressPublic: boolOf(j['isAddressPublic']),
    roles: stringList(j['roles']),
    displayStatus: str(j['displayStatus']),
    bloodGroup: j['bloodGroup']?.toString(),
    bloodGroupConfirmed: boolOf(j['bloodGroupConfirmed']),
    lastDonationDate: parseDate(j['lastDonationDate']),
    nextEligibleDonationDate: parseDate(j['nextEligibleDonationDate']),
    createdAt: parseDate(j['createdAt']),
    canEdit: boolOf(j['canEdit']),
  );

  final String userId, firstName, lastName, gender, displayStatus;
  final String? email, phoneNumber, address, bloodGroup;
  final bool isEmailPublic,
      isPhonePublic,
      isAddressPublic,
      bloodGroupConfirmed,
      canEdit;
  final List<String> roles;
  final DateTime? lastDonationDate, nextEligibleDonationDate, createdAt;
  String get fullName => '$firstName $lastName'.trim();
}

class ProfileRepository {
  ProfileRepository(this._api);

  final ApiClient _api;

  Future<MyProfileRef> me() async {
    final j = unwrapMap(await _api.get('/profiles/me'));
    return MyProfileRef(str(j['type']), str(j['id']));
  }

  Future<HospitalProfile> hospital(String id) async => HospitalProfile.fromJson(
    unwrapMap(await _api.get('/profiles/hospital/$id')),
  );

  Future<UserProfile> user(String id) async =>
      UserProfile.fromJson(unwrapMap(await _api.get('/profiles/user/$id')));

  Future<UserProfile> updateUser(
    UserProfile profile, {
    String? firstName,
    String? lastName,
    String? phoneNumber,
    String? gender,
    String? address,
    String? bloodGroup,
    DateTime? lastDonationDate,
    bool? isEmailPublic,
    bool? isPhonePublic,
    bool? isAddressPublic,
  }) async => UserProfile.fromJson(
    unwrapMap(
      await _api.put(
        '/profiles/user/${profile.userId}',
        body: {
          'firstName': firstName ?? profile.firstName,
          'lastName': lastName ?? profile.lastName,
          'phoneNumber': phoneNumber ?? profile.phoneNumber ?? '',
          'gender': gender ?? profile.gender,
          'address': address ?? profile.address ?? '',
          'bloodGroup': bloodGroup ?? profile.bloodGroup,
          'lastDonationDate': (lastDonationDate ?? profile.lastDonationDate)
              ?.toIso8601String(),
          'isEmailPublic': isEmailPublic ?? profile.isEmailPublic,
          'isPhonePublic': isPhonePublic ?? profile.isPhonePublic,
          'isAddressPublic': isAddressPublic ?? profile.isAddressPublic,
        },
      ),
    ),
  );
}

final profileRepositoryProvider = Provider<ProfileRepository>(
  (ref) => ProfileRepository(ref.watch(apiClientProvider)),
);

/// The signed-in hospital staff member's own hospital (id, shelf life, expiry window).
final myHospitalProvider = FutureProvider.autoDispose<HospitalProfile>((
  ref,
) async {
  final repo = ref.watch(profileRepositoryProvider);
  final me = await repo.me();
  return repo.hospital(me.id);
});

final userProfileProvider = FutureProvider.autoDispose
    .family<UserProfile, String>(
      (ref, id) => ref.watch(profileRepositoryProvider).user(id),
    );

final hospitalProfileProvider = FutureProvider.autoDispose
    .family<HospitalProfile, String>(
      (ref, id) => ref.watch(profileRepositoryProvider).hospital(id),
    );
