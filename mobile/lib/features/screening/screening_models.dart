import '../../core/api/json.dart';

const screeningUnknown = 'Donor doesn\'t remember';
const screeningConfirmId = 'CONFIRM_TRUE';

class ScreeningPart {
  const ScreeningPart(this.id, this.label, this.type, this.options, this.required, this.allowUnknown,
      this.allowNone, this.femaleOnly, this.showIf, this.defaultValue, this.min, this.max);
  factory ScreeningPart.fromJson(Map<String, dynamic> j) => ScreeningPart(
      str(j['id']), str(j['label']), str(j['type'], 'text'), stringList(j['options']), j['required'] != false,
      boolOf(j['allow_unknown']), boolOf(j['allow_none']), boolOf(j['female_only']),
      j['show_if'] is Map ? Map<String, dynamic>.from(j['show_if'] as Map) : null, j['default'],
      j['min'] is num ? (j['min'] as num).toDouble() : null, j['max'] is num ? (j['max'] as num).toDouble() : null);
  final String id, label, type;
  final List<String> options;
  final bool required, allowUnknown, allowNone, femaleOnly;
  final Map<String, dynamic>? showIf;
  final Object? defaultValue;
  final double? min, max;
}

class ScreeningQuestion {
  const ScreeningQuestion(this.id, this.number, this.title, this.text, this.type, this.confidential,
      this.followUp, this.missing, this.parts);
  factory ScreeningQuestion.fromJson(Map<String, dynamic> j) => ScreeningQuestion(
      str(j['question_id']), intOf(j['number']), str(j['title']), str(j['text']), str(j['type']),
      boolOf(j['confidential']), boolOf(j['follow_up']), stringList(j['missing']), j['parts'] is List
          ? (j['parts'] as List).whereType<Map>().map((e) => ScreeningPart.fromJson(Map<String, dynamic>.from(e))).toList()
          : const []);
  final String id, title, text, type;
  final int number;
  final bool confidential, followUp;
  final List<String> missing;
  final List<ScreeningPart> parts;
}

class ScreeningSession {
  const ScreeningSession(this.status, this.isComplete, this.sectionCount, this.sectionIndex, this.section,
      this.answered, this.total, this.question, this.transcript);
  factory ScreeningSession.fromJson(Map<String, dynamic> j) => ScreeningSession(
      str(j['status']), boolOf(j['isComplete']), intOf(j['sectionCount'], 7), intOf(j['sectionIndex'], 1),
      str(j['section']), intOf(j['answered']), intOf(j['total']), j['question'] is Map
          ? ScreeningQuestion.fromJson(Map<String, dynamic>.from(j['question'] as Map)) : null,
      j['transcript'] is List ? (j['transcript'] as List).whereType<Map>()
          .map((e) => (question: str(e['question']), answer: str(e['answer']))).toList() : const []);
  final String status, section;
  final bool isComplete;
  final int sectionCount, sectionIndex, answered, total;
  final ScreeningQuestion? question;
  final List<({String question, String answer})> transcript;
}

class AssistantReply {
  const AssistantReply(this.reply, this.screening);
  factory AssistantReply.fromJson(Map<String, dynamic> j) => AssistantReply(str(j['reply']), j['screening'] is Map
      ? ScreeningSession.fromJson(Map<String, dynamic>.from(j['screening'] as Map)) : null);
  final String reply;
  final ScreeningSession? screening;
}

bool screeningPartApplies(ScreeningPart part, Map<String, dynamic> values) {
  if (part.femaleOnly && '${values['P_GENDER']}'.toLowerCase() != 'female') return false;
  final rule = part.showIf;
  if (rule == null) return true;
  final parent = values[rule['field']];
  if (rule.containsKey('equals')) return parent == rule['equals'];
  return parent is List && parent.contains(rule['includes']);
}

bool screeningAnswered(ScreeningPart part, Object? value) {
  if (part.type == 'checklist') {
    return value is List;
  }
  if (part.type == 'trips') {
    return value is List && value.isNotEmpty &&
        value.every((t) => t is Map && '${t['country']}'.isNotEmpty && '${t['return_date']}'.isNotEmpty);
  }
  return value != null && '$value'.trim().isNotEmpty;
}

Map<String, dynamic> collectScreeningAnswers(List<ScreeningPart> parts, Map<String, dynamic> values) => {
  for (final part in parts)
    if (screeningPartApplies(part, values) && screeningAnswered(part, values[part.id])) part.id: values[part.id],
};

String summariseScreeningAnswers(List<ScreeningPart> parts, Map<String, dynamic> values) => parts
    .where((p) => p.type != 'confirm' && screeningPartApplies(p, values) && screeningAnswered(p, values[p.id]))
    .map((p) {
      final value = values[p.id];
      final text = p.type == 'checklist' ? ((value as List).isEmpty ? 'None of these' : value.join(', '))
          : p.type == 'trips' ? (value as List).map((t) => '${t['country']} (back ${t['return_date']})').join('; ') : '$value';
      return '${p.label}: $text';
    }).join('\n');
