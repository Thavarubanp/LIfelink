/// Client-side packet rules that mirror the API (the API re-checks every one).
library;

/// Maximum packets per "Add packets" form (CreateBloodPacketsDto.MaxQuantity).
const maxPacketsPerForm = 20;

/// Collected date rules, as on the web page: required, not after today (the Sri Lanka date), and not so old that the
/// packet would already be expired (collected date + shelf life must be after today).
String? collectedDateError(DateTime? collected, int? shelfLifeDays, DateTime today) {
  if (collected == null) return 'Collected date is required.';
  final day = DateTime(collected.year, collected.month, collected.day);
  final t = DateTime(today.year, today.month, today.day);
  if (day.isAfter(t)) return 'Collected date cannot be in the future.';
  if (shelfLifeDays != null && shelfLifeDays > 0) {
    final expiry = day.add(Duration(days: shelfLifeDays));
    if (!expiry.isAfter(t)) {
      return 'A packet collected on ${_ymd(day)} would already be expired (shelf life $shelfLifeDays days).';
    }
  }
  return null;
}

/// Earliest collected date the date picker offers (the oldest date that is not already expired).
DateTime earliestCollectedDate(int? shelfLifeDays, DateTime today) {
  final t = DateTime(today.year, today.month, today.day);
  if (shelfLifeDays == null || shelfLifeDays <= 0) return DateTime(t.year - 1, t.month, t.day);
  return t.subtract(Duration(days: shelfLifeDays - 1));
}

final _tracking = RegExp(r'^PKT-\d{8,}$');

/// A scanned or typed code as a tracking number ("pkt-00000012" → "PKT-00000012"); null when it is not one.
String? normalizeTrackingNumber(String? raw) {
  if (raw == null) return null;
  final value = raw.trim().toUpperCase();
  return _tracking.hasMatch(value) ? value : null;
}

String _ymd(DateTime d) =>
    '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';
