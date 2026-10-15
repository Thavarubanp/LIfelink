import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:lifelink_mobile/core/routing/routes.dart';
import 'package:lifelink_mobile/core/theme/app_theme.dart';
import 'package:lifelink_mobile/features/analysis/analysis_repository.dart';
import 'package:lifelink_mobile/features/emergencies/emergency_repository.dart';
import 'package:lifelink_mobile/features/inventory/hospital_home_screen.dart';
import 'package:lifelink_mobile/features/inventory/inventory_repository.dart';
import 'package:lifelink_mobile/features/profile/profile_repository.dart';

import '../helpers.dart';

const _hospital = HospitalProfile(
  hospitalId: 'h1',
  name: 'Central Hospital',
  address: '1 Main Street',
  city: 'Colombo',
  contactNumber: '0112345678',
  email: 'hospital@example.test',
  isVerified: true,
  doctorCount: 2,
  packetShelfLifeDays: 35,
  expiryAlertDays: 7,
);

class _FakeAnalysisRepository extends AnalysisRepository {
  _FakeAnalysisRepository() : super(MockedApi().client);

  @override
  Future<AnalysisStatus> status({bool background = false}) async =>
      AnalysisStatus(
        state: 'Idle',
        cooldownEndsAt: null,
        serverNow: DateTime.utc(2026, 10, 15),
        lastRun: null,
        nextScheduledAt: null,
        scheduleEnabled: false,
        cooldownSeconds: 120,
      );
}

Future<GoRouter> _pumpHome(WidgetTester tester, {required double width}) async {
  tester.view.physicalSize = Size(width * 3, 2340);
  tester.view.devicePixelRatio = 3;
  addTearDown(tester.view.reset);

  final destinations = <String>[
    AppRoutes.hospitalEmergencies,
    AppRoutes.createRequest,
    AppRoutes.hospitalDonate,
    AppRoutes.hospitalVerifyRequests,
  ];
  final router = GoRouter(
    initialLocation: AppRoutes.hospitalHome,
    routes: [
      GoRoute(
        path: AppRoutes.hospitalHome,
        builder: (_, _) => const HospitalHomeScreen(),
      ),
      for (final path in destinations)
        GoRoute(
          path: path,
          builder: (_, _) => Scaffold(body: Text('route:$path')),
        ),
    ],
  );
  addTearDown(router.dispose);

  await tester.pumpWidget(
    ProviderScope(
      overrides: [
        myHospitalProvider.overrideWith((ref) async => _hospital),
        hospitalInventoryProvider.overrideWith((ref) async => []),
        criticalEmergenciesProvider.overrideWith((ref) async => []),
        analysisRepositoryProvider.overrideWithValue(_FakeAnalysisRepository()),
      ],
      retry: (_, _) => null,
      child: MaterialApp.router(theme: AppTheme.light(), routerConfig: router),
    ),
  );
  await tester.pump();
  await tester.pump();
  return router;
}

void main() {
  const actions = <(String, String, Key)>[
    (
      'Emergencies',
      AppRoutes.hospitalEmergencies,
      Key('quick-action-emergencies'),
    ),
    (
      'Create blood request',
      AppRoutes.createRequest,
      Key('quick-action-create-request'),
    ),
    ('Donate blood', AppRoutes.hospitalDonate, Key('quick-action-donate')),
    (
      'Verify requests',
      AppRoutes.hospitalVerifyRequests,
      Key('quick-action-verify'),
    ),
  ];

  testWidgets('phone uses a two-by-two one-line quick-action grid', (
    tester,
  ) async {
    await _pumpHome(tester, width: 360);

    final positions = <String, Offset>{
      for (final (label, _, key) in actions)
        label: tester.getTopLeft(find.byKey(key)),
    };
    expect(positions['Emergencies']!.dy, positions['Create blood request']!.dy);
    expect(positions['Donate blood']!.dy, positions['Verify requests']!.dy);
    expect(
      positions['Donate blood']!.dy,
      greaterThan(positions['Emergencies']!.dy),
    );
    expect(
      positions['Emergencies']!.dx,
      lessThan(positions['Create blood request']!.dx),
    );
    expect(
      positions['Donate blood']!.dx,
      lessThan(positions['Verify requests']!.dx),
    );

    for (final (label, _, _) in actions) {
      final text = tester.widget<Text>(find.text(label));
      expect(text.maxLines, 1);
      expect(text.softWrap, isFalse);
    }
    expect(find.byTooltip('Scan packet'), findsNothing);

    final recommendations = tester.widget<Text>(find.text('Recommendations'));
    expect(recommendations.maxLines, 1);
    expect(recommendations.softWrap, isFalse);
  });

  testWidgets('wide layout puts all four quick actions in one row', (
    tester,
  ) async {
    await _pumpHome(tester, width: 800);

    final top = tester.getTopLeft(find.byKey(actions.first.$3)).dy;
    for (final (_, _, key) in actions.skip(1)) {
      expect(tester.getTopLeft(find.byKey(key)).dy, top);
    }
  });

  testWidgets('quick actions push the existing More-entry routes', (
    tester,
  ) async {
    final router = await _pumpHome(tester, width: 360);

    for (final (_, path, key) in actions) {
      await tester.tap(find.byKey(key));
      await tester.pumpAndSettle();
      expect(find.text('route:$path'), findsOneWidget);
      router.pop();
      await tester.pumpAndSettle();
    }
  });
}
