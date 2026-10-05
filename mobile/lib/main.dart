import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app.dart';
import 'core/notifications/phone_notifications.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  // Phone notifications (and the notification that opened the app, if any)
  try {
    await PhoneNotifications.instance.init();
  } catch (_) {
    // The app works without phone notifications
  }
  // Failed loads show an error with a Retry button instead of retrying automatically in the background
  runApp(ProviderScope(retry: (_, _) => null, child: const LifeLinkApp()));
}
