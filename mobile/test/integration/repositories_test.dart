import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';
import 'package:lifelink_mobile/features/analysis/analysis_repository.dart';
import 'package:lifelink_mobile/features/donate/donate_repository.dart';
import 'package:lifelink_mobile/features/inventory/inventory_repository.dart';
import 'package:lifelink_mobile/features/notifications/notifications_repository.dart';
import 'package:lifelink_mobile/features/profile/profile_repository.dart';
import 'package:lifelink_mobile/features/search/search_repository.dart';
import 'package:lifelink_mobile/features/transfers/transfer_repository.dart';

import '../helpers.dart';

/// Repositories against a mocked HTTP layer, with the API's real response shapes.
void main() {
  test('inventory: hospital groups (ApiResponse wrapper) and one packet with history', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet(
        '/Inventory/hospital/h1',
        (s) => s.reply(200, {
          'success': true,
          'message': 'ok',
          'data': [
            {
              'inventoryId': 'i1',
              'hospitalId': 'h1',
              'bloodGroup': 'O-',
              'unitsAvailable': 3,
              'minimumThreshold': 5,
              'maximumCapacity': 100,
              'isLowStock': true,
              'expiringSoonUnits': 1,
              'nextExpiryDate': '2026-10-09T00:00:00Z',
              'expiryAlertDays': 7,
            },
          ],
        }),
      )
      ..onGet(
        '/Inventory/packets',
        (s) => s.reply(200, {
          'success': true,
          'data': [
            {
              'packetId': 'p1',
              'trackingNumber': 'PKT-00000007',
              'bloodGroup': 'O-',
              'status': 'Available',
              'canEdit': true,
              'history': [
                {
                  'transactionId': 't1',
                  'transactionType': 'PACKET_CREATED',
                  'units': 1,
                  'notes': 'Collected',
                  'createdAt': '2026-10-05T04:30:00Z',
                },
              ],
            },
          ],
        }),
        queryParameters: {'packetId': 'p1'},
      );
    final repo = InventoryRepository(api.client);

    final groups = await repo.forHospital('h1');
    expect(groups.single.isLowStock, isTrue);
    expect(groups.single.nextExpiryDate, DateTime.utc(2026, 10, 9));

    final packet = await repo.packet('p1');
    expect(packet!.trackingNumber, 'PKT-00000007');
    expect(packet.history.single.transactionType, 'PACKET_CREATED');
  });

  test(
    'inventory: issuing sends thresholds, packet ids and the reason',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter.onPut(
        '/Inventory/i1',
        (s) => s.reply(200, {'success': true}),
        data: {
          'minimumThreshold': 5,
          'maximumCapacity': 100,
          'issuePacketIds': ['p1', 'p2'],
          'auditNotes': 'Theatre',
        },
      );

      await InventoryRepository(api.client).update(
        'i1',
        minimumThreshold: 5,
        maximumCapacity: 100,
        issuePacketIds: ['p1', 'p2'],
        auditNotes: 'Theatre',
      );
      expect(api.sent.single.method, 'PUT');
    },
  );

  test('transfers: list, approve with packets, 409 conflict message', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet(
        '/transfers',
        (s) => s.reply(200, {
          'success': true,
          'data': [
            {
              'transferRequestId': 't1',
              'transferType': 'Request',
              'status': 'Pending',
              'senderHospitalId': 'me',
              'receiverHospitalId': 'h2',
              'createdByHospitalId': 'h2',
              'unitsRequested': 2,
              'bloodGroup': 'A+',
              'isSuspended': true,
              'suspensionReason': 'Under review',
            },
          ],
        }),
      )
      ..onPut(
        '/transfers/t1/approve',
        (s) => s.reply(409, {
          'success': false,
          'message': 'The selected packets are no longer available.',
        }),
        data: {
          'packetIds': ['p1', 'p2'],
        },
      );
    final repo = TransferRepository(api.client);

    final list = await repo.all();
    expect(TransferLists(list, 'me').incoming.single.isSuspended, isTrue);
    await expectLater(
      repo.approve('t1', ['p1', 'p2']),
      throwsA(
        isA<ApiError>().having(
          (e) => e.message,
          'message',
          'The selected packets are no longer available.',
        ),
      ),
    );
  });

  test('transfer counterparts use the restricted group projection', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onGet(
      '/transfers/counterparts',
      (s) => s.reply(200, [
        {
          'hospitalId': 'h2',
          'hospitalName': 'Central',
          'bloodGroup': 'O-',
          'transferableUnits': 4,
        },
      ]),
      queryParameters: {'bloodGroup': 'O-'},
    );

    final item = (await TransferRepository(api.client).counterparts('O-'))
        .single;
    expect(item.hospitalName, 'Central');
    expect(item.transferableUnits, 4);
    expect(api.sent.single.path, '/transfers/counterparts');
  });

  test('user profile preserves nullable privacy fields and sends independent flags', () async {
    final api = MockedApi(token: 'jwt');
    final profileJson = {
      'userId': 'u1',
      'firstName': 'Sam',
      'lastName': 'Silva',
      'email': null,
      'phoneNumber': '0771234567',
      'address': null,
      'isEmailPublic': false,
      'isPhonePublic': true,
      'isAddressPublic': false,
      'canEdit': true,
    };
    api.adapter
      ..onGet('/profiles/user/u1', (s) => s.reply(200, profileJson))
      ..onPut(
        '/profiles/user/u1',
        (s) => s.reply(200, {...profileJson, 'isAddressPublic': true}),
        data: Matchers.any,
      );
    final repo = ProfileRepository(api.client);
    final profile = await repo.user('u1');
    expect(profile.email, isNull);
    expect(profile.address, isNull);
    final updated = await repo.updateUser(profile, isAddressPublic: true);
    expect(updated.isAddressPublic, isTrue);
    expect(api.sent.last.data['isEmailPublic'], isFalse);
    expect(api.sent.last.data['isPhonePublic'], isTrue);
    expect(api.sent.last.data['isAddressPublic'], isTrue);
  });

  test('global search uses the existing typed result contract', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onGet(
      '/search',
      (s) => s.reply(200, {
        'hospitals': [
          {
            'resultType': 'Hospital',
            'id': 'h1',
            'displayName': 'Central',
            'subText': 'Colombo',
          },
        ],
        'doctors': [],
        'users': [
          {
            'resultType': 'User',
            'id': 'u1',
            'displayName': 'Sam',
            'subText': 'private@example.test',
          },
        ],
      }),
      queryParameters: {'q': 'sa'},
    );
    final results = await SearchRepository(api.client).search(' sa ');
    expect(results.totalCount, 2);
    expect(results.users.single.id, 'u1');
  });

  test(
    'analysis: status is a background request; run returns the counts',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter
        ..onGet(
          '/Inventory/analysis/status',
          (s) => s.reply(200, {
            'success': true,
            'data': {
              'state': 'Idle',
              'serverNow': '2026-10-05T04:30:00Z',
              'scheduleEnabled': true,
              'cooldownSeconds': 120,
            },
          }),
        )
        ..onPost(
          '/Inventory/analysis/run',
          (s) => s.reply(200, {
            'success': true,
            'message': 'Inventory analysis complete.',
            'data': {
              'status': 'Completed',
              'trigger': 'Manual',
              'lowStockAlerts': 2,
              'expiringAlerts': 1,
            },
          }),
        );
      final repo = AnalysisRepository(api.client);

      final status = await repo.status(background: true);
      expect(status.state, 'Idle');
      expect(api.sent.last.headers.containsKey('X-LifeLink-Activity'), isFalse);

      final run = await repo.run();
      expect(run.lowStockAlerts, 2);
      expect(api.sent.last.headers['X-LifeLink-Activity'], '1');
    },
  );

  test('notifications and donations: plain-list responses', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter
      ..onGet(
        '/notifications/my',
        (s) => s.reply(200, [
          {
            'notificationId': 'n1',
            'title': 'Low stock: O-',
            'message': 'O- is below threshold',
            'notificationType': 'InventoryShortage',
            'isRead': false,
            'createdAt': '2026-10-05T04:30:00',
          },
        ]),
      )
      ..onGet('/notifications/unread-count', (s) => s.reply(200, {'count': 4}))
      ..onGet(
        '/Acceptances/my',
        (s) => s.reply(200, [
          {
            'acceptanceId': 'a1',
            'bloodRequestId': 'r1',
            'status': 'Accepted',
            'hospitalName': 'Fake Teaching',
            'requestBloodGroup': 'O-',
            'packets': [
              {'trackingNumber': 'PKT-00000003'},
            ],
          },
        ]),
      );

    final n = await NotificationsRepository(api.client).my();
    expect(n.single.type, 'InventoryShortage');
    expect(
      await NotificationsRepository(api.client).unreadCount(background: true),
      4,
    );

    final d = await DonateRepository(api.client).myDonations();
    expect(d.single.trackingNumbers, ['PKT-00000003']);
    expect(d.single.statusLabel, 'Awaiting doctor approval');
  });
}
