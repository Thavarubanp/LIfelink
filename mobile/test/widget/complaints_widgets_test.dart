import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/admin/complaints/admin_complaints.dart';
import 'package:lifelink_mobile/features/complaints/complaints_repository.dart';
import 'package:lifelink_mobile/features/complaints/complaints_screen.dart';

import '../helpers.dart';

void main() {
  testWidgets('complaint recipient search debounces and ignores stale input', (
    tester,
  ) async {
    final queries = <String>[];
    await tester.pumpWidget(
      testApp(
        const ComplaintsScreen(),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
          myComplaintsProvider.overrideWith((ref) async => <Complaint>[]),
          complaintHospitalsProvider.overrideWith((ref) async => []),
          complaintUsersProvider.overrideWith((ref, query) async {
            if (query.length < 2) return [];
            queries.add(query);
            return const <ComplaintUserChoice>[];
          }),
        ],
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(
      find.widgetWithText(FloatingActionButton, 'File complaint'),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('User'));
    await tester.pump();

    final search = find.widgetWithText(TextField, 'Search registered users');
    await tester.enterText(search, 'al');
    await tester.pump(const Duration(milliseconds: 200));
    await tester.enterText(search, 'alice');
    await tester.pump(const Duration(milliseconds: 349));
    expect(queries, isEmpty);

    await tester.pump(const Duration(milliseconds: 1));
    await tester.pump();
    expect(queries, ['alice']);
  });
}
