import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/attachments/attachment.dart';
import '../../core/providers.dart';
import '../admin/complaints/admin_complaints.dart';
import '../blood_requests/blood_request_models.dart';

class ComplaintUserChoice {
  const ComplaintUserChoice(this.id, this.name, this.subtitle, this.extraInfo, this.status);
  factory ComplaintUserChoice.fromJson(Map<String, dynamic> j) => ComplaintUserChoice(
    str(j['id']), str(j['displayName']), str(j['subText'], 'Registered platform user'),
    str(j['extraInfo']), str(j['status']));
  final String id, name, subtitle, extraInfo, status;
}

class ComplaintsRepository {
  ComplaintsRepository(this._api);
  final ApiClient _api;
  Future<List<Complaint>> mine() async => unwrapList(await _api.get('/Complaints/my-complaints'))
      .map(Complaint.fromJson).toList();
  Future<void> create({required String type, required String subject, required String description,
    String? hospitalId, String? targetUserId, required String idempotencyKey}) => _api.post('/Complaints', body: {
      'complaintType': type, 'subject': subject, 'description': description,
      'hospitalId': hospitalId, 'targetUserId': targetUserId,
    }, options: apiOptions(idempotencyKey: idempotencyKey));
  Future<void> reply(String id, String notes, Attachment? attachment) => _api.post('/Complaints/$id/reply', body: {
    'notes': notes, 'attachmentUrl': attachment?.url, 'attachmentName': attachment?.name,
  });
  Future<void> solve(String id, String notes) => _api.put('/Complaints/$id/solve',
    body: {'notes': notes.trim().isEmpty ? null : notes.trim()});
  Future<void> delete(String id) => _api.put('/Complaints/$id/cancel');
  Future<List<HospitalChoice>> hospitals() async => unwrapList(await _api.get('/Hospitals'))
      .map(HospitalChoice.fromJson).toList();
  Future<List<ComplaintUserChoice>> users(String query) async {
    final data = unwrapMap(await _api.get('/search', query: {'q': query}));
    final users = data['users'];
    return users is List ? users.whereType<Map>().map((e) => ComplaintUserChoice.fromJson(Map<String, dynamic>.from(e)))
        .where((u) => u.id.isNotEmpty).toList() : const [];
  }
}

final complaintsRepositoryProvider = Provider<ComplaintsRepository>((ref) => ComplaintsRepository(ref.watch(apiClientProvider)));
final myComplaintsProvider = FutureProvider.autoDispose<List<Complaint>>((ref) async {
  final list = await ref.watch(complaintsRepositoryProvider).mine();
  list.sort((a, b) => (b.createdAt ?? DateTime(0)).compareTo(a.createdAt ?? DateTime(0)));
  return list;
});
final complaintHospitalsProvider = FutureProvider.autoDispose<List<HospitalChoice>>(
  (ref) => ref.watch(complaintsRepositoryProvider).hospitals());
final complaintUsersProvider = FutureProvider.autoDispose.family<List<ComplaintUserChoice>, String>((ref, query) =>
  query.trim().length < 2 ? const [] : ref.watch(complaintsRepositoryProvider).users(query.trim()));
