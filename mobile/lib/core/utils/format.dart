import 'package:intl/intl.dart';

import '../config/constants.dart';

/// Dates and times as the web app shows them. Sri Lanka time is UTC+5:30 all year.
class Fmt {
  const Fmt._();

  static DateTime toSriLanka(DateTime value) => value.toUtc().add(AppConstants.sriLankaOffset);

  /// "3 Oct 2026, 2:15 pm" in Sri Lanka time.
  static String sriLankaDateTime(DateTime? value) {
    if (value == null) return '-';
    final local = toSriLanka(value);
    return '${DateFormat('d MMM yyyy, h:mm').format(local)} ${local.hour < 12 ? 'am' : 'pm'}';
  }

  /// "3 Oct 2026" in Sri Lanka time.
  static String date(DateTime? value) => value == null ? '-' : DateFormat('d MMM yyyy').format(toSriLanka(value));

  /// A calendar date as the API expects it: yyyy-MM-dd.
  static String apiDate(DateTime value) => DateFormat('yyyy-MM-dd').format(value);

  /// Today's calendar date in Sri Lanka (the API treats "today" as the Sri Lanka date).
  static DateTime sriLankaToday([DateTime? now]) {
    final sl = toSriLanka(now ?? DateTime.now());
    return DateTime(sl.year, sl.month, sl.day);
  }

  /// "4:59" countdown.
  static String countdown(Duration d) {
    final total = d.inMilliseconds <= 0 ? 0 : (d.inMilliseconds / 1000).ceil();
    return '${total ~/ 60}:${(total % 60).toString().padLeft(2, '0')}';
  }

  static String plural(int n, String word) => '$n $word${n == 1 ? '' : 's'}';

  /// "InventoryShortageHelp" -> "Inventory shortage help", "TRANSFER_IN" -> "Transfer in".
  static String humanize(String value) {
    final spaced = value.replaceAll('_', ' ').replaceAllMapped(RegExp(r'(?<=[a-z])(?=[A-Z])'), (_) => ' ').trim();
    if (spaced.isEmpty) return spaced;
    final lower = spaced.toLowerCase();
    return lower[0].toUpperCase() + lower.substring(1);
  }
}
