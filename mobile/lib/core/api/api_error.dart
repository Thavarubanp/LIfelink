import 'package:dio/dio.dart';

/// One error type for every failed API call, with the message to show (the API's own message when it sent one).
class ApiError implements Exception {
  const ApiError(this.message, {this.statusCode, this.fieldErrors = const {}, this.isNetwork = false});

  final String message;
  final int? statusCode;

  /// ASP.NET validation errors by field (camelCase), first message per field.
  final Map<String, String> fieldErrors;

  /// No answer from the server (no connection or timeout).
  final bool isNetwork;

  bool get isConflict => statusCode == 409;
  bool get isUnauthorized => statusCode == 401;
  bool get isForbidden => statusCode == 403;

  static const noConnection = 'No connection to the LifeLink server. Check your internet connection and try again.';
  static const timedOut = 'The server took too long to answer. Please try again.';

  /// Maps any error (Dio or not) to an [ApiError], mirroring the web app's errorUtils.
  static ApiError from(Object error) {
    if (error is ApiError) return error;
    if (error is! DioException) return ApiError(error.toString());

    switch (error.type) {
      case DioExceptionType.connectionTimeout:
      case DioExceptionType.sendTimeout:
      case DioExceptionType.receiveTimeout:
        return const ApiError(timedOut, isNetwork: true);
      case DioExceptionType.connectionError:
        return const ApiError(noConnection, isNetwork: true);
      case DioExceptionType.cancel:
        return const ApiError('The request was cancelled.');
      default:
        break;
    }

    final response = error.response;
    if (response == null) return const ApiError(noConnection, isNetwork: true);

    final status = response.statusCode;
    final data = response.data;
    final fields = _fieldErrors(data);

    if (status == 502 || status == 504 || (data is String && data.contains('<!DOCTYPE html>'))) {
      return ApiError(noConnection, statusCode: status, isNetwork: true);
    }

    String? message;
    if (data is String && data.trim().isNotEmpty) message = data.trim();
    if (data is Map) {
      final m = data['message'] ?? data['Message'];
      if (m is String && m.trim().isNotEmpty) message = m;
      if (message == null && status == 400) {
        final title = data['title'];
        message = fields.values.isNotEmpty ? fields.values.first : (title is String ? title : null);
      }
    }

    message ??= switch (status) {
      400 => 'Validation failed. Please check your inputs.',
      401 => 'Your session has ended. Please sign in again.',
      403 => 'Your account does not have permission to do this.',
      404 => 'Not found.',
      409 => 'Someone changed this at the same moment. The latest data has been loaded.',
      429 => 'Too many requests. Please wait a moment and try again.',
      _ => 'An unexpected server error occurred (HTTP $status).',
    };
    return ApiError(message, statusCode: status, fieldErrors: fields);
  }

  static Map<String, String> _fieldErrors(Object? data) {
    if (data is! Map) return const {};
    final raw = data['errors'] ?? data['Errors'];
    if (raw is! Map) return const {};
    final result = <String, String>{};
    raw.forEach((key, value) {
      var name = key.toString();
      if (name.contains('.')) name = name.split('.').last;
      if (name.isEmpty) return;
      name = name[0].toLowerCase() + name.substring(1);
      if (value is List && value.isNotEmpty) {
        result[name] = value.first.toString();
      } else if (value is String) {
        result[name] = value;
      }
    });
    return result;
  }

  @override
  String toString() => message;
}
