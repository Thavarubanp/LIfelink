import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../widgets/common.dart';

/// Device feature: call or email a donor from the acceptance list (the phone's dialer / mail app).
class ContactLauncher {
  const ContactLauncher._();

  /// tel: link for a phone number (spaces and dashes removed); null when there is no usable number.
  static Uri? phoneUri(String phone) {
    final digits = phone.replaceAll(RegExp(r'[^\d+]'), '');
    return digits.length < 7 ? null : Uri(scheme: 'tel', path: digits);
  }

  /// mailto: link with a subject; null when the address is not an email.
  static Uri? emailUri(String email, {String? subject}) {
    final e = email.trim();
    if (!RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$').hasMatch(e)) return null;
    return Uri(scheme: 'mailto', path: e, query: subject == null ? null : 'subject=${Uri.encodeComponent(subject)}');
  }

  static Future<void> call(BuildContext context, String phone) =>
      _open(context, phoneUri(phone), 'There is no phone number for this donor.', 'No app on this phone can make calls.');

  static Future<void> email(BuildContext context, String email, {String? subject}) => _open(
      context, emailUri(email, subject: subject), 'There is no valid email address for this donor.', 'No email app is set up on this phone.');

  static Future<void> _open(BuildContext context, Uri? uri, String missing, String noApp) async {
    if (uri == null) {
      showSnack(context, missing, type: SnackType.warning);
      return;
    }
    var opened = false;
    try {
      opened = await launchUrl(uri, mode: LaunchMode.externalApplication);
    } catch (_) {
      opened = false;
    }
    if (!opened && context.mounted) showSnack(context, noApp, type: SnackType.warning);
  }
}
