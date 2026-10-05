import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'api/api_client.dart';
import 'auth/auth_repository.dart';
import 'auth/session_activity.dart';
import 'auth/token_storage.dart';

/// Shared infrastructure. Tests override these with in-memory or mocked versions.
final tokenStorageProvider = Provider<TokenStorage>((ref) => SecureTokenStorage());

final sessionActivityProvider = Provider<SessionActivity>((ref) => SessionActivity());

final apiClientProvider = Provider<ApiClient>(
  (ref) => ApiClient(tokens: ref.watch(tokenStorageProvider), activity: ref.watch(sessionActivityProvider)),
);

final authRepositoryProvider = Provider<AuthRepository>((ref) => AuthRepository(ref.watch(apiClientProvider)));
