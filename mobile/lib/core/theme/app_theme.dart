import 'package:flutter/material.dart';

/// The web app's palette (Tailwind red / slate / emerald / amber / rose / blue).
class AppColors {
  const AppColors._();

  static const red600 = Color(0xFFDC2626);
  static const red500 = Color(0xFFEF4444);
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
  static const emerald300 = Color(0xFF6EE7B7);
  static const emerald700 = Color(0xFF047857);
  static const amber500 = Color(0xFFF59E0B);
  static const amber600 = Color(0xFFD97706);
  static const amber300 = Color(0xFFFCD34D);
  static const amber800 = Color(0xFF92400E);
  static const rose600 = Color(0xFFE11D48);
  static const rose300 = Color(0xFFFDA4AF);
  static const rose700 = Color(0xFFBE123C);
  static const blue600 = Color(0xFF2563EB);
  static const blue300 = Color(0xFF93C5FD);
  static const blue700 = Color(0xFF1D4ED8);
  static const violet600 = Color(0xFF7C3AED);
  static const violet300 = Color(0xFFC4B5FD);
  static const violet700 = Color(0xFF6D28D9);

  /// Readable secondary text for the current surface brightness.
  static Color mutedText(BuildContext context) =>
      Theme.of(context).brightness == Brightness.dark ? slate300 : slate600;

  /// Keeps semantic accents readable when they are used for text and icons.
  static Color readableAccent(BuildContext context, Color color) {
    final dark = Theme.of(context).brightness == Brightness.dark;
    if (color == emerald600) return dark ? emerald300 : emerald700;
    if (color == amber500 || color == amber600) {
      return dark ? amber300 : amber800;
    }
    if (color == rose600) return dark ? rose300 : rose700;
    if (color == blue600) return dark ? blue300 : blue700;
    if (color == violet600) return dark ? violet300 : violet700;
    if (color == red500 || color == red600 || color == red700) {
      return dark ? const Color(0xFFFCA5A5) : red700;
    }
    return dark ? slate200 : slate700;
  }
}

/// Shared spacing values used by the app's reusable layout widgets.
class AppSpacing {
  const AppSpacing._();

  static const double xs = 4;
  static const double sm = 8;
  static const double md = 12;
  static const double lg = 16;
  static const double xl = 24;
  static const double xxl = 32;
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
      onSurfaceVariant: dark ? AppColors.slate300 : AppColors.slate600,
      outline: dark ? AppColors.slate700 : AppColors.slate200,
      outlineVariant: dark ? AppColors.slate800 : AppColors.slate200,
      surfaceContainerHighest: dark ? AppColors.slate800 : AppColors.slate100,
    );
    final border = OutlineInputBorder(
      borderRadius: BorderRadius.circular(12),
      borderSide: BorderSide(
        color: dark ? AppColors.slate700 : AppColors.slate200,
      ),
    );
    final baseTextTheme = ThemeData(
      useMaterial3: true,
      brightness: brightness,
    ).textTheme;
    final textTheme = baseTextTheme.copyWith(
      headlineSmall: baseTextTheme.headlineSmall?.copyWith(
        fontWeight: FontWeight.w700,
        letterSpacing: -0.25,
      ),
      titleLarge: baseTextTheme.titleLarge?.copyWith(
        fontWeight: FontWeight.w700,
        letterSpacing: -0.15,
      ),
      titleMedium: baseTextTheme.titleMedium?.copyWith(
        fontWeight: FontWeight.w700,
      ),
      titleSmall: baseTextTheme.titleSmall?.copyWith(
        fontWeight: FontWeight.w700,
      ),
      labelLarge: baseTextTheme.labelLarge?.copyWith(
        fontWeight: FontWeight.w700,
      ),
      bodySmall: baseTextTheme.bodySmall?.copyWith(
        color: dark ? AppColors.slate300 : AppColors.slate600,
        height: 1.35,
      ),
    );

    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      brightness: brightness,
      textTheme: textTheme,
      materialTapTargetSize: MaterialTapTargetSize.padded,
      focusColor: AppColors.red600.withValues(alpha: 0.14),
      hoverColor: AppColors.red600.withValues(alpha: 0.06),
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
          side: BorderSide(
            color: dark ? AppColors.slate800 : AppColors.slate200,
          ),
        ),
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: dark ? AppColors.slate800 : AppColors.slate50,
        border: border,
        enabledBorder: border,
        focusedBorder: border.copyWith(
          borderSide: const BorderSide(color: AppColors.red600, width: 1.5),
        ),
        errorBorder: border.copyWith(
          borderSide: const BorderSide(color: AppColors.rose600),
        ),
        focusedErrorBorder: border.copyWith(
          borderSide: const BorderSide(color: AppColors.rose600, width: 1.5),
        ),
        labelStyle: TextStyle(
          color: dark ? AppColors.slate300 : AppColors.slate600,
        ),
        helperStyle: TextStyle(
          color: dark ? AppColors.slate300 : AppColors.slate600,
        ),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: 14,
          vertical: 14,
        ),
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: AppColors.red600,
          foregroundColor: Colors.white,
          minimumSize: const Size(48, 48),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(12),
          ),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: dark ? AppColors.slate100 : AppColors.slate700,
          side: BorderSide(
            color: dark ? AppColors.slate700 : AppColors.slate200,
          ),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(12),
          ),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          foregroundColor: dark ? const Color(0xFFF87171) : AppColors.red600,
          minimumSize: const Size(48, 48),
          textStyle: const TextStyle(fontWeight: FontWeight.w700),
        ),
      ),
      iconButtonTheme: IconButtonThemeData(
        style: IconButton.styleFrom(
          minimumSize: const Size(48, 48),
          foregroundColor: dark ? AppColors.slate200 : AppColors.slate700,
        ),
      ),
      chipTheme: ChipThemeData(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        side: BorderSide(color: dark ? AppColors.slate700 : AppColors.slate200),
        selectedColor: AppColors.red600,
        secondarySelectedColor: AppColors.red600,
        checkmarkColor: Colors.white,
        labelStyle: TextStyle(
          fontSize: 13,
          color: dark ? AppColors.slate200 : AppColors.slate700,
        ),
        secondaryLabelStyle: const TextStyle(color: Colors.white),
      ),
      navigationBarTheme: NavigationBarThemeData(
        backgroundColor: dark ? AppColors.slate900 : Colors.white,
        indicatorColor: dark ? const Color(0x33DC2626) : AppColors.red50,
        iconTheme: WidgetStateProperty.resolveWith(
          (s) => IconThemeData(
            color: s.contains(WidgetState.selected)
                ? AppColors.red600
                : AppColors.slate500,
          ),
        ),
        labelTextStyle: WidgetStateProperty.resolveWith(
          (s) => TextStyle(
            fontSize: 12,
            fontWeight: s.contains(WidgetState.selected)
                ? FontWeight.w700
                : FontWeight.w500,
            color: s.contains(WidgetState.selected)
                ? AppColors.red600
                : AppColors.slate500,
          ),
        ),
      ),
      navigationRailTheme: NavigationRailThemeData(
        backgroundColor: dark ? AppColors.slate900 : Colors.white,
        indicatorColor: dark ? const Color(0x33DC2626) : AppColors.red50,
        selectedIconTheme: const IconThemeData(color: AppColors.red600),
        selectedLabelTextStyle: const TextStyle(
          color: AppColors.red600,
          fontWeight: FontWeight.w700,
        ),
      ),
      listTileTheme: ListTileThemeData(
        iconColor: dark ? AppColors.slate300 : AppColors.slate600,
        minVerticalPadding: 10,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
      ),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? Colors.white
              : dark
              ? AppColors.slate300
              : AppColors.slate600,
        ),
        trackColor: WidgetStateProperty.resolveWith(
          (states) => states.contains(WidgetState.selected)
              ? AppColors.red600
              : dark
              ? AppColors.slate700
              : AppColors.slate300,
        ),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: dark ? const Color(0xFFF87171) : AppColors.red600,
        linearTrackColor: dark ? AppColors.slate800 : AppColors.slate200,
        circularTrackColor: dark ? AppColors.slate800 : AppColors.slate200,
      ),
      dividerTheme: DividerThemeData(
        color: dark ? AppColors.slate800 : AppColors.slate200,
        space: 1,
      ),
      snackBarTheme: const SnackBarThemeData(
        behavior: SnackBarBehavior.floating,
        showCloseIcon: true,
      ),
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
