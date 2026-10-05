import 'package:flutter/material.dart';

/// The web app's palette (Tailwind red / slate / emerald / amber / rose / blue).
class AppColors {
  const AppColors._();

  static const red600 = Color(0xFFDC2626);
  static const red700 = Color(0xFFB91C1C);
  static const red50 = Color(0xFFFEF2F2);
  static const slate50 = Color(0xFFF8FAFC);
  static const slate100 = Color(0xFFF1F5F9);
  static const slate200 = Color(0xFFE2E8F0);
  static const slate300 = Color(0xFFCBD5E1);
  static const slate400 = Color(0xFF94A3B8);
  static const slate500 = Color(0xFF64748B);
  static const slate600 = Color(0xFF475569);
  static const slate700 = Color(0xFF334155);
  static const slate800 = Color(0xFF1E293B);
  static const slate900 = Color(0xFF0F172A);
  static const slate950 = Color(0xFF020617);
  static const emerald600 = Color(0xFF059669);
  static const amber500 = Color(0xFFF59E0B);
  static const amber600 = Color(0xFFD97706);
  static const rose600 = Color(0xFFE11D48);
  static const blue600 = Color(0xFF2563EB);
  static const violet600 = Color(0xFF7C3AED);
}

class AppTheme {
  const AppTheme._();

  static ThemeData light() => _build(Brightness.light);
  static ThemeData dark() => _build(Brightness.dark);

  static ThemeData _build(Brightness brightness) {
    final dark = brightness == Brightness.dark;
    final scheme = ColorScheme.fromSeed(
      seedColor: AppColors.red600,
      brightness: brightness,
      primary: AppColors.red600,
      onPrimary: Colors.white,
      error: AppColors.rose600,
      surface: dark ? AppColors.slate900 : Colors.white,
      onSurface: dark ? AppColors.slate100 : AppColors.slate900,
      outline: dark ? AppColors.slate700 : AppColors.slate200,
      outlineVariant: dark ? AppColors.slate800 : AppColors.slate200,
      surfaceContainerHighest: dark ? AppColors.slate800 : AppColors.slate100,
    );
    final border = OutlineInputBorder(
      borderRadius: BorderRadius.circular(12),
      borderSide: BorderSide(color: dark ? AppColors.slate700 : AppColors.slate200),
    );

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      brightness: brightness,
      scaffoldBackgroundColor: dark ? AppColors.slate950 : AppColors.slate50,
      appBarTheme: AppBarTheme(
        backgroundColor: dark ? AppColors.slate900 : Colors.white,
        foregroundColor: dark ? AppColors.slate100 : AppColors.slate900,
        elevation: 0,
        scrolledUnderElevation: 1,
        centerTitle: false,
        titleTextStyle: TextStyle(
          fontSize: 18,
          fontWeight: FontWeight.w700,
          color: dark ? AppColors.slate100 : AppColors.slate900,
        ),
      ),
      cardTheme: CardThemeData(
        color: dark ? AppColors.slate900 : Colors.white,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16),
          side: BorderSide(color: dark ? AppColors.slate800 : AppColors.slate200),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: dark ? AppColors.slate800 : AppColors.slate50,
        border: border,
        enabledBorder: border,
        focusedBorder: border.copyWith(borderSide: const BorderSide(color: AppColors.red600, width: 1.5)),
        errorBorder: border.copyWith(borderSide: const BorderSide(color: AppColors.rose600)),
        contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: AppColors.red600,
          foregroundColor: Colors.white,
          minimumSize: const Size(48, 48),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: dark ? AppColors.slate100 : AppColors.slate700,
          side: BorderSide(color: dark ? AppColors.slate700 : AppColors.slate200),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(foregroundColor: dark ? const Color(0xFFF87171) : AppColors.red600),
      ),
      chipTheme: ChipThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        side: BorderSide(color: dark ? AppColors.slate700 : AppColors.slate200),
        selectedColor: AppColors.red600,
        secondarySelectedColor: AppColors.red600,
        checkmarkColor: Colors.white,
        labelStyle: TextStyle(fontSize: 13, color: dark ? AppColors.slate200 : AppColors.slate700),
        secondaryLabelStyle: const TextStyle(color: Colors.white),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: dark ? AppColors.slate900 : Colors.white,
        indicatorColor: dark ? const Color(0x33DC2626) : AppColors.red50,
        iconTheme: WidgetStateProperty.resolveWith(
          (s) => IconThemeData(color: s.contains(WidgetState.selected) ? AppColors.red600 : AppColors.slate500),
        ),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (s) => TextStyle(
            fontSize: 12,
            fontWeight: s.contains(WidgetState.selected) ? FontWeight.w700 : FontWeight.w500,
            color: s.contains(WidgetState.selected) ? AppColors.red600 : AppColors.slate500,
          ),
        ),
      ),
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: dark ? AppColors.slate900 : Colors.white,
        indicatorColor: dark ? const Color(0x33DC2626) : AppColors.red50,
        selectedIconTheme: const IconThemeData(color: AppColors.red600),
        selectedLabelTextStyle: const TextStyle(color: AppColors.red600, fontWeight: FontWeight.w700),
      ),
      dividerTheme: DividerThemeData(color: dark ? AppColors.slate800 : AppColors.slate200, space: 1),
      snackBarTheme: const SnackBarThemeData(behavior: SnackBarBehavior.floating),
      dialogTheme: DialogThemeData(
        backgroundColor: dark ? AppColors.slate900 : Colors.white,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(20)),
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: dark ? AppColors.slate900 : Colors.white,
        showDragHandle: true,
      ),
    );
  }
}
