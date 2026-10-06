import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/core/auth/auth_controller.dart';
import 'package:lifelink_mobile/features/profile/profile_repository.dart';
import 'package:lifelink_mobile/features/profile/user_profile_screen.dart';
import 'package:lifelink_mobile/features/search/global_search_screen.dart';
import 'package:lifelink_mobile/features/search/search_repository.dart';

import '../helpers.dart';

UserProfile profile({
  bool owner = true,
  String? email = 'owner@example.test',
  String? phone = '0771234567',
  String? address = 'Colombo',
}) => UserProfile(
  userId: 'u1',
  firstName: 'Sam',
  lastName: 'Silva',
  email: email,
  phoneNumber: phone,
  gender: 'Other',
  address: address,
  isEmailPublic: false,
  isPhonePublic: true,
  isAddressPublic: false,
  roles: const ['User'],
  displayStatus: 'Active',
  bloodGroup: 'O+',
  bloodGroupConfirmed: false,
  lastDonationDate: null,
  nextEligibleDonationDate: null,
  createdAt: null,
  canEdit: owner,
);

void main() {
  testWidgets('owner sees contact values and independent privacy controls', (
    tester,
  ) async {
    await tester.pumpWidget(
      testApp(
        const UserProfileScreen(userId: 'u1'),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
          userProfileProvider.overrideWith((ref, id) async => profile()),
        ],
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('owner@example.test'), findsOneWidget);
    expect(find.text('0771234567'), findsOneWidget);
    expect(find.text('Colombo'), findsOneWidget);
    expect(find.text('Public'), findsOneWidget);
    expect(find.text('Private'), findsNWidgets(2));
    expect(find.text('Edit profile'), findsOneWidget);
  });

  testWidgets(
    'private cross-user contacts never retain a visible value or controls',
    (tester) async {
      await tester.pumpWidget(
        testApp(
          const UserProfileScreen(userId: 'u1'),
          overrides: [
            authControllerProvider.overrideWith(
              () => FakeAuthController(AuthState.signedIn(testUser())),
            ),
            userProfileProvider.overrideWith(
              (ref, id) async => profile(
                owner: false,
                email: null,
                phone: null,
                address: null,
              ),
            ),
          ],
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Private'), findsNWidgets(3));
      expect(find.text('owner@example.test'), findsNothing);
      expect(find.text('Edit profile'), findsNothing);
      expect(find.byType(FilterChip), findsNothing);
    },
  );

  testWidgets('global search shows typed results and empty state', (
    tester,
  ) async {
    const results = SearchResults(
      hospitals: [
        SearchItem(
          resultType: 'Hospital',
          id: 'h1',
          displayName: 'Central Hospital',
          subText: 'Colombo',
          extraInfo: null,
          avatarInitial: 'H',
          status: 'Active',
        ),
      ],
      doctors: [],
      users: [],
    );
    await tester.pumpWidget(
      testApp(
        const GlobalSearchScreen(),
        overrides: [
          authControllerProvider.overrideWith(
            () => FakeAuthController(AuthState.signedIn(testUser())),
          ),
          globalSearchProvider.overrideWith((ref, query) async => results),
        ],
      ),
    );
    expect(find.textContaining('Enter at least 2'), findsOneWidget);
    await tester.enterText(find.byType(TextField), 'ce');
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pumpAndSettle();
    expect(find.text('Central Hospital'), findsOneWidget);
    expect(find.text('Hospitals'), findsOneWidget);
  });
}
