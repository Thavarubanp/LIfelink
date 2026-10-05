import '../../core/api/json.dart';
import '../../core/attachments/attachment.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/thread_view.dart';

/// AppealMessageDto.
class AppealMessage {
  const AppealMessage({required this.id, required this.fromAdmin, required this.adminEmail, required this.message, required this.attachment, required this.createdAt});

  factory AppealMessage.fromJson(Map<String, dynamic> j) => AppealMessage(
        id: str(j['messageId']),
        fromAdmin: boolOf(j['fromAdmin']),
        adminEmail: j['adminEmail']?.toString(),
        message: str(j['message']),
        attachment: Attachment.fromApi(j['attachmentUrl']?.toString(), j['attachmentName']?.toString()),
        createdAt: parseDate(j['createdAt']),
      );

  final String id;
  final bool fromAdmin;
  final String? adminEmail;
  final String message;
  final Attachment? attachment;
  final DateTime? createdAt;

  /// Decisions are recorded as "[APPROVED] ..." admin messages and never carry a file (web AppealThread).
  bool get isDecision => fromAdmin && RegExp(r'^\[(APPROVED|REJECTED|CLOSED|PENDING)\]').hasMatch(message);
}

/// AppealResponseDto, used by the admin screens and the appellant's governance screen.
class Appeal {
  const Appeal({
    required this.id,
    required this.userId,
    required this.userEmail,
    required this.hospitalId,
    required this.hospitalName,
    required this.reason,
    required this.status,
    required this.submittedAt,
    required this.messages,
    required this.isClosed,
    required this.awaitingAdminReply,
    required this.canAppellantReply,
    required this.canReject,
    required this.rejectedAt,
  });

  factory Appeal.fromJson(Map<String, dynamic> j) => Appeal(
        id: str(j['appealId']),
        userId: j['userId']?.toString(),
        userEmail: j['userEmail']?.toString(),
        hospitalId: j['hospitalId']?.toString(),
        hospitalName: j['hospitalName']?.toString(),
        reason: str(j['reason']),
        status: str(j['status']),
        submittedAt: parseDate(j['submittedAt']),
        messages: j['messages'] is List
            ? (j['messages'] as List).whereType<Map>().map((e) => AppealMessage.fromJson(Map<String, dynamic>.from(e))).toList()
            : const [],
        isClosed: boolOf(j['isClosed']),
        awaitingAdminReply: boolOf(j['awaitingAdminReply']),
        canAppellantReply: boolOf(j['canAppellantReply']),
        canReject: boolOf(j['canReject']),
        rejectedAt: parseDate(j['rejectedAt']),
      );

  final String id;
  final String? userId;
  final String? userEmail;
  final String? hospitalId;
  final String? hospitalName;
  final String reason;
  final String status; // PENDING | REJECTED | APPROVED | CLOSED
  final DateTime? submittedAt;
  final List<AppealMessage> messages;
  final bool isClosed;
  final bool awaitingAdminReply;
  final bool canAppellantReply;
  final bool canReject;
  final DateTime? rejectedAt;

  bool get isHospital => hospitalId != null && hospitalId!.isNotEmpty;
  String get who => userEmail ?? hospitalName ?? 'Unknown';

  /// The thread as chat messages; the appellant side is labelled [appellantLabel].
  List<ThreadMessage> thread({String appellantLabel = 'Appellant'}) => [
        for (final m in messages)
          ThreadMessage(
            author: m.fromAdmin ? 'Administrator' : appellantLabel,
            fromAdmin: m.fromAdmin,
            at: m.createdAt,
            text: m.message,
            attachment: m.isDecision ? null : m.attachment,
          ),
      ];
}

BadgeVariant appealVariant(String status) => switch (status) {
      'PENDING' => BadgeVariant.warning,
      'REJECTED' => BadgeVariant.danger,
      'APPROVED' => BadgeVariant.success,
      _ => BadgeVariant.neutral,
    };
