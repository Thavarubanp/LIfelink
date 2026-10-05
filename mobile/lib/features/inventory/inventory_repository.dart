import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import '../profile/profile_repository.dart';
import 'inventory_models.dart';

/// /api/Inventory endpoints, exactly as the web app's inventoryApi uses them.
class InventoryRepository {
  InventoryRepository(this._api);

  final ApiClient _api;

  Future<List<InventoryItem>> forHospital(String hospitalId) async =>
      unwrapList(await _api.get('/Inventory/hospital/$hospitalId')).map(InventoryItem.fromJson).toList();

  /// Every hospital's stock (used for the "available at" hints when requesting a transfer).
  Future<List<InventoryItem>> all() async => unwrapList(await _api.get('/Inventory')).map(InventoryItem.fromJson).toList();

  /// New blood group category for the signed-in hospital (thresholds only; stock arrives as packets).
  Future<void> createGroup({required String bloodGroup, required int minimumThreshold, required int maximumCapacity}) =>
      _api.post('/Inventory', body: {
        'bloodGroup': bloodGroup,
        'minimumThreshold': minimumThreshold,
        'maximumCapacity': maximumCapacity,
      });

  /// Thresholds of an own group; with [issuePacketIds] those packets are issued with [auditNotes] as the reason.
  Future<void> update(
    String inventoryId, {
    required int minimumThreshold,
    required int maximumCapacity,
    List<String>? issuePacketIds,
    String? auditNotes,
  }) =>
      _api.put('/Inventory/$inventoryId', body: {
        'minimumThreshold': minimumThreshold,
        'maximumCapacity': maximumCapacity,
        'issuePacketIds': ?issuePacketIds,
        'auditNotes': ?auditNotes,
      });

  /// The signed-in hospital's packets, optionally filtered.
  Future<List<BloodPacket>> packets({String? status, String? bloodGroup}) async =>
      unwrapList(await _api.get('/Inventory/packets', query: {'status': status, 'bloodGroup': bloodGroup}))
          .map(BloodPacket.fromJson)
          .toList();

  /// One packet with its full history (null when it is not visible to this hospital).
  Future<BloodPacket?> packet(String packetId) async {
    final list = unwrapList(await _api.get('/Inventory/packets', query: {'packetId': packetId}));
    return list.isEmpty ? null : BloodPacket.fromJson(list.first);
  }

  /// Collected blood entered as packets (1–20); one idempotency key per form.
  Future<List<BloodPacket>> createPackets({
    required String bloodGroup,
    required String collectionDate,
    required int quantity,
    required String idempotencyKey,
  }) async =>
      unwrapList(await _api.post(
        '/Inventory/packets',
        body: {'bloodGroup': bloodGroup, 'collectionDate': collectionDate, 'quantity': quantity},
        options: apiOptions(idempotencyKey: idempotencyKey),
      )).map(BloodPacket.fromJson).toList();

  /// Only the creating hospital, while it holds the packet and it is Available.
  Future<void> updatePacket(String packetId, {required String bloodGroup, required String collectionDate}) =>
      _api.put('/Inventory/packets/$packetId', body: {'bloodGroup': bloodGroup, 'collectionDate': collectionDate});
}

final inventoryRepositoryProvider = Provider<InventoryRepository>((ref) => InventoryRepository(ref.watch(apiClientProvider)));

/// The signed-in hospital's blood groups.
final hospitalInventoryProvider = FutureProvider.autoDispose<List<InventoryItem>>((ref) async {
  final hospital = await ref.watch(myHospitalProvider.future);
  final list = await ref.watch(inventoryRepositoryProvider).forHospital(hospital.hospitalId);
  list.sort((a, b) => a.bloodGroup.compareTo(b.bloodGroup));
  return list;
});

/// The signed-in hospital's packets with one status (null = all statuses).
final packetsProvider = FutureProvider.autoDispose.family<List<BloodPacket>, String?>(
  (ref, status) => ref.watch(inventoryRepositoryProvider).packets(status: status),
);
