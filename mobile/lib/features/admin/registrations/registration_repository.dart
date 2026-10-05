import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/api/api_client.dart';
import '../../../core/api/json.dart';
import '../../../core/attachments/attachment.dart';
import '../../../core/providers.dart';

/// HospitalApprovalHistoryDto: one entry of a registration conversation.
class RegistrationEntry {
  const RegistrationEntry({
    required this.id,
    required this.type,
    required this.fromAdmin,
    required this.timestamp,
    required this.adminName,
    required this.message,
    required this.changedFields,
    required this.attachment,
  });

  factory RegistrationEntry.fromJson(Map<String, dynamic> j) => RegistrationEntry(
        id: str(j['id']),
        type: str(j['type']),
        fromAdmin: boolOf(j['fromAdmin']),
        timestamp: parseDate(j['timestamp']),
        adminName: j['adminName']?.toString(),
        message: j['message']?.toString(),
        changedFields: j['changedFields']?.toString(),
        attachment: Attachment.fromApi(j['attachmentUrl']?.toString(), j['attachmentName']?.toString()),
      );

  final String id;
  final String type; // Submitted, Rejected, AdminComment, HospitalReply, Approved
  final bool fromAdmin;
  final DateTime? timestamp;
  final String? adminName;
  final String? message;
  final String? changedFields;
  final Attachment? attachment;

  String get title => switch (type) {
        'Submitted' => 'Registration submitted',
        'Rejected' => 'Rejected by the administrator',
        'AdminComment' => 'Administrator comment',
        'HospitalReply' => 'Hospital reply',
        'Approved' => 'Approved',
        _ => type,
      };
}

/// AdminHospitalResponseDto (the fields the admin screens use).
class AdminHospital {
  const AdminHospital({
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
    required this.isSuspended,
    required this.suspensionReason,
    required this.suspendedUntil,
    required this.createdAt,
    required this.history,
  });

  factory AdminHospital.fromJson(Map<String, dynamic> j) => AdminHospital(
        hospitalId: str(j['hospitalId']),
        name: str(j['name']),
        licenseNumber: str(j['licenseNumber']),
        registrationNumber: j['registrationNumber']?.toString(),
        address: str(j['address']),
        city: j['city']?.toString(),
        contactNumber: str(j['contactNumber']),
        email: str(j['email']),
        approvalStatus: str(j['approvalStatus']),
        contactPersonName: j['contactPersonName']?.toString(),
        contactPersonPhone: j['contactPersonPhone']?.toString(),
        contactPersonEmail: j['contactPersonEmail']?.toString(),
        licenseDocument: Attachment.fromApi(j['licenseDocumentUrl']?.toString(), j['licenseDocumentName']?.toString()),
        accreditationDocument: Attachment.fromApi(j['accreditationDocumentUrl']?.toString(), j['accreditationDocumentName']?.toString()),
        isSuspended: boolOf(j['isSuspended']),
        suspensionReason: j['suspensionReason']?.toString(),
        suspendedUntil: parseDate(j['suspendedUntil']),
        createdAt: parseDate(j['createdAt']),
        history: j['approvalHistory'] is List
            ? (j['approvalHistory'] as List).whereType<Map>().map((e) => RegistrationEntry.fromJson(Map<String, dynamic>.from(e))).toList()
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
  final String approvalStatus; // Pending | AwaitingAdminReview | Rejected | Approved
  final String? contactPersonName;
  final String? contactPersonPhone;
  final String? contactPersonEmail;
  final Attachment? licenseDocument;
  final Attachment? accreditationDocument;
  final bool isSuspended;
  final String? suspensionReason;
  final DateTime? suspendedUntil;
  final DateTime? createdAt;
  final List<RegistrationEntry> history;

  bool get isPending => approvalStatus == 'Pending';
  bool get isAwaitingReview => approvalStatus == 'AwaitingAdminReview';

  /// The admin acts on Pending registrations and on hospital replies; Rejected waits for the hospital.
  bool get needsReview => isPending || isAwaitingReview;
  bool get waitingForHospital => approvalStatus == 'Rejected';

  /// Same rules as the web page: reject only while it needs review; comment on anything but a new Pending one.
  bool get canApprove => needsReview;
  bool get canReject => needsReview;
  bool get canComment => !isPending && approvalStatus != 'Approved';

  /// The newest conversation entry the admin has seen (the API refuses an action based on an older one: 409).
  String? get lastEntryId => history.isEmpty ? null : history.last.id;

  String get statusLabel => switch (approvalStatus) {
        'Pending' => 'Pending review',
        'AwaitingAdminReview' => 'Hospital replied · review',
        'Rejected' => 'Rejected · waiting for hospital',
        _ => approvalStatus,
      };
}

/// /api/Admin/hospitals registration actions (same calls as the web's HospitalManagementPage).
class RegistrationRepository {
  RegistrationRepository(this._api);

  final ApiClient _api;

  Future<List<AdminHospital>> all() async =>
      unwrapList(await _api.get('/Admin/hospitals')).map(AdminHospital.fromJson).toList();

  Future<void> approve(AdminHospital h) => _api.put('/Admin/hospitals/${h.hospitalId}/approve', body: {'lastSeenEntryId': h.lastEntryId});

  Future<void> reject(AdminHospital h, String reason, Attachment? report) => _api.put('/Admin/hospitals/${h.hospitalId}/reject', body: {
        'reason': reason,
        'reportDocumentName': report?.name,
        'reportDocumentUrl': report?.url,
        'lastSeenEntryId': h.lastEntryId,
      });

  Future<void> comment(AdminHospital h, String message, Attachment? attachment) =>
      _api.post('/Admin/hospitals/${h.hospitalId}/comments', body: {
        'message': message,
        'attachmentUrl': attachment?.url,
        'attachmentName': attachment?.name,
        'lastSeenEntryId': h.lastEntryId,
      });
}

final registrationRepositoryProvider =
    Provider<RegistrationRepository>((ref) => RegistrationRepository(ref.watch(apiClientProvider)));

/// Every hospital as the admin sees it (registrations, documents, conversation, suspension).
final adminHospitalsProvider = FutureProvider.autoDispose<List<AdminHospital>>((ref) async {
  final list = await ref.watch(registrationRepositoryProvider).all();
  list.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
  return list;
});
