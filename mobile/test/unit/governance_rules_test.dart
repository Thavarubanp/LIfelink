import 'dart:convert';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/attachments/attachment.dart';
import 'package:lifelink_mobile/core/notifications/notification_rules.dart';
import 'package:lifelink_mobile/core/routing/routes.dart';
import 'package:lifelink_mobile/features/activity/activity_log.dart';
import 'package:lifelink_mobile/features/admin/admin_repository.dart';
import 'package:lifelink_mobile/features/admin/complaints/admin_complaints.dart';
import 'package:lifelink_mobile/features/admin/directory/admin_actions.dart';
import 'package:lifelink_mobile/features/admin/directory/directory.dart';
import 'package:lifelink_mobile/features/admin/oversight/admin_activity_screen.dart';
import 'package:lifelink_mobile/features/admin/registrations/registration_repository.dart';
import 'package:lifelink_mobile/features/appeals/appeal_models.dart';
import 'package:lifelink_mobile/features/governance/governance_repository.dart';
import 'package:lifelink_mobile/features/transfers/transfer_repository.dart';

AdminHospital hospital(
  String status, {
  List<Map<String, dynamic>> history = const [],
}) => AdminHospital.fromJson({
  'hospitalId': 'h1',
  'name': 'Fake General',
  'approvalStatus': status,
  'approvalHistory': history,
});

void main() {
  group('attention badges', () {
    test('totals and badge text', () {
      final a = AdminAttention.fromJson({
        'newBloodRequests': 2,
        'newTransfers': 1,
        'pendingRegistrations': 3,
        'pendingAppeals': 0,
        'pendingComplaints': 4,
      });
      expect(a.total, 10);
      expect(a.newActivity, 3);
      expect(badgeText(0), isNull);
      expect(badgeText(7), '7');
      expect(badgeText(120), '99+');
    });
  });

  group('registrations (web HospitalManagementPage rules)', () {
    test('who may approve, reject or comment', () {
      expect(
        (
          hospital('Pending').canApprove,
          hospital('Pending').canReject,
          hospital('Pending').canComment,
        ),
        (true, true, false),
      );
      final replied = hospital('AwaitingAdminReview');
      expect(
        (replied.canApprove, replied.canReject, replied.canComment),
        (true, true, true),
      );
      final rejected = hospital('Rejected');
      expect(
        (
          rejected.canApprove,
          rejected.canReject,
          rejected.canComment,
          rejected.waitingForHospital,
        ),
        (false, false, true, true),
      );
      expect(hospital('Approved').canComment, isFalse);
    });

    test('the newest conversation entry is sent with every decision', () {
      final h = hospital(
        'AwaitingAdminReview',
        history: [
          {'id': 'e1', 'type': 'Submitted', 'fromAdmin': false},
          {
            'id': 'e2',
            'type': 'Rejected',
            'fromAdmin': true,
            'message': 'Licence unreadable',
          },
          {
            'id': 'e3',
            'type': 'HospitalReply',
            'fromAdmin': false,
            'changedFields': 'City: Colmbo -> Colombo',
          },
        ],
      );
      expect(h.lastEntryId, 'e3');
      expect(h.history[1].title, 'Rejected by the administrator');
      expect(hospital('Pending').lastEntryId, isNull);
    });
  });

  group('appeals', () {
    final appeal = Appeal.fromJson({
      'appealId': 'a1',
      'userEmail': 'donor@example.test',
      'status': 'REJECTED',
      'canReject': false,
      'awaitingAdminReply': true,
      'messages': [
        {
          'messageId': 'm1',
          'fromAdmin': false,
          'message': 'Please reinstate me',
          'attachmentUrl': 'data:image/png;base64,AAAA',
          'attachmentName': 'proof.png',
        },
        {
          'messageId': 'm2',
          'fromAdmin': true,
          'message': '[REJECTED] Not enough evidence',
          'attachmentUrl': 'data:image/png;base64,AAAA',
        },
      ],
    });

    test('decision messages never show a file; reject only once', () {
      expect(appeal.messages[1].isDecision, isTrue);
      final thread = appeal.thread();
      expect(thread[0].attachment, isNotNull);
      expect(thread[1].attachment, isNull);
      expect(appeal.canReject, isFalse);
      expect(appeal.who, 'donor@example.test');
    });

    test('appeal reason is 10–2000 characters', () {
      expect(appealReasonError('too short'), isNotNull);
      expect(appealReasonError('This is a long enough reason'), isNull);
      expect(appealReasonError('x' * 2001), isNotNull);
    });
  });

  group('complaints (web filters)', () {
    Complaint c(String status, {String? hospitalId, String? targetUserId}) =>
        Complaint.fromJson({
          'complaintId': 'c-$status',
          'status': status,
          'subject': 'Late response',
          'description': 'Nobody answered',
          'userEmail': 'donor@example.test',
          'hospitalId': hospitalId,
          'targetUserId': targetUserId,
        });

    test('status, target (incl. general question) and search', () {
      final general = c('OPEN');
      final againstHospital = c('RESOLVED', hospitalId: 'h1');
      expect(general.target, ComplaintTarget.general);
      expect(general.targetLabel, 'General question (no target)');
      expect(
        complaintMatches(
          general,
          status: 'OPEN',
          target: ComplaintTarget.general,
        ),
        isTrue,
      );
      expect(
        complaintMatches(
          againstHospital,
          status: 'OPEN',
          target: ComplaintTarget.all,
        ),
        isFalse,
      );
      expect(
        complaintMatches(
          againstHospital,
          status: 'RESOLVED',
          target: ComplaintTarget.hospital,
        ),
        isTrue,
      );
      expect(
        complaintMatches(
          c('REJECTED'),
          status: 'OPEN',
          target: ComplaintTarget.all,
        ),
        isFalse,
      );
      expect(
        complaintMatches(
          general,
          status: 'ALL',
          target: ComplaintTarget.all,
          query: 'nobody',
        ),
        isTrue,
      );
      expect(
        complaintMatches(
          general,
          status: 'ALL',
          target: ComplaintTarget.all,
          query: 'zzz',
        ),
        isFalse,
      );
    });

    test('the thread starts with the description', () {
      expect(c('OPEN').thread().first.text, 'Nobody answered');
    });
  });

  group('activity log oversight', () {
    test('only open items can be suspended', () {
      for (final s in ['Pending', 'Verified', 'Approved']) {
        expect(AdminBloodRequest.fromJson({'status': s}).canSuspend, isTrue);
      }
      expect(
        AdminBloodRequest.fromJson({'status': 'Completed'}).canSuspend,
        isFalse,
      );
      expect(
        OversightItem.transfer(
          Transfer.fromJson({'status': 'Pending', 'transferRequestId': 't1'}),
        ).canSuspend,
        isTrue,
      );
      expect(
        OversightItem.transfer(
          Transfer.fromJson({'status': 'Completed', 'transferRequestId': 't2'}),
        ).canSuspend,
        isFalse,
      );
    });

    test('"New" since last seen; filters by status, suspension, type and Sri Lanka date', () {
      final item = OversightItem.transfer(
        Transfer.fromJson({
          'transferRequestId': 't1',
          'status': 'Pending',
          'transferType': 'Offer',
          'isSuspended': true,
          'createdAt': '2026-10-04T20:00:00Z', // 5 Oct in Sri Lanka
        }),
      );
      expect(item.isNewSince(null), isTrue);
      expect(item.isNewSince(DateTime.utc(2026, 10, 4)), isTrue);
      expect(item.isNewSince(DateTime.utc(2026, 10, 5)), isFalse);
      expect(oversightMatches(item, status: suspendedFilter), isTrue);
      expect(oversightMatches(item, status: 'Completed'), isFalse);
      expect(oversightMatches(item, type: 'Request'), isFalse);
      expect(
        oversightMatches(
          item,
          from: DateTime(2026, 10, 5),
          to: DateTime(2026, 10, 5),
        ),
        isTrue,
      );
      expect(oversightMatches(item, to: DateTime(2026, 10, 4)), isFalse);
    });

    test('activity query parameters', () {
      final q = const ActivityQuery(
        page: 2,
        type: 'Appeal',
      ).copyWith(from: DateTime(2026, 10, 3));
      expect(q.toParams(), {
        'page': 2,
        'pageSize': 10,
        'type': 'Appeal',
        'from': '2026-10-03',
        'to': null,
      });
      expect(q.copyWith(clearType: true, clearFrom: true).hasFilters, isFalse);
      final page = ActivityPage.fromJson({
        'items': [],
        'total': 21,
        'page': 1,
        'pageSize': 10,
        'types': ['Appeal'],
      });
      expect(page.totalPages, 3);
    });
  });

  group('admin messages and suspensions', () {
    test('message: subject 3–120, message 5–2000', () {
      expect(adminMessageError('Hi', 'Hello there'), contains('subject'));
      expect(adminMessageError('Notice', 'Hey'), contains('message'));
      expect(
        adminMessageError('Notice', 'Please update your documents.'),
        isNull,
      );
      expect(adminMessageError('x' * 121, 'Please update'), isNotNull);
    });

    test('suspension reason 3–500; only donor/patient accounts', () {
      expect(suspendReasonError('no'), isNotNull);
      expect(suspendReasonError('Fake documents'), isNull);
      expect(
        AdminUser.fromJson({
          'roles': ['User'],
        }).isDonorPatient,
        isTrue,
      );
      expect(
        AdminUser.fromJson({
          'roles': ['Doctor'],
        }).isDonorPatient,
        isFalse,
      );
      expect(
        AdminUser.fromJson({
          'roles': ['HospitalStaff'],
        }).isDonorPatient,
        isFalse,
      );
      expect(
        AdminUser.fromJson({
          'roles': ['User', 'Admin'],
        }).isDonorPatient,
        isFalse,
      );
    });
  });

  group('attachments (web limits)', () {
    test('type, empty and 2 MB checks', () {
      expect(AttachmentRules.check('report.pdf', 10), isNull);
      expect(AttachmentRules.check('photo.JPG', 10), isNull);
      expect(AttachmentRules.check('virus.exe', 10), contains('not allowed'));
      expect(
        AttachmentRules.check('empty.pdf', 0),
        AttachmentRules.emptyFileMessage,
      );
      expect(
        AttachmentRules.check('big.pdf', AttachmentRules.maxBytes + 1),
        AttachmentRules.tooLargeMessage,
      );
    });

    test('data URL round trip and empty files', () {
      final a = Attachment.fromBytes(
        'note.png',
        Uint8List.fromList(utf8.encode('png-bytes')),
      );
      expect(a.url, startsWith('data:image/png;base64,'));
      expect(a.isImage, isTrue);
      expect(utf8.decode(a.bytes!), 'png-bytes');
      expect(
        Attachment.fromApi('data:application/pdf;base64,', 'x.pdf'),
        isNull,
      );
      expect(Attachment.fromApi(null, null), isNull);
      expect(
        () => Attachment.fromBytes('x.exe', Uint8List(4)),
        throwsA(isA<AttachmentException>()),
      );
    });
  });

  group('phone notification rules', () {
    PolledNotification n(
      String id,
      int minute, {
      String type = 'AdminMessage',
    }) => PolledNotification(
      id: id,
      title: 't$id',
      message: 'm',
      type: type,
      createdAt: DateTime.utc(2026, 10, 5, 4, minute),
    );

    test('first run only records the newest; later only new ones, once', () {
      final first = newNotifications([
        n('a', 1),
        n('b', 2),
      ], const PollMarker());
      expect(first.toShow, isEmpty);
      expect(first.marker.lastSeenId, 'b');

      final second = newNotifications([
        n('a', 1),
        n('b', 2),
        n('c', 3),
      ], first.marker);
      expect(second.toShow.map((x) => x.id), ['c']);

      final third = newNotifications([
        n('a', 1),
        n('b', 2),
        n('c', 3),
      ], second.marker);
      expect(third.toShow, isEmpty);
    });

    test('at most five, newest kept', () {
      final marker = PollMarker(
        lastSeenId: 'x',
        lastSeenAt: DateTime.utc(2026, 10, 5, 4, 0),
      );
      final r = newNotifications([
        for (var i = 1; i <= 8; i++) n('$i', i),
      ], marker);
      expect(r.toShow.map((x) => x.id), ['4', '5', '6', '7', '8']);
    });

    test('admin attention summary and tap routes', () {
      expect(
        attentionIncrease(null, const AttentionSnapshot(appeals: 2)),
        isNull,
      );
      expect(
        attentionIncrease(
          const AttentionSnapshot(appeals: 1),
          const AttentionSnapshot(appeals: 2, registrations: 1),
        ),
        '1 new hospital registration(s), 1 new appeal(s)',
      );
      expect(
        attentionIncrease(
          const AttentionSnapshot(appeals: 2),
          const AttentionSnapshot(appeals: 1),
        ),
        isNull,
      );
      expect(
        routeForNotificationType('PacketsExpiringSoon'),
        AppRoutes.hospitalRecommendations,
      );
      expect(
        routeForNotificationType('AppealApproved'),
        AppRoutes.governanceStatus,
      );
      expect(routeForNotificationType('AdminMessage'), AppRoutes.notifications);
      expect(
        routeForNotificationType('EligibleDonorAlert'),
        AppRoutes.donorRequests,
      );
      expect(
        routeForNotificationType('DonorApproved'),
        AppRoutes.donorAcceptances,
      );
      expect(
        routeForNotificationType('TransferAccepted'),
        AppRoutes.hospitalTransfers,
      );
    });
  });
}
