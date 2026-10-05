import 'package:flutter/material.dart';

import '../attachments/attachment.dart';
import '../attachments/attachment_widgets.dart';
import '../theme/app_theme.dart';
import '../utils/format.dart';

/// One message of a conversation (registration, appeal or complaint thread).
class ThreadMessage {
  const ThreadMessage({
    required this.author,
    required this.fromAdmin,
    required this.at,
    this.title,
    this.text,
    this.detail,
    this.attachment,
  });

  final String author;
  final bool fromAdmin;
  final DateTime? at;
  final String? title;
  final String? text;

  /// Extra lines (e.g. the fields a hospital corrected).
  final String? detail;
  final Attachment? attachment;
}

/// A conversation: administrator messages on one side, the other party's on the other.
class ThreadView extends StatelessWidget {
  const ThreadView({super.key, required this.messages, this.emptyText = 'No messages yet.'});

  final List<ThreadMessage> messages;
  final String emptyText;

  @override
  Widget build(BuildContext context) {
    if (messages.isEmpty) return Text(emptyText, style: const TextStyle(color: AppColors.slate500));
    final dark = Theme.of(context).brightness == Brightness.dark;
    return Column(
      children: [
        for (final m in messages)
          Align(
            alignment: m.fromAdmin ? Alignment.centerRight : Alignment.centerLeft,
            child: Container(
              constraints: const BoxConstraints(maxWidth: 560),
              margin: const EdgeInsets.only(bottom: 10),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: m.fromAdmin
                    ? (dark ? const Color(0x33DC2626) : AppColors.red50)
                    : (dark ? AppColors.slate800 : AppColors.slate100),
                borderRadius: BorderRadius.circular(14),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    '${m.title != null ? '${m.title} · ' : ''}${m.author}',
                    style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700),
                  ),
                  if (m.text != null && m.text!.trim().isNotEmpty) ...[const SizedBox(height: 4), SelectableText(m.text!)],
                  if (m.detail != null && m.detail!.trim().isNotEmpty) ...[
                    const SizedBox(height: 4),
                    Text(m.detail!, style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                  ],
                  if (m.attachment != null) ...[const SizedBox(height: 8), AttachmentView(attachment: m.attachment!)],
                  const SizedBox(height: 4),
                  Text(Fmt.sriLankaDateTime(m.at), style: const TextStyle(fontSize: 11, color: AppColors.slate500)),
                ],
              ),
            ),
          ),
      ],
    );
  }
}
