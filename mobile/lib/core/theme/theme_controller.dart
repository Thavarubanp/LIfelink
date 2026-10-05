import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

final themeModeProvider = NotifierProvider<ThemeController, ThemeMode>(ThemeController.new);

/// Light / dark / system theme, remembered on the phone (not secret, so plain preferences).
class ThemeController extends Notifier<ThemeMode> {
  static const _key = 'lifelink_theme';

  @override
  ThemeMode build() {
    Future.microtask(_load);
    return ThemeMode.system;
  }

  Future<void> _load() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      state = ThemeMode.values.firstWhere((m) => m.name == prefs.getString(_key), orElse: () => ThemeMode.system);
    } catch (_) {
      // Preferences unavailable: follow the system
    }
  }

  Future<void> set(ThemeMode mode) async {
    state = mode;
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setString(_key, mode.name);
    } catch (_) {
      // Not remembered, still applied
    }
  }
}
