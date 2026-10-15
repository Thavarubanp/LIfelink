import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../admin/attention_controller.dart';
import '../blood_requests/blood_request_models.dart';
import '../blood_requests/blood_request_repository.dart';
import '../inventory/inventory_repository.dart';
import '../inventory/inventory_models.dart';
import '../profile/profile_repository.dart';
import '../screening/screening_repository.dart';
import '../verification/verification_repository.dart';
import 'role_attention_repository.dart';

enum TabAttentionKey {
  donorCreate,
  donorDonations,
  hospitalHome,
  hospitalInventory,
  hospitalTransfers,
  doctorRequests,
  doctorReports,
}

class TabAttentionState {
  const TabAttentionState({
    this.donorCreate = 0,
    this.donorDonations = 0,
    this.hospitalHome = 0,
    this.hospitalInventory = 0,
    this.hospitalTransfers = 0,
    this.doctorRequests = 0,
    this.doctorReports = 0,
  });

  final int donorCreate;
  final int donorDonations;
  final int hospitalHome;
  final int hospitalInventory;
  final int hospitalTransfers;
  final int doctorRequests;
  final int doctorReports;

  TabAttentionState copyWith({
    int? donorCreate,
    int? donorDonations,
    int? hospitalHome,
    int? hospitalInventory,
    int? hospitalTransfers,
    int? doctorRequests,
    int? doctorReports,
  }) => TabAttentionState(
    donorCreate: donorCreate ?? this.donorCreate,
    donorDonations: donorDonations ?? this.donorDonations,
    hospitalHome: hospitalHome ?? this.hospitalHome,
    hospitalInventory: hospitalInventory ?? this.hospitalInventory,
    hospitalTransfers: hospitalTransfers ?? this.hospitalTransfers,
    doctorRequests: doctorRequests ?? this.doctorRequests,
    doctorReports: doctorReports ?? this.doctorReports,
  );

  int count(TabAttentionKey key) => switch (key) {
    TabAttentionKey.donorCreate => donorCreate,
    TabAttentionKey.donorDonations => donorDonations,
    TabAttentionKey.hospitalHome => hospitalHome,
    TabAttentionKey.hospitalInventory => hospitalInventory,
    TabAttentionKey.hospitalTransfers => hospitalTransfers,
    TabAttentionKey.doctorRequests => doctorRequests,
    TabAttentionKey.doctorReports => doctorReports,
  };
}

int donorActionCount(Iterable<DonorAcceptance> acceptances) => acceptances
    .where(
      (a) =>
          !a.requestDeleted &&
          !a.requestSuspended &&
          const {'Accepted', 'ScreeningPending', 'Verified'}.contains(a.status),
    )
    .length;

class DonorRequestMarkerResult {
  const DonorRequestMarkerResult(this.changed, this.marker);

  final int changed;
  final Map<String, String> marker;
}

/// New requests establish their own baseline; only a known request changing
/// status is badged, and its old marker is retained until the tab is viewed.
DonorRequestMarkerResult reconcileDonorRequestStatuses(
  Map<String, String>? before,
  Map<String, String> current,
) {
  if (before == null) return DonorRequestMarkerResult(0, current);
  var changed = 0;
  final retained = <String, String>{};
  for (final entry in current.entries) {
    final old = before[entry.key];
    if (old == null) {
      retained[entry.key] = entry.value;
    } else {
      retained[entry.key] = old;
      if (old != entry.value) changed++;
    }
  }
  return DonorRequestMarkerResult(changed, retained);
}

final tabAttentionProvider =
    NotifierProvider<TabAttentionController, TabAttentionState>(
      TabAttentionController.new,
    );

final tabAttentionCountProvider = Provider.family<int, TabAttentionKey>(
  (ref, key) => ref.watch(tabAttentionProvider).count(key),
);

/// One role-wide polling cycle supplies every actionable tab badge.
class TabAttentionController extends Notifier<TabAttentionState> {
  Timer? _timer;
  String? _hospitalId;
  List<BloodRequest> _lastDonorRequests = const [];
  bool _refreshing = false;
  bool _refreshPending = false;

  @override
  TabAttentionState build() {
    final auth = ref.watch(authControllerProvider);
    _timer?.cancel();
    ref.onDispose(() => _timer?.cancel());
    _hospitalId = null;
    _lastDonorRequests = const [];

    if (!auth.isSignedIn || auth.user!.isSuspended) {
      return const TabAttentionState();
    }

    Future.microtask(refresh);
    _timer = Timer.periodic(AppConstants.unreadPollInterval, (_) => refresh());
    return const TabAttentionState();
  }

  Future<void> refresh() async {
    if (_refreshing) {
      _refreshPending = true;
      return;
    }
    final auth = ref.read(authControllerProvider);
    final user = auth.user;
    if (!auth.isSignedIn || user == null || user.isSuspended) return;
    _refreshing = true;
    try {
      switch (user.primaryRole) {
        case Roles.admin:
          await ref.read(adminAttentionProvider.notifier).refresh();
          return;
        case Roles.hospitalStaff:
          await _refreshHospital();
          return;
        case Roles.doctor:
          await _refreshDoctor();
          return;
        default:
          await _refreshDonor(user.userId);
          return;
      }
    } finally {
      _refreshing = false;
      if (_refreshPending && ref.mounted) {
        _refreshPending = false;
        Future.microtask(refresh);
      }
    }
  }

  Future<void> _refreshDonor(String userId) async {
    final requestsFuture = ref
        .read(bloodRequestRepositoryProvider)
        .myRequests(background: true);
    final acceptancesFuture = ref
        .read(screeningRepositoryProvider)
        .myAcceptances(background: true);

    try {
      final requests = await requestsFuture;
      _lastDonorRequests = requests;
      final changed = await _donorRequestChanges(userId, requests);
      if (ref.mounted) state = state.copyWith(donorCreate: changed);
    } catch (_) {
      // Keep the last successful count.
    }
    try {
      final acceptances = await acceptancesFuture;
      if (ref.mounted) {
        state = state.copyWith(donorDonations: donorActionCount(acceptances));
      }
    } catch (_) {
      // Keep the last successful count.
    }
  }

  Future<void> _refreshHospital() async {
    final attentionFuture = ref
        .read(roleAttentionRepositoryProvider)
        .get(background: true);
    final inventoryFuture = _hospitalInventory();
    try {
      final attention = await attentionFuture;
      if (ref.mounted) {
        state = state.copyWith(
          hospitalHome: attention.pendingHospitalVerifications,
          hospitalTransfers: attention.pendingTransferResponses,
        );
      }
    } catch (_) {
      // Keep the last successful counts.
    }
    try {
      final inventory = await inventoryFuture;
      if (ref.mounted) {
        state = state.copyWith(
          hospitalInventory: inventory.where((i) => i.isLowStock).length,
        );
      }
    } catch (_) {
      // Keep the last successful count.
    }
  }

  Future<List<InventoryItem>> _hospitalInventory() async {
    _hospitalId ??=
        (await ref.read(profileRepositoryProvider).me(background: true)).id;
    return ref
        .read(inventoryRepositoryProvider)
        .forHospital(_hospitalId!, background: true);
  }

  Future<void> _refreshDoctor() async {
    final attentionFuture = ref
        .read(roleAttentionRepositoryProvider)
        .get(background: true);
    final requestsFuture = ref
        .read(verificationRepositoryProvider)
        .assignedRequests(background: true);
    try {
      final attention = await attentionFuture;
      if (ref.mounted) {
        state = state.copyWith(
          doctorReports: attention.pendingScreeningReviews,
        );
      }
    } catch (_) {
      // Keep the last successful count.
    }
    try {
      final requests = await requestsFuture;
      if (ref.mounted) {
        state = state.copyWith(
          doctorRequests: requests.where((r) => r.canDoctorDecide).length,
        );
      }
    } catch (_) {
      // Keep the last successful count.
    }
  }

  Future<int> _donorRequestChanges(
    String userId,
    List<BloodRequest> requests,
  ) async {
    final prefs = await SharedPreferences.getInstance();
    final key = 'tab_attention_create_$userId';
    final saved = prefs.getString(key);
    final current = {for (final r in requests) r.id: r.status};
    if (saved == null) {
      final result = reconcileDonorRequestStatuses(null, current);
      await prefs.setString(key, jsonEncode(result.marker));
      return result.changed;
    }

    Map<String, String> before;
    try {
      final decoded = jsonDecode(saved) as Map<String, dynamic>;
      before = decoded.map((key, value) => MapEntry(key, '$value'));
    } catch (_) {
      before = <String, String>{};
    }
    final result = reconcileDonorRequestStatuses(before, current);
    await prefs.setString(key, jsonEncode(result.marker));
    return result.changed;
  }

  Future<void> markSeen(TabAttentionKey? key) async {
    if (key != TabAttentionKey.donorCreate) return;
    final user = ref.read(authControllerProvider).user;
    if (user == null) return;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(
      'tab_attention_create_${user.userId}',
      jsonEncode({for (final r in _lastDonorRequests) r.id: r.status}),
    );
    if (ref.mounted && state.donorCreate != 0) {
      state = state.copyWith(donorCreate: 0);
    }
  }
}
