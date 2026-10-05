import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  // Failed loads show an error with a Retry button instead of retrying automatically in the background
  runApp(ProviderScope(retry: (_, _) => null, child: const LifeLinkApp()));
}
