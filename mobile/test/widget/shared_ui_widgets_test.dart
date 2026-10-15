import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/attachments/attachment.dart';
import 'package:lifelink_mobile/core/attachments/attachment_widgets.dart';
import 'package:lifelink_mobile/core/theme/app_theme.dart';
import 'package:lifelink_mobile/core/widgets/common.dart';
import 'package:lifelink_mobile/core/widgets/dashboard_metric_card.dart';
import 'package:lifelink_mobile/core/widgets/state_views.dart';

void _size(WidgetTester tester, Size size, {double textScale = 1}) {
  tester.view.devicePixelRatio = 1;
  tester.view.physicalSize = size;
  tester.platformDispatcher.textScaleFactorTestValue = textScale;
  addTearDown(tester.view.reset);
  addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
}

Widget _app(Widget child, {bool dark = false}) => MaterialApp(
  theme: dark ? AppTheme.dark() : AppTheme.light(),
  home: Scaffold(body: SingleChildScrollView(child: child)),
);

void main() {
  for (final testCase in const [
    (size: Size(320, 568), scale: 1.0, dark: false),
    (size: Size(360, 780), scale: 2.0, dark: false),
    (size: Size(390, 844), scale: 1.0, dark: true),
    (size: Size(430, 932), scale: 1.0, dark: false),
    (size: Size(600, 960), scale: 1.0, dark: true),
    (size: Size(768, 1024), scale: 2.0, dark: true),
    (size: Size(820, 1180), scale: 1.0, dark: false),
    (size: Size(1024, 1366), scale: 1.0, dark: true),
    (size: Size(844, 390), scale: 1.0, dark: false),
    (size: Size(1180, 820), scale: 1.0, dark: true),
  ]) {
    testWidgets(
      'metric cards stay equal and overflow-free at ${testCase.size} x${testCase.scale}',
      (tester) async {
        _size(tester, testCase.size, textScale: testCase.scale);
        await tester.pumpWidget(
          _app(
            Padding(
              padding: const EdgeInsets.all(16),
              child: DashboardMetricGrid(
                narrowColumns: 2,
                wideColumns: 4,
                children: [
                  for (var i = 0; i < 4; i++)
                    DashboardMetricCard(
                      key: Key('metric-$i'),
                      label: i == 0
                          ? 'A deliberately long dashboard metric label'
                          : 'Metric $i',
                      value: i == 0 ? '999999999999' : '$i',
                      detail: 'A long but readable supporting explanation',
                      icon: Icons.insights_outlined,
                      color: AppColors.amber600,
                      onTap: () {},
                    ),
                ],
              ),
            ),
            dark: testCase.dark,
          ),
        );
        await tester.pumpAndSettle();

        final first = tester.getRect(find.byKey(const Key('metric-0')));
        for (var i = 1; i < 4; i++) {
          final rect = tester.getRect(find.byKey(Key('metric-$i')));
          expect(rect.width, closeTo(first.width, 0.01));
          expect(rect.height, closeTo(first.height, 0.01));
        }
        expect(tester.takeException(), isNull);
      },
    );
  }

  testWidgets('adaptive form rows stack on phones and align when wide', (
    tester,
  ) async {
    _size(tester, const Size(320, 568));
    await tester.pumpWidget(
      _app(
        const Padding(
          padding: EdgeInsets.all(16),
          child: AdaptiveFormRow(
            children: [
              TextField(key: Key('field-a')),
              TextField(key: Key('field-b')),
            ],
          ),
        ),
      ),
    );
    expect(
      tester.getTopLeft(find.byKey(const Key('field-b'))).dy,
      greaterThan(tester.getTopLeft(find.byKey(const Key('field-a'))).dy),
    );
    expect(tester.takeException(), isNull);

    _size(tester, const Size(768, 1024));
    await tester.pumpWidget(
      _app(
        const Padding(
          padding: EdgeInsets.all(16),
          child: AdaptiveFormRow(
            children: [
              TextField(key: Key('wide-field-a')),
              TextField(key: Key('wide-field-b')),
            ],
          ),
        ),
      ),
    );
    expect(
      tester.getTopLeft(find.byKey(const Key('wide-field-a'))).dy,
      tester.getTopLeft(find.byKey(const Key('wide-field-b'))).dy,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('shared states and actions remain usable at large text', (
    tester,
  ) async {
    _size(tester, const Size(320, 568), textScale: 2);
    await tester.pumpWidget(
      _app(
        Column(
          children: [
            const EmptyView(
              title: 'Nothing here yet',
              message: 'New activity will appear here when it is available.',
            ),
            BusyButton(label: 'Continue', onPressed: () {}),
          ],
        ),
      ),
    );

    expect(
      tester.getSize(find.byType(FilledButton)).height,
      greaterThanOrEqualTo(48),
    );
    expect(find.text('Nothing here yet'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('refresh failure keeps stale data visible and disables actions', (
    tester,
  ) async {
    var shouldFail = false;
    var actions = 0;
    final valueProvider = FutureProvider<int>((ref) async {
      if (shouldFail) throw Exception('Refresh failed');
      return 7;
    });

    await tester.pumpWidget(
      ProviderScope(
        retry: (_, _) => null,
        child: MaterialApp(
          theme: AppTheme.light(),
          home: Scaffold(
            body: Consumer(
              builder: (context, ref, _) => Column(
                children: [
                  TextButton(
                    key: const Key('force-refresh'),
                    onPressed: () {
                      shouldFail = true;
                      ref.invalidate(valueProvider);
                    },
                    child: const Text('Refresh'),
                  ),
                  AsyncView(
                    value: ref.watch(valueProvider),
                    onRetry: () => ref.invalidate(valueProvider),
                    data: (value) => FilledButton(
                      key: const Key('stale-action'),
                      onPressed: () => actions++,
                      child: Text('Delete $value'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Delete 7'), findsOneWidget);

    await tester.tap(find.byKey(const Key('force-refresh')));
    await tester.pumpAndSettle();

    expect(find.text('Delete 7'), findsOneWidget);
    expect(find.textContaining('previously loaded data'), findsOneWidget);
    await tester.tap(
      find.byKey(const Key('stale-action')),
      warnIfMissed: false,
    );
    expect(actions, 0);
  });

  testWidgets('attachment completion after disposal does not update state', (
    tester,
  ) async {
    final picker = Completer<Attachment?>();
    await tester.pumpWidget(
      _app(
        AttachmentPickerField(
          value: null,
          onChanged: (_) {},
          documentPicker: () => picker.future,
        ),
      ),
    );
    await tester.tap(find.text('Attach file'));
    await tester.pump();

    await tester.pumpWidget(_app(const SizedBox.shrink()));
    picker.complete(null);
    await tester.pump();

    expect(tester.takeException(), isNull);
  });
}
