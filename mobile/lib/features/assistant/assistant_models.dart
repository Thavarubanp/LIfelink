import '../../core/api/json.dart';

class AssistantSource {
  const AssistantSource({required this.title, this.publisher, this.url});

  factory AssistantSource.fromJson(Map<String, dynamic> json) =>
      AssistantSource(
        title: str(json['title']),
        publisher: json['publisher']?.toString(),
        url: json['url']?.toString(),
      );

  final String title;
  final String? publisher;
  final String? url;
}

class AssistantSegment {
  const AssistantSegment({
    required this.type,
    required this.text,
    this.sources = const [],
  });

  factory AssistantSegment.fromJson(Map<String, dynamic> json) =>
      AssistantSegment(
        type: str(json['type'], 'platform'),
        text: str(json['text']),
        sources: json['sources'] is List
            ? (json['sources'] as List)
                  .whereType<Map>()
                  .map(
                    (source) => AssistantSource.fromJson(
                      Map<String, dynamic>.from(source),
                    ),
                  )
                  .toList()
            : const [],
      );

  final String type;
  final String text;
  final List<AssistantSource> sources;
}

class AssistantAction {
  const AssistantAction({required this.label, required this.route});

  factory AssistantAction.fromJson(Map<String, dynamic> json) =>
      AssistantAction(label: str(json['label']), route: str(json['route']));

  final String label;
  final String route;
}

class AssistantReply {
  const AssistantReply({
    required this.reply,
    this.segments = const [],
    this.actions = const [],
  });

  factory AssistantReply.fromJson(Map<String, dynamic> json) => AssistantReply(
    reply: str(json['reply']),
    segments: json['segments'] is List
        ? (json['segments'] as List)
              .whereType<Map>()
              .map(
                (segment) => AssistantSegment.fromJson(
                  Map<String, dynamic>.from(segment),
                ),
              )
              .toList()
        : const [],
    actions: json['actions'] is List
        ? (json['actions'] as List)
              .whereType<Map>()
              .map(
                (action) =>
                    AssistantAction.fromJson(Map<String, dynamic>.from(action)),
              )
              .toList()
        : const [],
  );

  final String reply;
  final List<AssistantSegment> segments;
  final List<AssistantAction> actions;
}

class AssistantMessage {
  const AssistantMessage.user(this.text)
    : isUser = true,
      segments = const [],
      actions = const [];

  const AssistantMessage.assistant({
    required this.text,
    this.segments = const [],
    this.actions = const [],
  }) : isUser = false;

  final bool isUser;
  final String text;
  final List<AssistantSegment> segments;
  final List<AssistantAction> actions;

  String get historyContent {
    if (text.isNotEmpty) return text;
    return segments.map((segment) => segment.text).join('\n');
  }
}
