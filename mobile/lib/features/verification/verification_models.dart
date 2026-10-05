import '../../core/api/json.dart';
import '../../core/widgets/badges.dart';

/// BloodRequestResponseDto as the hospital's verify screen and the doctor's screens show it.
class ClinicalRequest {
  const ClinicalRequest({
    required this.id,
    required this.bloodGroup,
    required this.unitsRequired,
    required this.fulfilledUnits,
    required this.reservedUnits,
    required this.priority,
    required this.reason,
    required this.status,
    required this.createdAt,
    required this.expiryDate,
    required this.hospitalName,
    required this.createdByName,
    required this.assignedDoctorId,
    required this.assignedDoctorName,
    required this.rejectionReason,
    required this.isSuspended,
    required this.suspensionReason,
    required this.pendingHospitalDonations,
  });

  factory ClinicalRequest.fromJson(Map<String, dynamic> j) => ClinicalRequest(
        id: str(j['bloodRequestId']),
        bloodGroup: str(j['bloodGroup']),
        unitsRequired: intOf(j['unitsRequired']),
        fulfilledUnits: intOf(j['fulfilledUnits']),
        reservedUnits: intOf(j['reservedUnits']),
        priority: str(j['priority']),
        reason: str(j['reason']),
        status: str(j['status']),
        createdAt: parseDate(j['createdAt']),
        expiryDate: parseDate(j['expiryDate']),
        hospitalName: j['hospitalName']?.toString(),
        createdByName: j['createdByName']?.toString(),
        assignedDoctorId: j['assignedDoctorId']?.toString(),
        assignedDoctorName: j['assignedDoctorName']?.toString(),
        rejectionReason: j['rejectionReason']?.toString(),
        isSuspended: boolOf(j['isSuspended']),
        suspensionReason: j['suspensionReason']?.toString(),
        pendingHospitalDonations: intOf(j['pendingHospitalDonations']),
      );

  final String id;
  final String bloodGroup;
  final int unitsRequired;
  final int fulfilledUnits;
  final int reservedUnits;
  final String priority;
  final String reason;
  final String status; // Pending | Verified | Approved | Rejected | Completed | Cancelled | Deleted
  final DateTime? createdAt;
  final DateTime? expiryDate;
  final String? hospitalName;
  final String? createdByName;
  final String? assignedDoctorId;

  /// "Removed doctor" when the assigned doctor was removed (the API sends that text).
  final String? assignedDoctorName;
  final String? rejectionReason;
  final bool isSuspended;
  final String? suspensionReason;
  final int pendingHospitalDonations;

  String get shortId => id.length >= 8 ? id.substring(0, 8) : id;
  bool get isCritical => priority.toUpperCase() == 'CRITICAL';
  int get freeSlots => (unitsRequired - fulfilledUnits - reservedUnits).clamp(0, unitsRequired);

  /// Hospital staff verify (assign a doctor) or reject only Pending requests; admin-suspended ones are frozen.
  bool get canVerify => status == 'Pending' && !isSuspended;

  /// The assigned doctor approves or rejects Verified requests (not while suspended).
  bool get canDoctorDecide => status == 'Verified' && !isSuspended;
}

BadgeVariant requestStatusVariant(String status) => switch (status) {
      'Pending' => BadgeVariant.warning,
      'Verified' => BadgeVariant.info,
      'Approved' => BadgeVariant.primary,
      'Completed' => BadgeVariant.success,
      'Rejected' => BadgeVariant.danger,
      _ => BadgeVariant.neutral,
    };

const requestStatusFilters = <(String?, String)>[
  ('Pending', 'Pending'),
  ('Verified', 'Verified'),
  ('Approved', 'Approved'),
  ('Completed', 'Completed'),
  ('Rejected', 'Rejected'),
  ('Cancelled', 'Cancelled'),
  (null, 'All'),
];

/// Search across the fields both web tables search.
bool requestMatches(ClinicalRequest r, {String? status, String query = ''}) {
  if (status != null && r.status != status) return false;
  final q = query.trim().toLowerCase();
  if (q.isEmpty) return true;
  return [r.shortId, r.bloodGroup, r.priority, r.reason, r.createdByName ?? '', r.hospitalName ?? '', r.assignedDoctorName ?? '']
      .any((v) => v.toLowerCase().contains(q));
}

/// One donated packet of a hospital donation.
class DonatedPacket {
  const DonatedPacket({required this.trackingNumber, required this.bloodGroup, required this.collectionDate, required this.expiryDate});

  factory DonatedPacket.fromJson(Map<String, dynamic> j) => DonatedPacket(
        trackingNumber: str(j['trackingNumber']),
        bloodGroup: str(j['bloodGroup']),
        collectionDate: parseDate(j['collectionDate']),
        expiryDate: parseDate(j['expiryDate']),
      );

  final String trackingNumber;
  final String bloodGroup;
  final DateTime? collectionDate;
  final DateTime? expiryDate;
}

/// RequestAcceptanceDetailDto: a donor (or a donating hospital) with contact details, for the request's
/// hospital doctors and staff only.
class RequestAcceptance {
  const RequestAcceptance({
    required this.id,
    required this.bloodRequestId,
    required this.donorName,
    required this.donorEmail,
    required this.donorPhone,
    required this.status,
    required this.acceptedAt,
    required this.rejectionReason,
    required this.donorHospitalId,
    required this.packets,
  });

  factory RequestAcceptance.fromJson(Map<String, dynamic> j) => RequestAcceptance(
        id: str(j['acceptanceId']),
        bloodRequestId: str(j['bloodRequestId']),
        donorName: str(j['donorName']),
        donorEmail: str(j['donorEmail']),
        donorPhone: str(j['donorPhoneNumber']),
        status: str(j['status']),
        acceptedAt: parseDate(j['acceptedAt']),
        rejectionReason: j['rejectionReason']?.toString(),
        donorHospitalId: j['donorHospitalId']?.toString(),
        packets: (j['packets'] is List ? j['packets'] as List : const [])
            .whereType<Map>()
            .map((e) => DonatedPacket.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
      );

  final String id;
  final String bloodRequestId;
  final String donorName;
  final String donorEmail;
  final String donorPhone;
  final String status; // Accepted | ScreeningPending | ScreeningCompleted | Verified | Matched | Rejected | Cancelled
  final DateTime? acceptedAt;
  final String? rejectionReason;
  final String? donorHospitalId;
  final List<DonatedPacket> packets;

  bool get isHospitalDonation => donorHospitalId != null && donorHospitalId!.isNotEmpty;

  /// A hospital's packets waiting for the assigned doctor's decision.
  bool get awaitsHospitalDecision => isHospitalDonation && status == 'Accepted';

  /// An approved donor with a reserved slot: the donation can be recorded or the slot released.
  bool get isReserved => !isHospitalDonation && status == 'Verified';

  String get statusLabel => switch (status) {
        'Accepted' => isHospitalDonation ? 'Awaiting doctor approval' : 'Accepted',
        'ScreeningPending' => 'Screening in progress',
        'ScreeningCompleted' => 'Screening report waiting',
        'Verified' => 'Approved · slot reserved',
        'Matched' => 'Donated',
        'Rejected' => 'Not approved',
        'Cancelled' => 'Withdrawn / released',
        _ => status,
      };
}

BadgeVariant acceptanceVariant(String status) => switch (status) {
      'Verified' => BadgeVariant.info,
      'Matched' => BadgeVariant.success,
      'Rejected' => BadgeVariant.danger,
      'Cancelled' => BadgeVariant.neutral,
      _ => BadgeVariant.warning,
    };

/// Reason / message rules shared by the web dialogs: required (trimmed), at most 500 characters.
String? requiredReasonError(String text, {String message = 'A reason is required.'}) {
  final t = text.trim();
  if (t.isEmpty) return message;
  if (t.length > 500) return 'Please keep it under 500 characters.';
  return null;
}
