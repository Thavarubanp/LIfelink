import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/session_activity.dart';
import 'package:lifelink_mobile/core/utils/format.dart';
import 'package:lifelink_mobile/core/utils/validators.dart';
import 'package:lifelink_mobile/features/analysis/analysis_repository.dart';
import 'package:lifelink_mobile/features/donate/donate_repository.dart';
import 'package:lifelink_mobile/features/emergencies/emergencies_screen.dart';
import 'package:lifelink_mobile/features/emergencies/emergency_repository.dart';
import 'package:lifelink_mobile/features/inventory/inventory_models.dart';
import 'package:lifelink_mobile/features/inventory/inventory_screen.dart';
import 'package:lifelink_mobile/features/inventory/packet_rules.dart';
import 'package:lifelink_mobile/features/transfers/transfer_repository.dart';

import '../helpers.dart';

Map<String, dynamic> inv(String group, int units, int threshold, {int expiring = 0, bool? isLow}) => {
      'inventoryId': 'inv-$group',
      'hospitalId': 'h1',
      'bloodGroup': group,
      'unitsAvailable': units,
      'minimumThreshold': threshold,
      'maximumCapacity': 100,
      'expiringSoonUnits': expiring,
      'isLowStock': ?isLow,
    };

void main() {
  group('low-stock rule (units < threshold, same as InventoryRules)', () {
    test('below, equal and above the threshold', () {
      expect(isBelowThreshold(4, 5), isTrue);
      expect(isBelowThreshold(5, 5), isFalse);
      expect(isBelowThreshold(6, 5), isFalse);
      expect(isBelowThreshold(0, 0), isFalse);
    });

    test('the API flag is used; the rule only fills in when it is missing', () {
      expect(InventoryItem.fromJson(inv('O-', 4, 5)).isLowStock, isTrue);
      expect(InventoryItem.fromJson(inv('O-', 5, 5)).isLowStock, isFalse);
      expect(InventoryItem.fromJson(inv('O-', 9, 5, isLow: true)).isLowStock, isTrue);
    });

    test('search and stock filter', () {
      final items = [InventoryItem.fromJson(inv('A+', 2, 5)), InventoryItem.fromJson(inv('O-', 9, 5, expiring: 2))];
      final packets = [
        BloodPacket.fromJson({'packetId': 'p1', 'trackingNumber': 'PKT-00000042', 'bloodGroup': 'O-', 'status': 'Available'}),
      ];
      expect(filterGroups(items, packets, filter: StockFilter.low).map((i) => i.bloodGroup), ['A+']);
      expect(filterGroups(items, packets, filter: StockFilter.expiring).map((i) => i.bloodGroup), ['O-']);
      expect(filterGroups(items, packets, term: '0042').map((i) => i.bloodGroup), ['O-']);
      expect(filterGroups(items, packets, term: 'a+').map((i) => i.bloodGroup), ['A+']);
    });
  });

  group('collected date (same rules as the web form)', () {
    final today = DateTime(2026, 10, 5);

    test('required, not in the future', () {
      expect(collectedDateError(null, 35, today), 'Collected date is required.');
      expect(collectedDateError(DateTime(2026, 10, 6), 35, today), 'Collected date cannot be in the future.');
      expect(collectedDateError(today, 35, today), isNull);
    });

    test('not already expired: collected + shelf life must be after today', () {
      expect(collectedDateError(DateTime(2026, 9, 1), 35, today), isNull); // expires 6 Oct
      expect(collectedDateError(DateTime(2026, 8, 31), 35, today), contains('would already be expired')); // expires 5 Oct
      expect(earliestCollectedDate(35, today), DateTime(2026, 9, 1));
      expect(collectedDateError(earliestCollectedDate(35, today), 35, today), isNull);
    });
  });

  test('tracking numbers from scanned or typed codes', () {
    expect(normalizeTrackingNumber(' pkt-00000012 '), 'PKT-00000012');
    expect(normalizeTrackingNumber('PKT-123456789'), 'PKT-123456789');
    expect(normalizeTrackingNumber('PKT-12'), isNull);
    expect(normalizeTrackingNumber('https://example.test'), isNull);
    expect(normalizeTrackingNumber(null), isNull);
  });

  group('validators', () {
    test('password rule matches the API DTO', () {
      expect(Validators.strongPassword('Secret#123'), isNull);
      expect(Validators.strongPassword('secret#123'), isNotNull);
      expect(Validators.strongPassword('Sec#1'), contains('8 characters'));
    });

    test('email, phone, OTP and ranges', () {
      expect(Validators.email('donor@example.test'), isNull);
      expect(Validators.email('not-an-email'), 'Invalid email address format.');
      expect(Validators.phone('0771234567'), isNull);
      expect(Validators.phone('12345'), isNotNull);
      expect(Validators.otp('123456'), isNull);
      expect(Validators.otp('12345'), isNotNull);
      expect(Validators.intRange('21', 'Number of packets', min: 1, max: 20), 'Number of packets must be at most 20.');
      expect(Validators.intRange('0', 'Units', min: 1), 'Units must be at least 1.');
    });
  });

  group('formatting', () {
    test('Sri Lanka time is UTC+5:30', () {
      expect(Fmt.sriLankaDateTime(DateTime.utc(2026, 10, 3, 8, 45)), '3 Oct 2026, 2:15 pm');
      expect(Fmt.date(DateTime.utc(2026, 10, 4, 20, 0)), '5 Oct 2026');
      expect(Fmt.sriLankaToday(DateTime.utc(2026, 10, 4, 19, 0)), DateTime(2026, 10, 5));
    });

    test('countdown and labels', () {
      expect(Fmt.countdown(const Duration(seconds: 119)), '1:59');
      expect(Fmt.countdown(const Duration(milliseconds: 1)), '0:01');
      expect(Fmt.countdown(Duration.zero), '0:00');
      expect(Fmt.humanize('InventoryShortageHelp'), 'Inventory shortage help');
      expect(Fmt.humanize('TRANSFER_IN'), 'Transfer in');
    });
  });

  group('session activity', () {
    test('marks at most every 15 s from the later of mark and confirmation', () {
      final clock = FakeClock();
      final a = SessionActivity(clock: clock.call);
      expect(a.takeMark(), isTrue);
      expect(a.takeMark(), isFalse);
      clock.advance(const Duration(seconds: 15));
      expect(a.takeMark(), isTrue);
      a.releaseMark();
      expect(a.takeMark(), isTrue);
    });
  });

  group('analysis status', () {
    final status = AnalysisStatus.fromJson({
      'state': 'Cooldown',
      'cooldownEndsAt': '2026-10-05T04:32:00Z',
      'serverNow': '2026-10-05T04:30:00Z',
      'nextScheduledAt': '2026-10-05T04:41:30Z',
      'scheduleEnabled': true,
      'lastRun': {'trigger': 'Manual', 'hospitalName': 'Fake General', 'status': 'CompletedRuleBased', 'lowStockAlerts': 2, 'expiringAlerts': 1, 'skippedDuplicates': 3},
    });

    test('cooldown and next run use the server clock', () {
      // The phone clock is 1 minute behind the server
      final phone = DateTime.utc(2026, 10, 5, 4, 29);
      const offset = Duration(minutes: 1);
      expect(status.cooldownLeft(phone, offset), const Duration(minutes: 2));
      expect(status.nextRunText(phone, offset), 'Next scheduled run in 12 min.');
      expect(status.cooldownLeft(DateTime.utc(2026, 10, 5, 4, 40), offset), Duration.zero);
    });

    test('run summary like the web panel', () {
      expect(status.lastRun!.summary, '2 low-stock, 1 expiring-soon alert sent, 3 repeated alert(s) skipped (rule-based: the AI agents were unavailable)');
      expect(status.lastRun!.triggerLabel, 'manual by Fake General');
    });
  });

  group('transfers', () {
    Transfer t(String type, String status, String sender, String receiver, String creator) => Transfer.fromJson({
          'transferRequestId': '$type-$status-$sender',
          'transferType': type,
          'status': status,
          'senderHospitalId': sender,
          'receiverHospitalId': receiver,
          'createdByHospitalId': creator,
        });

    test('incoming / outgoing / history for this hospital', () {
      final all = [
        t('Request', 'Pending', 'me', 'other', 'other'), // they ask me for blood: I answer
        t('Offer', 'Pending', 'other', 'me', 'other'), // they offer me blood: I answer
        t('Request', 'Pending', 'other', 'me', 'me'), // I asked: waiting for them
        t('Offer', 'Completed', 'me', 'other', 'me'),
      ];
      final lists = TransferLists(all, 'me');
      expect(lists.incoming.length, 2);
      expect(lists.outgoing.length, 1);
      expect(lists.history.single.status, 'Completed');
    });
  });

  test('emergency actions follow the API status rules', () {
    EmergencyRequest e(String status) => EmergencyRequest.fromJson({'status': status, 'priority': 'CRITICAL'});
    expect(emergencyActions(e('Pending')), (approve: true, reject: true, complete: true));
    expect(emergencyActions(e('Approved')), (approve: false, reject: true, complete: true));
    expect(emergencyActions(e('Completed')), (approve: false, reject: false, complete: false));
    expect(emergencyActions(e('Rejected')), (approve: false, reject: false, complete: false));
  });

  test('public request free slots and donation labels', () {
    final r = PublicRequest.fromJson({'unitsRequired': 5, 'fulfilledUnits': 1, 'reservedUnits': 2});
    expect(r.remainingUnits, 4);
    expect(r.freeSlots, 2);
    expect(HospitalDonation.fromJson({'status': 'Accepted'}).statusLabel, 'Awaiting doctor approval');
    expect(HospitalDonation.fromJson({'status': 'Matched', 'requestDeleted': true}).statusLabel, 'Closed');
  });
}
