import 'package:flutter_test/flutter_test.dart';
import 'package:lifelink_mobile/features/attention/role_attention_repository.dart';

import '../helpers.dart';

void main() {
  test('GET /attention maps all role counts as a background request', () async {
    final api = MockedApi(token: 'jwt');
    api.adapter.onGet(
      '/attention',
      (server) => server.reply(200, {
        'success': true,
        'data': {
          'pendingHospitalVerifications': 4,
          'pendingTransferResponses': 2,
          'pendingScreeningReviews': 7,
        },
      }),
    );

    final attention = await RoleAttentionRepository(api.client)
        .get(background: true);

    expect(attention.pendingHospitalVerifications, 4);
    expect(attention.pendingTransferResponses, 2);
    expect(attention.pendingScreeningReviews, 7);
    expect(api.sent.single.path, '/attention');
    expect(api.sent.single.headers.containsKey('X-LifeLink-Activity'), isFalse);
  });
}
