import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';

class SearchItem {
  const SearchItem({
    required this.resultType,
    required this.id,
    required this.displayName,
    required this.subText,
    required this.extraInfo,
    required this.avatarInitial,
    required this.status,
  });

  factory SearchItem.fromJson(Map<String, dynamic> j) => SearchItem(
    resultType: str(j['resultType']),
    id: str(j['id']),
    displayName: str(j['displayName']),
    subText: str(j['subText']),
    extraInfo: j['extraInfo']?.toString(),
    avatarInitial: j['avatarInitial']?.toString(),
    status: j['status']?.toString(),
  );

  final String resultType, id, displayName, subText;
  final String? extraInfo, avatarInitial, status;
}

class SearchResults {
  const SearchResults({
    required this.hospitals,
    required this.doctors,
    required this.users,
  });
  factory SearchResults.fromJson(Map<String, dynamic> j) => SearchResults(
    hospitals: _items(j['hospitals']),
    doctors: _items(j['doctors']),
    users: _items(j['users']),
  );
  final List<SearchItem> hospitals, doctors, users;
  int get totalCount => hospitals.length + doctors.length + users.length;

  static List<SearchItem> _items(dynamic value) => value is List
      ? value
            .whereType<Map>()
            .map((e) => SearchItem.fromJson(Map<String, dynamic>.from(e)))
            .toList()
      : const [];
}

class SearchRepository {
  const SearchRepository(this._api);
  final ApiClient _api;
  Future<SearchResults> search(String query) async => SearchResults.fromJson(
    unwrapMap(await _api.get('/search', query: {'q': query.trim()})),
  );
}

final searchRepositoryProvider = Provider<SearchRepository>(
  (ref) => SearchRepository(ref.watch(apiClientProvider)),
);
final globalSearchProvider = FutureProvider.autoDispose
    .family<SearchResults, String>(
      (ref, query) => ref.watch(searchRepositoryProvider).search(query),
    );
