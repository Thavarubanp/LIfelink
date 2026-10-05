/// Helpers for the API's two response shapes: `{ success, message, data }` (ApiResponse) or the data itself.
library;

/// The payload of a response: `data` when the body is an ApiResponse wrapper, otherwise the body.
Object? unwrap(Object? body) {
  if (body is Map && body.containsKey('success') && body.containsKey('data')) return body['data'];
  return body;
}

/// A list payload (empty when missing).
List<Map<String, dynamic>> unwrapList(Object? body) {
  final data = unwrap(body);
  if (data is List) return data.whereType<Map>().map((e) => Map<String, dynamic>.from(e)).toList();
  return const [];
}

/// An object payload (empty when missing).
Map<String, dynamic> unwrapMap(Object? body) {
  final data = unwrap(body);
  return data is Map ? Map<String, dynamic>.from(data) : <String, dynamic>{};
}

/// Parses an API timestamp. Times without an offset are UTC (the API stores UTC).
DateTime? parseDate(Object? value) {
  if (value is! String || value.isEmpty) return null;
  final hasOffset = value.endsWith('Z') || RegExp(r'[+-]\d\d:\d\d$').hasMatch(value);
  final parsed = DateTime.tryParse(hasOffset ? value : '${value}Z');
  return parsed?.toUtc();
}

String str(Object? value, [String fallback = '']) => value?.toString() ?? fallback;

int intOf(Object? value, [int fallback = 0]) =>
    value is int ? value : value is num ? value.toInt() : int.tryParse('$value') ?? fallback;

double doubleOf(Object? value, [double fallback = 0]) =>
    value is num ? value.toDouble() : double.tryParse('$value') ?? fallback;

bool boolOf(Object? value) => value == true || value == 'true';

List<String> stringList(Object? value) => value is List ? value.map((e) => e.toString()).toList() : const [];
