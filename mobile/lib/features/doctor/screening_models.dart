import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import '../../core/widgets/badges.dart';

/// One flag the screening agent raised (eligibility_rules.py): "defer" or "review", on a question (section) number.
class ScreeningFlag {
  const ScreeningFlag({required this.code, required this.severity, required this.section, required this.message});

  factory ScreeningFlag.fromJson(Map<String, dynamic> j) =>
      ScreeningFlag(code: str(j['code']), severity: str(j['severity']).toLowerCase(), section: intOf(j['section']), message: str(j['message']));

  final String code;
  final String severity;
  final int section;
  final String message;

  bool get isDeferral => severity == 'defer';

  /// The agent never decides: a deferral flag is only a likely outcome for the doctor to confirm.
  String get label => isDeferral ? 'Likely deferral – doctor to confirm' : 'Review';

  BadgeVariant get variant => isDeferral ? BadgeVariant.danger : BadgeVariant.warning;
}

class ScreeningAnswer {
  const ScreeningAnswer({required this.questionId, required this.question, required this.answer});

  final String questionId;
  final String question;
  final String answer;
}

class ScreeningSection {
  const ScreeningSection({required this.index, required this.title, required this.confidential, required this.items});

  final int index;
  final String title;
  final bool confidential;
  final List<ScreeningAnswer> items;
}

/// The agent's report (lifelink.screening.v2, stored as ReportJson): risk, recommendation, summary, flags, answers.
class ScreeningReportContent {
  const ScreeningReportContent({
    required this.riskLevel,
    required this.recommendation,
    required this.summary,
    required this.doctorNotes,
    required this.governance,
    required this.flags,
    required this.sections,
  });

  /// Null for legacy records without an AI report (or unreadable JSON).
  static ScreeningReportContent? tryParse(String? json) {
    if (json == null || json.trim().isEmpty) return null;
    try {
      final j = jsonDecode(json);
      if (j is! Map) return null;
      final m = Map<String, dynamic>.from(j);
      return ScreeningReportContent(
        riskLevel: str(m['risk_level']),
        recommendation: str(m['recommendation']),
        summary: str(m['summary']),
        doctorNotes: m['doctor_notes']?.toString(),
        governance: m['governance']?.toString(),
        flags: (m['flags'] is List ? m['flags'] as List : const [])
            .whereType<Map>()
            .map((e) => ScreeningFlag.fromJson(Map<String, dynamic>.from(e)))
            .toList(),
        sections: (m['sections'] is List ? m['sections'] as List : const []).whereType<Map>().map((s) {
          final items = (s['items'] is List ? s['items'] as List : const []).whereType<Map>().map((i) => ScreeningAnswer(
                questionId: str(i['question_id']),
                question: str(i['question']),
                answer: str(i['answer']),
              ));
          return ScreeningSection(
            index: intOf(s['index']),
            title: str(s['title']),
            confidential: boolOf(s['confidential']),
            items: items.toList(),
          );
        }).toList(),
      );
    } catch (_) {
      return null;
    }
  }

  final String riskLevel; // HIGH | MEDIUM | LOW
  final String recommendation;
  final String summary;
  final String? doctorNotes;
  final String? governance;
  final List<ScreeningFlag> flags;
  final List<ScreeningSection> sections;

  /// Answers in a flagged section are highlighted for the doctor.
  bool isHighlighted(ScreeningSection s) => flags.any((f) => f.section == s.index);

  List<ScreeningFlag> flagsFor(ScreeningSection s) => flags.where((f) => f.section == s.index).toList();
}

BadgeVariant riskVariant(String? risk) => switch (risk) {
      'HIGH' => BadgeVariant.danger,
      'MEDIUM' => BadgeVariant.warning,
      'LOW' => BadgeVariant.success,
      _ => BadgeVariant.neutral,
    };

/// DonorVerificationResponseDto: one version of a donor's screening report and the doctor's decision.
class ScreeningReport {
  const ScreeningReport({
    required this.id,
    required this.acceptanceId,
    required this.version,
    required this.isLatestVersion,
    required this.decidedByName,
    required this.isAssignedToMe,
    required this.status,
    required this.reportJson,
    required this.riskLevel,
    required this.recommendation,
    required this.notes,
    required this.verifiedAt,
    required this.createdAt,
    required this.donorName,
    required this.donorBloodGroup,
    required this.donorAccountStatus,
    required this.acceptanceStatus,
    required this.bloodRequestId,
    required this.requestBloodGroup,
    required this.requestStatus,
    required this.unitsRequired,
    required this.fulfilledUnits,
    required this.reservedUnits,
    required this.hasFreeSlot,
  });

  factory ScreeningReport.fromJson(Map<String, dynamic> j) => ScreeningReport(
        id: str(j['donorVerificationId']),
        acceptanceId: str(j['acceptanceId']),
        version: intOf(j['reportVersion'], 1),
        isLatestVersion: boolOf(j['isLatestVersion']),
        decidedByName: j['decidedByName']?.toString(),
        isAssignedToMe: boolOf(j['isAssignedToMe']),
        status: str(j['status']),
        reportJson: j['reportJson']?.toString(),
        riskLevel: j['riskLevel']?.toString(),
        recommendation: j['recommendation']?.toString(),
        notes: j['notes']?.toString(),
        verifiedAt: parseDate(j['verifiedAt']),
        createdAt: parseDate(j['createdAt']),
        donorName: str(j['donorName'], 'Donor'),
        donorBloodGroup: j['donorBloodGroup']?.toString(),
        donorAccountStatus: j['donorAccountStatus']?.toString(),
        acceptanceStatus: str(j['acceptanceStatus']),
        bloodRequestId: str(j['bloodRequestId']),
        requestBloodGroup: str(j['requestBloodGroup']),
        requestStatus: str(j['requestStatus']),
        unitsRequired: intOf(j['unitsRequired']),
        fulfilledUnits: intOf(j['fulfilledUnits']),
        reservedUnits: intOf(j['reservedUnits']),
        hasFreeSlot: boolOf(j['hasFreeSlot']),
      );

  final String id;
  final String acceptanceId;
  final int version;
  final bool isLatestVersion;
  final String? decidedByName;
  final bool isAssignedToMe;
  final String status; // Pending | Approved | Rejected | Superseded | Closed
  final String? reportJson;
  final String? riskLevel;
  final String? recommendation;
  final String? notes;
  final DateTime? verifiedAt;
  final DateTime? createdAt;
  final String donorName;
  final String? donorBloodGroup;
  final String? donorAccountStatus;
  final String acceptanceStatus;
  final String bloodRequestId;
  final String requestBloodGroup;
  final String requestStatus;
  final int unitsRequired;
  final int fulfilledUnits;
  final int reservedUnits;
  final bool hasFreeSlot;

  ScreeningReportContent? get content => ScreeningReportContent.tryParse(reportJson);

  /// Only the newest Pending version of a completed screening can be decided; superseded and decided versions are
  /// read-only (the API keeps every version immutable).
  bool get canDecide => status == 'Pending' && acceptanceStatus == 'ScreeningCompleted';
}

/// All report versions of one acceptance (one row per donor, like the web queue).
class ReportGroup {
  ReportGroup(List<ScreeningReport> all) : versions = (List.of(all)..sort((a, b) => a.version.compareTo(b.version)));

  final List<ScreeningReport> versions;

  ScreeningReport get latest => versions.last;
}

enum ReportTab { review, awaiting, history }

/// Groups versions by acceptance and splits them into the web's three tabs. "Waiting for review": assigned to me
/// first, then highest risk, then oldest.
Map<ReportTab, List<ReportGroup>> groupReports(List<ScreeningReport> reports) {
  final byAcceptance = <String, List<ScreeningReport>>{};
  for (final r in reports) {
    (byAcceptance[r.acceptanceId] ??= []).add(r);
  }
  final groups = byAcceptance.values.map(ReportGroup.new).toList();
  const riskOrder = {'HIGH': 0, 'MEDIUM': 1, 'LOW': 2};
  final review = groups.where((g) => g.latest.canDecide).toList()
    ..sort((a, b) {
      final mine = (b.latest.isAssignedToMe ? 1 : 0) - (a.latest.isAssignedToMe ? 1 : 0);
      if (mine != 0) return mine;
      final risk = (riskOrder[a.latest.riskLevel] ?? 3) - (riskOrder[b.latest.riskLevel] ?? 3);
      if (risk != 0) return risk;
      return (a.latest.createdAt ?? DateTime(0)).compareTo(b.latest.createdAt ?? DateTime(0));
    });
  final awaiting = groups.where((g) => g.latest.acceptanceStatus == 'Verified').toList();
  final history = groups.where((g) => !review.contains(g) && !awaiting.contains(g)).toList()
    ..sort((a, b) => (b.latest.createdAt ?? DateTime(0)).compareTo(a.latest.createdAt ?? DateTime(0)));
  return {ReportTab.review: review, ReportTab.awaiting: awaiting, ReportTab.history: history};
}

/// /api/donor-verification (same calls as the web's screeningApi).
class ScreeningRepository {
  ScreeningRepository(this._api);

  final ApiClient _api;

  Future<List<ScreeningReport>> reports() async =>
      unwrapList(await _api.get('/donor-verification')).map(ScreeningReport.fromJson).toList();

  /// Approval reserves one donation slot; optional notes are shown to the donor.
  Future<void> approve(String id, String notes) => _api.put('/donor-verification/$id/approve', body: {'notes': notes});

  /// Rejection with a reason the donor sees; the request stays open to other donors.
  Future<void> reject(String id, String reason) => _api.put('/donor-verification/$id/reject', body: {'notes': reason});
}

final screeningRepositoryProvider = Provider<ScreeningRepository>((ref) => ScreeningRepository(ref.watch(apiClientProvider)));

final screeningReportsProvider =
    FutureProvider.autoDispose<List<ScreeningReport>>((ref) => ref.watch(screeningRepositoryProvider).reports());
