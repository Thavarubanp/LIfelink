# LifeLink mobile application

The Flutter Android app uses the same ASP.NET Core API and backend rules as the React client.

## Status by student step

| Step | Area | Status |
|---|---|---|
| 1 | Shared foundation, hospital inventory, packets/QR, transfers, emergencies, hospital donations, analysis | Done |
| 2 | Donor/patient requests, acceptance, screening, donations, complaints, appeals | In progress; final routes currently show stub screens |
| 3 | Hospital verification/doctor management and doctor decisions, screening reports, donation recording | Done |
| 4 | Administration, governance, activity, directories, messages, and phone notifications | Done |

## Setup and run

Install Flutter with Android tooling, then:

```powershell
cd mobile
flutter pub get
flutter analyze
flutter run
```

The compile-time setting is `API_BASE_URL` and must include `/api`.

For a USB-connected Android phone with debugging enabled:

```powershell
adb reverse tcp:5231 tcp:5231
flutter run --dart-define=API_BASE_URL=http://127.0.0.1:5231/api
```

For the Android emulator:

```powershell
flutter run --dart-define=API_BASE_URL=http://10.0.2.2:5231/api
```

## Build APKs

```powershell
flutter build apk --debug
flutter build apk --release --dart-define=API_BASE_URL=https://<api-host>/api
```

Outputs are under `build/app/outputs/flutter-apk/`.

## Architecture and packages

| Package | Use |
|---|---|
| `flutter_riverpod` | Testable providers and asynchronous screen state |
| `go_router` | Declarative routes, role shells, deep links, redirect guards |
| `dio` | JWT/activity/idempotency interceptors, timeouts, API errors |
| `flutter_secure_storage` | Token storage backed by Android secure storage |
| `mobile_scanner`, `qr_flutter` | Scan and display packet tracking QR codes |
| `file_picker`, `image_picker` | Select documents/images and take attachment photos |
| `open_filex`, `path_provider` | Open downloaded attachments with device applications |
| `flutter_local_notifications` | Local notifications for newly polled LifeLink events |
| `workmanager` | Periodic Android polling while the app is closed |
| `url_launcher` | Call or email donors through installed device apps |
| `shared_preferences` | Theme and notification last-seen markers |

`lib/core/` contains API, auth, configuration, notification, routing, theme, utility, and shared-widget foundations. `lib/features/` is organized by business area. The router guards signed-out, suspended, unapproved-hospital, temporary-password, and wrong-role states in that order. Riverpod repositories call the API and expose loading/error/data state.

Device behavior includes packet QR scanning, optional camera/file attachments, phone/email links, local notifications, and periodic background polling. Background calls do not send the activity header and therefore do not keep an idle session alive.

## Tests

```powershell
flutter analyze
flutter test
```

Tests are grouped under `test/unit`, `test/widget`, `test/navigation`, and `test/integration`. HTTP integrations use mocked adapters.

## Android permissions

| Permission/capability | Reason |
|---|---|
| `INTERNET` | API requests |
| `CAMERA` | QR scanning and optional attachment photos |
| `POST_NOTIFICATIONS` | Android 13+ local notification permission |
| Package visibility queries | Open files, dial phone numbers, compose email, capture images |

Plain HTTP is permitted only by the debug configuration for local loopback/emulator hosts. Release builds use HTTPS.

See the [complete technical documentation](../docs/README.md#mobile-application).

