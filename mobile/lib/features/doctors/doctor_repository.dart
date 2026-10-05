import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import '../../core/utils/validators.dart';
import '../profile/profile_repository.dart';

/// DoctorResponseDto.
class Doctor {
  const Doctor({
    required this.doctorId,
    required this.hospitalId,
    required this.hospitalName,
    required this.userId,
    required this.firstName,
    required this.lastName,
    required this.email,
    required this.phoneNumber,
    required this.licenseNumber,
    required this.specialization,
    required this.isActive,
    required this.mustChangePassword,
    required this.deletedAt,
    required this.createdAt,
  });

  factory Doctor.fromJson(Map<String, dynamic> j) => Doctor(
        doctorId: str(j['doctorId']),
        hospitalId: str(j['hospitalId']),
        hospitalName: str(j['hospitalName']),
        userId: j['userId']?.toString(),
        firstName: str(j['firstName']),
        lastName: str(j['lastName']),
        email: str(j['email']),
        phoneNumber: str(j['phoneNumber']),
        licenseNumber: str(j['licenseNumber']),
        specialization: str(j['specialization']),
        isActive: boolOf(j['isActive']),
        mustChangePassword: boolOf(j['mustChangePassword']),
        deletedAt: parseDate(j['deletedAt']),
        createdAt: parseDate(j['createdAt']),
      );

  final String doctorId;
  final String hospitalId;
  final String hospitalName;
  final String? userId;
  final String firstName;
  final String lastName;
  final String email;
  final String phoneNumber;
  final String licenseNumber;
  final String specialization;
  final bool isActive;
  final bool mustChangePassword;
  final DateTime? deletedAt;
  final DateTime? createdAt;

  String get name => 'Dr. ${'$firstName $lastName'.trim()}';

  /// Same rule as the web picker and the API (DoctorAssignmentRules): active, has a sign-in, finished first login.
  bool get canBeAssigned => isActive && deletedAt == null && (userId?.isNotEmpty ?? false) && !mustChangePassword;
}

/// DoctorProfileDto (the doctor's own profile page).
class DoctorProfile {
  const DoctorProfile({
    required this.doctorId,
    required this.firstName,
    required this.lastName,
    required this.email,
    required this.phoneNumber,
    required this.licenseNumber,
    required this.specialization,
    required this.hospitalName,
    required this.hospitalAddress,
    required this.isActive,
    required this.canEdit,
  });

  factory DoctorProfile.fromJson(Map<String, dynamic> j) => DoctorProfile(
        doctorId: str(j['doctorId']),
        firstName: str(j['firstName']),
        lastName: str(j['lastName']),
        email: str(j['email']),
        phoneNumber: str(j['phoneNumber']),
        licenseNumber: str(j['licenseNumber']),
        specialization: str(j['specialization']),
        hospitalName: str(j['hospitalName']),
        hospitalAddress: str(j['hospitalAddress']),
        isActive: boolOf(j['isActive']),
        canEdit: boolOf(j['canEdit']),
      );

  final String doctorId;
  final String firstName;
  final String lastName;
  final String email;
  final String phoneNumber;
  final String licenseNumber;
  final String specialization;
  final String hospitalName;
  final String hospitalAddress;
  final bool isActive;
  final bool canEdit;
}

/// Doctor form values (add doctor and edit profile), validated with the API DTO rules.
class DoctorForm {
  const DoctorForm({
    required this.firstName,
    required this.lastName,
    required this.phoneNumber,
    required this.licenseNumber,
    this.specialization = '',
    this.email = '',
    this.password = '',
  });

  final String firstName;
  final String lastName;
  final String phoneNumber;
  final String licenseNumber;
  final String specialization;
  final String email;
  final String password;

  /// Field errors for a new doctor (CreateDoctorDto) or a profile edit (UpdateDoctorProfileDto, no email/password).
  /// [existingSlmc] reproduces the web's duplicate check (case-insensitive) before calling the API.
  Map<String, String> errors({bool isNew = true, Iterable<String> existingSlmc = const []}) {
    final e = <String, String>{};
    void put(String k, String? v) {
      if (v != null) e[k] = v;
    }

    put('firstName', Validators.name(firstName, 'First name'));
    put('lastName', Validators.name(lastName, 'Last name'));
    if (!RegExp(r'^\d{10}$').hasMatch(phoneNumber.trim())) e['phoneNumber'] = 'Phone number must be exactly 10 digits.';
    final slmc = licenseNumber.trim();
    if (slmc.isEmpty) {
      e['licenseNumber'] = 'SLMC number is required.';
    } else if (slmc.length > 100) {
      e['licenseNumber'] = 'SLMC number cannot exceed 100 characters.';
    } else if (existingSlmc.any((s) => s.trim().toUpperCase() == slmc.toUpperCase())) {
      e['licenseNumber'] = 'A doctor with this SLMC number already exists.';
    }
    if (specialization.trim().length > 100) e['specialization'] = 'Specialization cannot exceed 100 characters.';
    if (isNew) {
      put('email', Validators.email(email));
      put('password', Validators.strongPassword(password));
    }
    return e;
  }
}

/// /api/Doctors (hospital staff) and /api/profiles/doctor (the doctor's own profile).
class DoctorRepository {
  DoctorRepository(this._api);

  final ApiClient _api;

  Future<List<Doctor>> hospitalDoctors() async => unwrapList(await _api.get('/Doctors')).map(Doctor.fromJson).toList();

  /// Creates a doctor account with a temporary password; the doctor must change it at first sign-in.
  Future<void> create(String hospitalId, DoctorForm f) => _api.post('/Doctors', body: {
        'hospitalId': hospitalId,
        'firstName': f.firstName.trim(),
        'lastName': f.lastName.trim(),
        'email': f.email.trim().toLowerCase(),
        'password': f.password,
        'licenseNumber': f.licenseNumber.trim(),
        'specialization': f.specialization.trim(),
        'phoneNumber': f.phoneNumber.trim(),
      });

  /// Soft delete: the doctor can no longer sign in; history shows "Removed doctor".
  Future<void> remove(String doctorId) => _api.delete('/Doctors/$doctorId');

  Future<DoctorProfile> profile(String doctorId) async => DoctorProfile.fromJson(unwrapMap(await _api.get('/profiles/doctor/$doctorId')));

  Future<void> updateProfile(String doctorId, DoctorForm f) => _api.put('/profiles/doctor/$doctorId', body: {
        'firstName': f.firstName.trim(),
        'lastName': f.lastName.trim(),
        'phoneNumber': f.phoneNumber.trim(),
        'specialization': f.specialization.trim(),
        'licenseNumber': f.licenseNumber.trim(),
      });
}

final doctorRepositoryProvider = Provider<DoctorRepository>((ref) => DoctorRepository(ref.watch(apiClientProvider)));

/// The hospital's doctors (removed ones are not listed by the API).
final hospitalDoctorsProvider = FutureProvider.autoDispose<List<Doctor>>((ref) async {
  final list = await ref.watch(doctorRepositoryProvider).hospitalDoctors();
  list.sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
  return list.where((d) => d.deletedAt == null).toList();
});

/// The signed-in doctor's own profile (GET /profiles/me → doctor id).
final myDoctorProfileProvider = FutureProvider.autoDispose<DoctorProfile>((ref) async {
  final me = await ref.watch(profileRepositoryProvider).me();
  return ref.watch(doctorRepositoryProvider).profile(me.id);
});
