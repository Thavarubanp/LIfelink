import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/notifications/unread_badge.dart';

import '../helpers.dart';

final _countProvider = Provider<int>((ref) => 0);

void main() {
  testWidgets(
    'UnreadBadge accepts a count provider, hides zero and caps at 99+',
    (tester) async {
      await tester.pumpWidget(
        testApp(
          UnreadBadge(count: _countProvider, child: Icon(Icons.home_outlined)),
          overrides: [_countProvider.overrideWithValue(0)],
        ),
      );
      expect(tester.widget<Badge>(find.byType(Badge)).isLabelVisible, isFalse);

      await tester.pumpWidget(
        testApp(
          UnreadBadge(count: _countProvider, child: Icon(Icons.home_outlined)),
          overrides: [_countProvider.overrideWithValue(105)],
        ),
      );
      expect(tester.widget<Badge>(find.byType(Badge)).isLabelVisible, isTrue);
      expect(find.text('99+'), findsOneWidget);
    },
  );
}
