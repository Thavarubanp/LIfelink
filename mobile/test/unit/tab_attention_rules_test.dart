import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/attention/tab_attention_controller.dart';
import 'package:lifelink_mobile/features/blood_requests/blood_request_models.dart';

DonorAcceptance acceptance(
  String status, {
  bool deleted = false,
  bool suspended = false,
}) => DonorAcceptance.fromJson({
  'acceptanceId': status,
  'bloodRequestId': 'request-$status',
  'status': status,
  'requestDeleted': deleted,
  'requestSuspended': suspended,
});

void main() {
  test('donation badge counts only commitments needing donor action', () {
    final items = [
      acceptance('Accepted'),
      acceptance('ScreeningPending'),
      acceptance('Verified'),
      acceptance('ScreeningCompleted'),
      acceptance('Matched'),
      acceptance('Accepted', deleted: true),
      acceptance('Verified', suspended: true),
    ];

    expect(donorActionCount(items), 3);
  });

  test('each attention key reads only its mapped tab count', () {
    const state = TabAttentionState(
      donorCreate: 1,
      donorDonations: 2,
      hospitalHome: 3,
      hospitalInventory: 4,
      hospitalTransfers: 5,
      doctorRequests: 6,
      doctorReports: 7,
    );

    expect(
      TabAttentionKey.values.map(state.count),
      orderedEquals([1, 2, 3, 4, 5, 6, 7]),
    );
  });

  test(
    'create marker starts silent and retains changed statuses until seen',
    () {
      final baseline = reconcileDonorRequestStatuses(null, {
        'r1': 'Pending',
        'r2': 'Approved',
      });
      expect(baseline.changed, 0);

      final changed = reconcileDonorRequestStatuses(baseline.marker, {
        'r1': 'Verified',
        'r2': 'Approved',
        'r3': 'Pending',
      });
      expect(changed.changed, 1);
      expect(changed.marker, {
        'r1': 'Pending',
        'r2': 'Approved',
        'r3': 'Pending',
      });

      expect(
        reconcileDonorRequestStatuses(changed.marker, {
          'r1': 'Verified',
          'r2': 'Approved',
          'r3': 'Pending',
        }).changed,
        1,
      );
    },
  );
}
