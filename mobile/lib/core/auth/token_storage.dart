import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Keeps the JWT in the Android Keystore-backed secure storage, with an in-memory copy for each request.
abstract class TokenStorage {
  String? get token;
  Future<String?> load();
  Future<void> save(String token);
  Future<void> clear();
}

class SecureTokenStorage implements TokenStorage {
  SecureTokenStorage([FlutterSecureStorage? storage]) : _storage = storage ?? const FlutterSecureStorage();

  static const _key = 'lifelink_token';
  final FlutterSecureStorage _storage;
  String? _token;

  @override
  String? get token => _token;

  @override
  Future<String?> load() async {
    try {
      _token = await _storage.read(key: _key);
    } catch (_) {
      _token = null; // unreadable storage (e.g. restored backup): sign in again
    }
    return _token;
  }

  @override
  Future<void> save(String token) async {
    _token = token;
    await _storage.write(key: _key, value: token);
  }

  @override
  Future<void> clear() async {
    _token = null;
    try {
      await _storage.delete(key: _key);
    } catch (_) {
      // nothing stored
    }
  }
}

/// In-memory storage for tests.
class MemoryTokenStorage implements TokenStorage {
  MemoryTokenStorage([this._token]);

  String? _token;

  @override
  String? get token => _token;

  @override
  Future<String?> load() async => _token;

  @override
  Future<void> save(String token) async => _token = token;

  @override
  Future<void> clear() async => _token = null;
}
