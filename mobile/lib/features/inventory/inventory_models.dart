import '../../core/api/json.dart';

/// The backend's single low-stock rule (InventoryRules.IsBelowThreshold): available units below the minimum threshold.
bool isBelowThreshold(int unitsAvailable, int minimumThreshold) => unitsAvailable < minimumThreshold;

/// InventoryResponseDto: one blood group of a hospital.
class InventoryItem {
  const InventoryItem({
    required this.inventoryId,
    required this.hospitalId,
    required this.hospitalName,
    required this.bloodGroup,
    required this.unitsAvailable,
    required this.minimumThreshold,
    required this.maximumCapacity,
    required this.expiringSoonUnits,
    required this.nextExpiryDate,
    required this.expiryAlertDays,
    required this.isLowStock,
    required this.isSurplus,
  });

  factory InventoryItem.fromJson(Map<String, dynamic> j) {
    final units = intOf(j['unitsAvailable']);
    final threshold = intOf(j['minimumThreshold']);
    final capacity = intOf(j['maximumCapacity'], 100);
    return InventoryItem(
      inventoryId: str(j['inventoryId']),
      hospitalId: str(j['hospitalId']),
      hospitalName: str(j['hospitalName']),
      bloodGroup: str(j['bloodGroup']),
      unitsAvailable: units,
      minimumThreshold: threshold,
      maximumCapacity: capacity,
      expiringSoonUnits: intOf(j['expiringSoonUnits']),
      nextExpiryDate: parseDate(j['nextExpiryDate']),
      expiryAlertDays: intOf(j['expiryAlertDays']),
      // The API sends the flag; the same rule is applied when it is missing
      isLowStock: j['isLowStock'] is bool ? j['isLowStock'] as bool : isBelowThreshold(units, threshold),
      isSurplus: j['isSurplus'] is bool ? j['isSurplus'] as bool : units >= (capacity * 0.8).floor(),
    );
  }

  final String inventoryId;
  final String hospitalId;
  final String hospitalName;
  final String bloodGroup;
  final int unitsAvailable;
  final int minimumThreshold;
  final int maximumCapacity;
  final int expiringSoonUnits;
  final DateTime? nextExpiryDate;
  final int expiryAlertDays;
  final bool isLowStock;
  final bool isSurplus;
}

/// InventoryTransactionResponseDto: one entry of a packet's or group's history.
class InventoryTransaction {
  const InventoryTransaction({
    required this.transactionId,
    required this.transactionType,
    required this.units,
    required this.notes,
    required this.createdAt,
  });

  factory InventoryTransaction.fromJson(Map<String, dynamic> j) => InventoryTransaction(
        transactionId: str(j['transactionId']),
        transactionType: str(j['transactionType']),
        units: intOf(j['units']),
        notes: str(j['notes']),
        createdAt: parseDate(j['createdAt']),
      );

  final String transactionId;
  final String transactionType;
  final int units;
  final String notes;
  final DateTime? createdAt;
}

/// BloodPacketResponseDto: one 440 ml packet with its tracking number.
class BloodPacket {
  const BloodPacket({
    required this.packetId,
    required this.trackingNumber,
    required this.createdByHospitalId,
    required this.createdByHospitalName,
    required this.hospitalId,
    required this.hospitalName,
    required this.bloodGroup,
    required this.volumeMl,
    required this.collectionDate,
    required this.expiryDate,
    required this.status,
    required this.source,
    required this.isExpiringSoon,
    required this.createdAt,
    required this.canEdit,
    this.history = const [],
  });

  factory BloodPacket.fromJson(Map<String, dynamic> j) => BloodPacket(
        packetId: str(j['packetId']),
        trackingNumber: str(j['trackingNumber'] ?? j['packetCode']),
        createdByHospitalId: str(j['createdByHospitalId']),
        createdByHospitalName: str(j['createdByHospitalName']),
        hospitalId: str(j['hospitalId']),
        hospitalName: str(j['hospitalName']),
        bloodGroup: str(j['bloodGroup']),
        volumeMl: intOf(j['volumeMl']),
        collectionDate: parseDate(j['collectionDate']),
        expiryDate: parseDate(j['expiryDate']),
        status: str(j['status']),
        source: str(j['source']),
        isExpiringSoon: boolOf(j['isExpiringSoon']),
        createdAt: parseDate(j['createdAt']),
        canEdit: boolOf(j['canEdit']),
        history: j['history'] is List
            ? (j['history'] as List).whereType<Map>().map((e) => InventoryTransaction.fromJson(Map<String, dynamic>.from(e))).toList()
            : const [],
      );

  final String packetId;
  final String trackingNumber;
  final String createdByHospitalId;
  final String createdByHospitalName;
  final String hospitalId;
  final String hospitalName;
  final String bloodGroup;
  final int volumeMl;
  final DateTime? collectionDate;
  final DateTime? expiryDate;
  final String status;
  final String source;
  final bool isExpiringSoon;
  final DateTime? createdAt;
  final bool canEdit;
  final List<InventoryTransaction> history;

  bool get createdHere => createdByHospitalId == hospitalId;

  /// Available and not yet expired: can be issued, transferred or donated.
  bool usableAt(DateTime now) => status == 'Available' && (expiryDate == null || expiryDate!.isAfter(now));
}

/// Packet statuses (for the filter), with the web app's labels.
const packetStatusOptions = <(String?, String)>[
  ('Available', 'Available'),
  ('Reserved', 'Reserved'),
  ('Issued', 'Issued'),
  ('Donated', 'Donated'),
  ('Expired', 'Expired'),
  (null, 'All'),
];
