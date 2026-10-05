import '../../core/api/json.dart';

class BloodRequest {
  const BloodRequest({
    required this.id,
    required this.patientUserId,
    required this.hospitalId,
    required this.hospitalName,
    required this.bloodGroup,
    required this.unitsRequired,
    required this.fulfilledUnits,
    required this.reservedUnits,
    required this.isAcceptingDonors,
    required this.reason,
    required this.priority,
    required this.status,
    required this.createdAt,
    required this.expiryDate,
    required this.isSuspended,
    required this.suspensionReason,
    required this.rejectionReason,
    required this.assignedDoctorName,
    required this.hasActiveAcceptances,
    required this.hasScreenedDonors,
  });

  factory BloodRequest.fromJson(Map<String, dynamic> j) => BloodRequest(
        id: str(j['bloodRequestId']),
        patientUserId: str(j['patientUserId']),
        hospitalId: str(j['hospitalId']),
        hospitalName: str(j['hospitalName'], 'Hospital'),
        bloodGroup: str(j['bloodGroup']),
        unitsRequired: intOf(j['unitsRequired']),
        fulfilledUnits: intOf(j['fulfilledUnits']),
        reservedUnits: intOf(j['reservedUnits']),
        isAcceptingDonors: j['isAcceptingDonors'] != false,
        reason: str(j['reason']),
        priority: str(j['priority']),
        status: str(j['status']),
        createdAt: parseDate(j['createdAt']),
        expiryDate: parseDate(j['expiryDate']),
        isSuspended: boolOf(j['isSuspended']),
        suspensionReason: j['suspensionReason']?.toString(),
        rejectionReason: j['rejectionReason']?.toString(),
        assignedDoctorName: j['assignedDoctorName']?.toString(),
        hasActiveAcceptances: boolOf(j['hasActiveAcceptances']),
        hasScreenedDonors: boolOf(j['hasScreenedDonors']),
      );

  final String id;
  final String patientUserId;
  final String hospitalId;
  final String hospitalName;
  final String bloodGroup;
  final int unitsRequired;
  final int fulfilledUnits;
  final int reservedUnits;
  final bool isAcceptingDonors;
  final String reason;
  final String priority;
  final String status;
  final DateTime? createdAt;
  final DateTime? expiryDate;
  final bool isSuspended;
  final String? suspensionReason;
  final String? rejectionReason;
  final String? assignedDoctorName;
  final bool hasActiveAcceptances;
  final bool hasScreenedDonors;

  int get remainingUnits => (unitsRequired - fulfilledUnits).clamp(0, unitsRequired);
  bool get isActive => const {'Pending', 'Verified', 'Approved'}.contains(status);
  bool get isCritical => priority.toUpperCase() == 'CRITICAL';
}

class ScreeningDecision {
  const ScreeningDecision({
    required this.id,
    required this.reportVersion,
    required this.status,
    required this.submittedAt,
    required this.decidedAt,
    required this.decidedByName,
    required this.approvalNotes,
    required this.rejectionReason,
    required this.note,
  });

  factory ScreeningDecision.fromJson(Map<String, dynamic> j) => ScreeningDecision(
        id: str(j['donorVerificationId']),
        reportVersion: intOf(j['reportVersion']),
        status: str(j['status']),
        submittedAt: parseDate(j['submittedAt']),
        decidedAt: parseDate(j['decidedAt']),
        decidedByName: j['decidedByName']?.toString(),
        approvalNotes: j['approvalNotes']?.toString(),
        rejectionReason: j['rejectionReason']?.toString(),
        note: j['note']?.toString(),
      );

  final String id;
  final int reportVersion;
  final String status;
  final DateTime? submittedAt;
  final DateTime? decidedAt;
  final String? decidedByName;
  final String? approvalNotes;
  final String? rejectionReason;
  final String? note;
}

class DonorAcceptance {
  const DonorAcceptance({
    required this.id,
    required this.bloodRequestId,
    required this.status,
    required this.acceptedAt,
    required this.rejectionReason,
    required this.hospitalName,
    required this.requestBloodGroup,
    required this.requestPriority,
    required this.requestStatus,
    required this.unitsRequired,
    required this.fulfilledUnits,
    required this.reservedUnits,
    required this.requestDeleted,
    required this.requestSuspended,
    required this.screeningHistory,
  });

  factory DonorAcceptance.fromJson(Map<String, dynamic> j) => DonorAcceptance(
        id: str(j['acceptanceId']),
        bloodRequestId: str(j['bloodRequestId']),
        status: str(j['status']),
        acceptedAt: parseDate(j['acceptedAt']),
        rejectionReason: j['rejectionReason']?.toString(),
        hospitalName: str(j['hospitalName'], 'Hospital'),
        requestBloodGroup: str(j['requestBloodGroup']),
        requestPriority: str(j['requestPriority']),
        requestStatus: str(j['requestStatus']),
        unitsRequired: intOf(j['unitsRequired']),
        fulfilledUnits: intOf(j['fulfilledUnits']),
        reservedUnits: intOf(j['reservedUnits']),
        requestDeleted: boolOf(j['requestDeleted']),
        requestSuspended: boolOf(j['requestSuspended']),
        screeningHistory: j['screeningHistory'] is List
            ? (j['screeningHistory'] as List)
                .whereType<Map>()
                .map((e) => ScreeningDecision.fromJson(Map<String, dynamic>.from(e)))
                .toList()
            : const [],
      );

  final String id;
  final String bloodRequestId;
  final String status;
  final DateTime? acceptedAt;
  final String? rejectionReason;
  final String hospitalName;
  final String requestBloodGroup;
  final String requestPriority;
  final String requestStatus;
  final int unitsRequired;
  final int fulfilledUnits;
  final int reservedUnits;
  final bool requestDeleted;
  final bool requestSuspended;
  final List<ScreeningDecision> screeningHistory;

  bool get isActive => const {'Accepted', 'ScreeningPending', 'ScreeningCompleted', 'Verified'}.contains(status);
}

class DonorProfileSummary {
  const DonorProfileSummary({
    required this.bloodGroup,
    required this.bloodGroupConfirmed,
    required this.lastDonationDate,
    required this.nextEligibleDonationDate,
  });

  factory DonorProfileSummary.fromJson(Map<String, dynamic> j) => DonorProfileSummary(
        bloodGroup: j['bloodGroup']?.toString(),
        bloodGroupConfirmed: boolOf(j['bloodGroupConfirmed']),
        lastDonationDate: parseDate(j['lastDonationDate']),
        nextEligibleDonationDate: parseDate(j['nextEligibleDonationDate']),
      );

  final String? bloodGroup;
  final bool bloodGroupConfirmed;
  final DateTime? lastDonationDate;
  final DateTime? nextEligibleDonationDate;

  bool eligibleAt(DateTime now) => nextEligibleDonationDate == null || !nextEligibleDonationDate!.isAfter(now);
}

class HospitalChoice {
  const HospitalChoice({required this.id, required this.name, required this.address, required this.email});
  factory HospitalChoice.fromJson(Map<String, dynamic> j) => HospitalChoice(
        id: str(j['hospitalId']),
        name: str(j['name']),
        address: str(j['address']),
        email: str(j['email']),
      );
  final String id;
  final String name;
  final String address;
  final String email;
}

class DoctorChoice {
  const DoctorChoice({required this.id, required this.name, required this.specialization});
  factory DoctorChoice.fromJson(Map<String, dynamic> j) => DoctorChoice(
        id: str(j['doctorId']),
        name: 'Dr. ${str(j['firstName'])} ${str(j['lastName'])}'.trim(),
        specialization: str(j['specialization']),
      );
  final String id;
  final String name;
  final String specialization;
}
