/// Build-time configuration.
///
/// The API base URL (including `/api`) comes from `--dart-define=API_BASE_URL=...`:
/// - USB phone (default): `http://127.0.0.1:5231/api` after `adb reverse tcp:5231 tcp:5231`
/// - Android emulator: `http://10.0.2.2:5231/api`
/// - Release APK: the deployed API, e.g. `https://<app>.onrender.com/api`
class Env {
  const Env._();

  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://127.0.0.1:5231/api',
  );
}
