import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/misc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/api/api_error.dart';
import 'package:lifelink_mobile/features/analysis/analysis_panel.dart';
import 'package:lifelink_mobile/features/analysis/analysis_repository.dart';
import 'package:lifelink_mobile/features/inventory/inventory_models.dart';
import 'package:lifelink_mobile/features/inventory/inventory_repository.dart';
import 'package:lifelink_mobile/features/inventory/inventory_screen.dart';
import 'package:lifelink_mobile/features/inventory/packet_forms.dart';
import 'package:lifelink_mobile/features/profile/profile_repository.dart';

import '../helpers.dart';

const hospital = HospitalProfile(
  hospitalId: 'h1',
  name: 'Fake General',
  address: '1 Test Road',
  city: 'Colombo',
  contactNumber: '0110000000',
  email: 'fake.general@example.test',
  isVerified: true,
  doctorCount: 1,
  packetShelfLifeDays: 35,
  expiryAlertDays: 7,
);

InventoryItem item(String group, int units, int threshold) => InventoryItem.fromJson({
      'inventoryId': 'inv-$group',
      'hospitalId': 'h1',
      'bloodGroup': group,
      'unitsAvailable': units,
      'minimumThreshold': threshold,
      'maximumCapacity': 100,
    });

List<Override> inventoryOverrides(FutureOr<List<InventoryItem>> Function() items) => [
      myHospitalProvider.overrideWith((ref) async => hospital),
      hospitalInventoryProvider.overrideWith((ref) async => items()),
      packetsProvider.overrideWith((ref, status) async => <BloodPacket>[]),
    ];

/// Records packet creations instead of calling the API.
class FakeInventoryRepository extends InventoryRepository {
  FakeInventoryRepository() : super(MockedApi().client);

  final List<Map<String, Object>> created = [];

  @override
  Future<List<BloodPacket>> createPackets({
    required String bloodGroup,
    required String collectionDate,
    required int quantity,
    required String idempotencyKey,
  }) async {
    created.add({'bloodGroup': bloodGroup, 'collectionDate': collectionDate, 'quantity': quantity, 'key': idempotencyKey});
    return [BloodPacket.fromJson({'packetId': 'p1', 'trackingNumber': 'PKT-00000001', 'bloodGroup': bloodGroup})];
  }
}

class FakeAnalysisRepository extends AnalysisRepository {
  FakeAnalysisRepository(this.statusJson) : super(MockedApi().client);

  Map<String, dynamic> statusJson;
  int runs = 0;

  @override
  Future<AnalysisStatus> status({bool background = false}) async => AnalysisStatus.fromJson(statusJson);

  @override
  Future<AnalysisRun> run() async {
    runs++;
    return AnalysisRun.fromJson({'status': 'Completed', 'lowStockAlerts': 1, 'expiringAlerts': 0});
  }
}

void main() {
  group('Inventory screen states', () {
    testWidgets('loading', (tester) async {
      final never = Completer<List<InventoryItem>>();
      await tester.pumpWidget(testApp(const InventoryScreen(), overrides: inventoryOverrides(() => never.future)));
      await tester.pump();
      expect(find.text('Loading inventory...'), findsOneWidget);
    });

    testWidgets('empty', (tester) async {
      await tester.pumpWidget(testApp(const InventoryScreen(), overrides: inventoryOverrides(() => [])));
      await tester.pumpAndSettle();
      expect(find.text('No blood groups yet. Add packets or a blood group to start.'), findsOneWidget);
    });

    testWidgets('error with retry', (tester) async {
      await tester.pumpWidget(testApp(const InventoryScreen(),
          overrides: inventoryOverrides(() => throw const ApiError(ApiError.noConnection, isNetwork: true))));
      await tester.pumpAndSettle();
      expect(find.text(ApiError.noConnection), findsOneWidget);
      expect(find.text('Retry'), findsOneWidget);
    });

    testWidgets('data: health badges and the below-threshold filter', (tester) async {
      await tester.pumpWidget(testApp(const InventoryScreen(), overrides: inventoryOverrides(() => [item('A+', 2, 5), item('O-', 9, 5)])));
      await tester.pumpAndSettle();
      expect(find.text('Healthy'), findsOneWidget);
      expect(find.text('2 unit(s)'), findsOneWidget);
      expect(find.text('9 unit(s)'), findsOneWidget);

      await tester.tap(find.widgetWithText(ChoiceChip, 'Below threshold'));
      await tester.pumpAndSettle();
      expect(find.text('2 unit(s)'), findsOneWidget);
      expect(find.text('9 unit(s)'), findsNothing);
    });
  });

  group('Add packets form', () {
    Future<FakeInventoryRepository> pumpForm(WidgetTester tester) async {
      final repo = FakeInventoryRepository();
      tester.view.physicalSize = const Size(1200, 2400);
      addTearDown(tester.view.reset);
      await tester.pumpWidget(testApp(
        Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: FilledButton(
                onPressed: () => Navigator.of(context).push(MaterialPageRoute<bool>(
                  builder: (_) => Scaffold(body: PacketFormSheet(today: DateTime(2026, 10, 5))),
                )),
                child: const Text('open'),
              ),
            ),
          ),
        ),
        overrides: [
          myHospitalProvider.overrideWith((ref) async => hospital),
          inventoryRepositoryProvider.overrideWithValue(repo),
        ],
      ));
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();
      return repo;
    }

    testWidgets('quantity must be 1-20', (tester) async {
      final repo = await pumpForm(tester);
      await tester.enterText(find.byKey(const Key('packet-quantity')), '21');
      await tester.tap(find.byKey(const Key('packet-save')));
      await tester.pump();
      expect(find.text('Number of packets must be at most 20.'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('packet-quantity')), '0');
      await tester.tap(find.byKey(const Key('packet-save')));
      await tester.pump();
      expect(find.text('Number of packets must be at least 1.'), findsOneWidget);
      expect(repo.created, isEmpty);
    });

    testWidgets('valid form sends the Sri Lanka date and one idempotency key', (tester) async {
      final repo = await pumpForm(tester);
      expect(find.text('2026-10-05'), findsOneWidget); // today by default
      await tester.enterText(find.byKey(const Key('packet-quantity')), '3');
      await tester.tap(find.byKey(const Key('packet-save')));
      await tester.pumpAndSettle();

      expect(repo.created.single['collectionDate'], '2026-10-05');
      expect(repo.created.single['quantity'], 3);
      expect((repo.created.single['key'] as String).length, 36);
      expect(find.text('open'), findsOneWidget); // the form closed
    });
  });

  group('Inventory analysis panel', () {
    Map<String, dynamic> status(String state, {String? cooldownEndsAt}) => {
          'state': state,
          'serverNow': DateTime.now().toUtc().toIso8601String(),
          'cooldownEndsAt': cooldownEndsAt,
          'scheduleEnabled': true,
          'nextScheduledAt': DateTime.now().toUtc().add(const Duration(minutes: 10)).toIso8601String(),
          'lastRun': {'startedAt': '2026-10-05T04:30:00Z', 'trigger': 'Scheduled', 'status': 'Completed', 'lowStockAlerts': 2, 'expiringAlerts': 1},
        };

    testWidgets('idle: last run and Run analysis now', (tester) async {
      final repo = FakeAnalysisRepository(status('Idle'));
      await tester.pumpWidget(testApp(const Scaffold(body: SingleChildScrollView(child: InventoryAnalysisPanel())),
          overrides: [analysisRepositoryProvider.overrideWithValue(repo)]));
      await tester.pump();
      await tester.pump();
      expect(find.textContaining('(scheduled) – 2 low-stock, 1 expiring-soon alert sent'), findsOneWidget);
      expect(find.text('Run analysis now'), findsOneWidget);

      await tester.tap(find.byKey(const Key('analysis-run')));
      await tester.pump();
      await tester.pump();
      expect(repo.runs, 1);
    });

    testWidgets('running and cooldown disable the button', (tester) async {
      final repo = FakeAnalysisRepository(status('Running'));
      await tester.pumpWidget(testApp(const Scaffold(body: SingleChildScrollView(child: InventoryAnalysisPanel())),
          overrides: [analysisRepositoryProvider.overrideWithValue(repo)]));
      await tester.pump();
      await tester.pump();
      expect(find.byKey(const Key('analysis-running')), findsOneWidget);
      expect(tester.widget<FilledButton>(find.byKey(const Key('analysis-run'))).onPressed, isNull);

      repo.statusJson = status('Cooldown', cooldownEndsAt: DateTime.now().toUtc().add(const Duration(seconds: 90)).toIso8601String());
      await tester.pumpWidget(testApp(const Scaffold(body: SingleChildScrollView(child: InventoryAnalysisPanel(key: Key('second')))),
          overrides: [analysisRepositoryProvider.overrideWithValue(repo)]));
      await tester.pump();
      await tester.pump();
      expect(find.textContaining('Available again in 1:'), findsOneWidget);
      expect(tester.widget<FilledButton>(find.byKey(const Key('analysis-run'))).onPressed, isNull);
    });
  });
}
