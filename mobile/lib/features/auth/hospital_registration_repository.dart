import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/attachments/attachment.dart';
import '../../core/providers.dart';

class HospitalRegistrationEntry {
  const HospitalRegistrationEntry({
    required this.id,
    required this.type,
    required this.fromAdmin,
    required this.timestamp,
    required this.adminName,
    required this.message,
    required this.changedFields,
    required this.attachment,
  });

  factory HospitalRegistrationEntry.fromJson(Map<String, dynamic> json) =>
      HospitalRegistrationEntry(
        id: str(json['id']),
        type: str(json['type']),
        fromAdmin: boolOf(json['fromAdmin']),
        timestamp: parseDate(json['timestamp']),
        adminName: json['adminName']?.toString(),
        message: json['message']?.toString(),
        changedFields: json['changedFields']?.toString(),
        attachment: Attachment.fromApi(
          json['attachmentUrl']?.toString(),
          json['attachmentName']?.toString(),
        ),
      );

  final String id;
  final String type;
  final bool fromAdmin;
  final DateTime? timestamp;
  final String? adminName;
  final String? message;
  final String? changedFields;
  final Attachment? attachment;

  String get title {
    if (type == 'HospitalReply' && changedFields?.trim().isNotEmpty == true) {
      return 'Hospital correction';
    }
    return switch (type) {
      'Submitted' => 'Registration submitted',
      'Rejected' => 'Registration rejected',
      'AdminComment' => 'Admin comment',
      'HospitalReply' => 'Hospital reply',
      'Approved' => 'Registration approved',
      _ => type,
    };
  }
}

class HospitalRegistration {
  const HospitalRegistration({
    required this.hospitalId,
    required this.name,
    required this.licenseNumber,
    required this.registrationNumber,
    required this.address,
    required this.city,
    required this.contactNumber,
    required this.email,
    required this.approvalStatus,
    required this.contactPersonName,
    required this.contactPersonPhone,
    required this.contactPersonEmail,
    required this.licenseDocument,
    required this.accreditationDocument,
    required this.rejectionReason,
    required this.rejectionReport,
    required this.createdAt,
    required this.updatedAt,
    required this.history,
  });

  factory HospitalRegistration.fromJson(Map<String, dynamic> json) =>
      HospitalRegistration(
        hospitalId: str(json['hospitalId']),
        name: str(json['name']),
        licenseNumber: str(json['licenseNumber']),
        registrationNumber: json['registrationNumber']?.toString(),
        address: str(json['address']),
        city: json['city']?.toString(),
        contactNumber: str(json['contactNumber']),
        email: str(json['email']),
        approvalStatus: str(json['approvalStatus'], 'Pending'),
        contactPersonName: json['contactPersonName']?.toString(),
        contactPersonPhone: json['contactPersonPhone']?.toString(),
        contactPersonEmail: json['contactPersonEmail']?.toString(),
        licenseDocument: Attachment.fromApi(
          json['licenseDocumentUrl']?.toString(),
          json['licenseDocumentName']?.toString(),
        ),
        accreditationDocument: Attachment.fromApi(
          json['accreditationDocumentUrl']?.toString(),
          json['accreditationDocumentName']?.toString(),
        ),
        rejectionReason: json['rejectionReason']?.toString(),
        rejectionReport: Attachment.fromApi(
          json['rejectionReportUrl']?.toString(),
          json['rejectionReportName']?.toString(),
        ),
        createdAt: parseDate(json['createdAt']),
        updatedAt: parseDate(json['updatedAt']),
        history: json['approvalHistory'] is List
            ? (json['approvalHistory'] as List)
                  .whereType<Map>()
                  .map(
                    (entry) => HospitalRegistrationEntry.fromJson(
                      Map<String, dynamic>.from(entry),
                    ),
                  )
                  .toList()
            : const [],
      );

  final String hospitalId;
  final String name;
  final String licenseNumber;
  final String? registrationNumber;
  final String address;
  final String? city;
  final String contactNumber;
  final String email;
  final String approvalStatus;
  final String? contactPersonName;
  final String? contactPersonPhone;
  final String? contactPersonEmail;
  final Attachment? licenseDocument;
  final Attachment? accreditationDocument;
  final String? rejectionReason;
  final Attachment? rejectionReport;
  final DateTime? createdAt;
  final DateTime? updatedAt;
  final List<HospitalRegistrationEntry> history;

  bool get isApproved => approvalStatus == 'Approved';
  bool get isRejected => approvalStatus == 'Rejected';
  bool get isAwaitingAdminReview => approvalStatus == 'AwaitingAdminReview';
}

class HospitalRegistrationDraft {
  const HospitalRegistrationDraft({
    required this.name,
    required this.registrationNumber,
    required this.email,
    required this.password,
    required this.contactNumber,
    required this.address,
    required this.city,
    required this.contactPersonName,
    required this.contactPersonPhone,
    required this.licenseDocument,
    required this.accreditationDocument,
  });

  final String name;
  final String registrationNumber;
  final String email;
  final String password;
  final String contactNumber;
  final String address;
  final String city;
  final String contactPersonName;
  final String contactPersonPhone;
  final Attachment? licenseDocument;
  final Attachment? accreditationDocument;

  Map<String, dynamic> toJson() => {
    'name': name.trim(),
    'licenseNumber': registrationNumber.trim(),
    'registrationNumber': registrationNumber.trim(),
    'email': email.trim(),
    'password': password,
    'contactNumber': contactNumber.trim(),
    'address': address.trim(),
    if (city.trim().isNotEmpty) 'city': city.trim(),
    'contactPersonName': contactPersonName.trim(),
    'contactPersonPhone': contactPersonPhone.trim(),
    if (licenseDocument != null) ...{
      'licenseDocumentUrl': licenseDocument!.url,
      'licenseDocumentName': licenseDocument!.name,
    },
    if (accreditationDocument != null) ...{
      'accreditationDocumentUrl': accreditationDocument!.url,
      'accreditationDocumentName': accreditationDocument!.name,
    },
  };
}

class HospitalRegistrationReply {
  const HospitalRegistrationReply({
    required this.message,
    this.attachment,
    this.corrections = const {},
    this.licenseDocument,
    this.accreditationDocument,
  });

  final String message;
  final Attachment? attachment;
  final Map<String, String> corrections;
  final Attachment? licenseDocument;
  final Attachment? accreditationDocument;

  Map<String, dynamic> toJson() => {
    'message': message.trim(),
    if (attachment != null) ...{
      'attachmentUrl': attachment!.url,
      'attachmentName': attachment!.name,
    },
    for (final entry in corrections.entries)
      if (entry.value.trim().isNotEmpty) entry.key: entry.value.trim(),
    if (licenseDocument != null) ...{
      'licenseDocumentUrl': licenseDocument!.url,
      'licenseDocumentName': licenseDocument!.name,
    },
    if (accreditationDocument != null) ...{
      'accreditationDocumentUrl': accreditationDocument!.url,
      'accreditationDocumentName': accreditationDocument!.name,
    },
  };
}

class HospitalRegistrationRepository {
  HospitalRegistrationRepository(this._api);

  final ApiClient _api;

  Future<HospitalRegistration> create(HospitalRegistrationDraft draft) async =>
      HospitalRegistration.fromJson(
        unwrapMap(await _api.post('/Hospitals', body: draft.toJson())),
      );

  Future<HospitalRegistration> mine() async =>
      HospitalRegistration.fromJson(unwrapMap(await _api.get('/Hospitals/me')));

  Future<HospitalRegistration> reply(
    String hospitalId,
    HospitalRegistrationReply reply,
  ) async => HospitalRegistration.fromJson(
    unwrapMap(
      await _api.post('/Hospitals/$hospitalId/replies', body: reply.toJson()),
    ),
  );
}

final hospitalRegistrationRepositoryProvider =
    Provider<HospitalRegistrationRepository>(
      (ref) => HospitalRegistrationRepository(ref.watch(apiClientProvider)),
    );
