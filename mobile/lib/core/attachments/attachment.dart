import 'dart:convert';
import 'dart:typed_data';

/// Files travel to and from the API as data URLs (`data:<type>;base64,<payload>`), exactly as in the web app.
/// Limits are the web app's: 2 MB, and the types its file inputs accept.
class AttachmentRules {
  const AttachmentRules._();

  static const maxBytes = 2 * 1024 * 1024;
  static const allowedExtensions = ['pdf', 'doc', 'docx', 'png', 'jpg', 'jpeg'];
  static const emptyFileMessage = 'The selected file is empty. Please choose a file that has content.';
  static const tooLargeMessage = 'Files cannot exceed 2 MB.';

  static const mimeTypes = {
    'pdf': 'application/pdf',
    'doc': 'application/msword',
    'docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    'png': 'image/png',
    'jpg': 'image/jpeg',
    'jpeg': 'image/jpeg',
  };

  static String extensionOf(String name) {
    final dot = name.lastIndexOf('.');
    return dot < 0 ? '' : name.substring(dot + 1).toLowerCase();
  }

  /// Why a file cannot be attached, or null when it can.
  static String? check(String name, int length) {
    if (!allowedExtensions.contains(extensionOf(name))) {
      return 'This file type is not allowed. Use PDF, Word (DOC, DOCX) or an image (PNG, JPG).';
    }
    if (length == 0) return emptyFileMessage;
    if (length > maxBytes) return tooLargeMessage;
    return null;
  }
}

class AttachmentException implements Exception {
  const AttachmentException(this.message);
  final String message;

  @override
  String toString() => message;
}

/// One attachment: its file name and data URL.
class Attachment {
  const Attachment({required this.name, required this.url});

  /// Validates and encodes a picked file. Throws [AttachmentException] with a readable message.
  factory Attachment.fromBytes(String name, Uint8List bytes) {
    final problem = AttachmentRules.check(name, bytes.length);
    if (problem != null) throw AttachmentException(problem);
    final mime = AttachmentRules.mimeTypes[AttachmentRules.extensionOf(name)]!;
    return Attachment(name: name, url: 'data:$mime;base64,${base64Encode(bytes)}');
  }

  /// An attachment stored by the API (null when there is none or it is empty).
  static Attachment? fromApi(String? url, String? name) {
    if (url == null || !hasContent(url)) return null;
    return Attachment(name: (name == null || name.isEmpty) ? 'Attachment' : name, url: url);
  }

  final String name;
  final String url;

  /// A data URL with an empty payload counts as "no file" (same rule as the web app).
  static bool hasContent(String url) {
    if (!url.startsWith('data:')) return url.trim().isNotEmpty;
    final comma = url.indexOf(',');
    return comma >= 0 && url.substring(comma + 1).trim().isNotEmpty;
  }

  bool get isDataUrl => url.startsWith('data:');

  String get mimeType {
    if (isDataUrl) {
      final end = url.indexOf(';');
      if (end > 5) return url.substring(5, end);
    }
    return AttachmentRules.mimeTypes[AttachmentRules.extensionOf(name)] ?? 'application/octet-stream';
  }

  bool get isImage => mimeType.startsWith('image/');

  /// The file's bytes (null when the URL is not a readable data URL).
  Uint8List? get bytes {
    if (!isDataUrl) return null;
    final comma = url.indexOf(',');
    if (comma < 0) return null;
    try {
      return base64Decode(url.substring(comma + 1));
    } catch (_) {
      return null;
    }
  }

  /// Size of the encoded attachment for display.
  String get sizeLabel {
    final n = bytes?.length ?? 0;
    return n >= 1024 * 1024 ? '${(n / (1024 * 1024)).toStringAsFixed(1)} MB' : '${(n / 1024).ceil()} KB';
  }
}
