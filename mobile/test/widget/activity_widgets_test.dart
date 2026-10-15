import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/activity/activity_log.dart';

import '../helpers.dart';

ActivityPage _page(String id, String summary) => ActivityPage(
  items: [
    ActivityEntry(
      id: id,
      occurredAt: DateTime.utc(2026, 10, 15),
      actorRole: 'User',
      actorName: 'Test Person',
      action: 'Updated',
      entityType: 'BloodRequest',
      summary: summary,
    ),
  ],
  total: 1,
  page: 1,
  pageSize: 10,
  recordedFrom: DateTime.utc(2026, 10, 3),
  types: const ['BloodRequest'],
);

void main() {
  testWidgets('an older activity response cannot replace a newer filter', (
    tester,
  ) async {
    final first = Completer<ActivityPage>();
    final second = Completer<ActivityPage>();
    var calls = 0;

    await tester.pumpWidget(
      testApp(
        Scaffold(
          body: ActivityLogView(
            load: (_, _) {
              calls++;
              return calls == 1 ? first.future : second.future;
            },
          ),
        ),
      ),
    );
    await tester.pump();

    await tester.tap(find.text('Blood requests'));
    await tester.pump();
    expect(calls, 2);

    second.complete(_page('new', 'Newer filtered activity'));
    await tester.pump();
    expect(find.text('Newer filtered activity'), findsOneWidget);

    first.complete(_page('old', 'Older unfiltered activity'));
    await tester.pump();
    expect(find.text('Newer filtered activity'), findsOneWidget);
    expect(find.text('Older unfiltered activity'), findsNothing);
    expect(tester.takeException(), isNull);
  });
}
