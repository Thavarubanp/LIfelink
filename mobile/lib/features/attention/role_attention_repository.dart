import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';

class RoleAttention {
  const RoleAttention({
    required this.pendingHospitalVerifications,
    required this.pendingTransferResponses,
    required this.pendingScreeningReviews,
  });

  factory RoleAttention.fromJson(Map<String, dynamic> json) => RoleAttention(
    pendingHospitalVerifications: intOf(json['pendingHospitalVerifications']),
    pendingTransferResponses: intOf(json['pendingTransferResponses']),
    pendingScreeningReviews: intOf(json['pendingScreeningReviews']),
  );

  final int pendingHospitalVerifications;
  final int pendingTransferResponses;
  final int pendingScreeningReviews;
}

class RoleAttentionRepository {
  RoleAttentionRepository(this._api);

  final ApiClient _api;

  Future<RoleAttention> get({bool background = false}) async =>
      RoleAttention.fromJson(
        unwrapMap(
          await _api.get(
            '/attention',
            options: apiOptions(background: background),
          ),
        ),
      );
}

final roleAttentionRepositoryProvider = Provider<RoleAttentionRepository>(
  (ref) => RoleAttentionRepository(ref.watch(apiClientProvider)),
);
