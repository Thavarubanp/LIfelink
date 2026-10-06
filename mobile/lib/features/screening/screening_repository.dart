import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/json.dart';
import '../../core/providers.dart';
import '../blood_requests/blood_request_models.dart';
import 'screening_models.dart';

class ScreeningRepository {
  ScreeningRepository(this._api);
  final ApiClient _api;

  Future<AssistantReply> chat(
    String acceptanceId, {
    String message = '',
    Map<String, dynamic>? structured,
  }) async => AssistantReply.fromJson(
    unwrapMap(
      await _api.post(
        '/assistant/chat',
        body: {
          'message': message,
          'history': const [],
          'acceptanceId': acceptanceId,
          'structured': structured,
        },
        options: apiOptions(receiveTimeout: const Duration(seconds: 70)),
      ),
    ),
  );
  Future<List<DonorAcceptance>> myAcceptances() async =>
      unwrapList(await _api.get('/Acceptances/my'))
          .map(DonorAcceptance.fromJson)
          .toList();
  Future<void> withdraw(String id) => _api.put('/Acceptances/$id/cancel');
  Future<List<DonorAcceptance>> activeWithdrawals() async =>
      unwrapList(await _api.get('/Acceptances/my-active-withdrawals'))
          .map(DonorAcceptance.fromJson)
          .toList();
  Future<void> suspendedWithdraw(String id) =>
      _api.put('/Acceptances/$id/suspended-withdraw');
  Future<ScreeningAnswersData> answers(String id) async =>
      ScreeningAnswersData.fromJson(
        unwrapMap(await _api.get('/Acceptances/$id/screening-answers')),
      );
  Future<void> updateAnswers(String id, Map<String, dynamic> answers) =>
      _api.put(
        '/Acceptances/$id/screening-answers',
        body: {'answers': answers},
        options: apiOptions(receiveTimeout: const Duration(seconds: 40)),
      );
  Future<void> reopen(String id) => _api.put(
    '/Acceptances/$id/status',
    query: {'status': 'ScreeningPending'},
  );
}

class ScreeningAnswerItem {
  const ScreeningAnswerItem(this.question, this.answer);
  factory ScreeningAnswerItem.fromJson(Map<String, dynamic> j) =>
      ScreeningAnswerItem(str(j['question']), str(j['answer']));
  final String question, answer;
}

class ScreeningAnswerSection {
  const ScreeningAnswerSection(
    this.index,
    this.title,
    this.confidential,
    this.items,
  );
  factory ScreeningAnswerSection.fromJson(Map<String, dynamic> j) =>
      ScreeningAnswerSection(
        intOf(j['index']),
        str(j['title']),
        boolOf(j['confidential']),
        j['items'] is List
            ? (j['items'] as List)
                  .whereType<Map>()
                  .map(
                    (e) => ScreeningAnswerItem.fromJson(
                      Map<String, dynamic>.from(e),
                    ),
                  )
                  .toList()
            : const [],
      );
  final int index;
  final String title;
  final bool confidential;
  final List<ScreeningAnswerItem> items;
}

class ScreeningAnswersData {
  const ScreeningAnswersData(
    this.acceptanceId,
    this.reportVersion,
    this.status,
    this.submittedAt,
    this.canEdit,
    this.editUnavailableReason,
    this.questionnaire,
    this.answers,
    this.sections,
  );
  factory ScreeningAnswersData.fromJson(Map<String, dynamic> j) =>
      ScreeningAnswersData(
        str(j['acceptanceId']),
        intOf(j['reportVersion']),
        str(j['status']),
        parseDate(j['submittedAt']),
        boolOf(j['canEdit']),
        j['editUnavailableReason']?.toString(),
        j['questionnaire'] is List
            ? (j['questionnaire'] as List)
                  .whereType<Map>()
                  .map(
                    (e) => ScreeningQuestion.fromJson(
                      Map<String, dynamic>.from(e),
                    ),
                  )
                  .toList()
            : null,
        j['answers'] is Map
            ? Map<String, dynamic>.from(j['answers'] as Map)
            : const {},
        j['sections'] is List
            ? (j['sections'] as List)
                  .whereType<Map>()
                  .map(
                    (e) => ScreeningAnswerSection.fromJson(
                      Map<String, dynamic>.from(e),
                    ),
                  )
                  .toList()
            : const [],
      );
  final String acceptanceId, status;
  final int reportVersion;
  final DateTime? submittedAt;
  final bool canEdit;
  final String? editUnavailableReason;
  final List<ScreeningQuestion>? questionnaire;
  final Map<String, dynamic> answers;
  final List<ScreeningAnswerSection> sections;
}

final screeningRepositoryProvider = Provider<ScreeningRepository>(
  (ref) => ScreeningRepository(ref.watch(apiClientProvider)),
);

final myAcceptancesProvider = FutureProvider.autoDispose<List<DonorAcceptance>>(
  (ref) async {
    final list = await ref.watch(screeningRepositoryProvider).myAcceptances();
    list.sort(
      (a, b) =>
          (b.acceptedAt ?? DateTime(0)).compareTo(a.acceptedAt ?? DateTime(0)),
    );
    return list;
  },
);

final screeningAnswersProvider = FutureProvider.autoDispose
    .family<ScreeningAnswersData, String>(
      (ref, id) => ref.watch(screeningRepositoryProvider).answers(id),
    );

final activeWithdrawalProvider =
    FutureProvider.autoDispose<List<DonorAcceptance>>((ref) async {
      final list = await ref
          .watch(screeningRepositoryProvider)
          .activeWithdrawals();
      list.sort(
        (a, b) => (b.acceptedAt ?? DateTime(0)).compareTo(
          a.acceptedAt ?? DateTime(0),
        ),
      );
      return list;
    });
