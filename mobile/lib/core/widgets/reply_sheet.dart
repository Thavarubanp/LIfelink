import 'package:flutter/material.dart';

import '../../features/auth/auth_widgets.dart';
import '../api/api_error.dart';
import '../attachments/attachment.dart';
import '../attachments/attachment_widgets.dart';
import '../theme/app_theme.dart';
import 'common.dart';

/// Opens a bottom sheet for a message (with an optional attachment) and runs [onSubmit]; returns true when sent.
/// API errors are shown inside the sheet (like the web's reply modals). A 409 closes it so the caller reloads.
Future<bool> showReplySheet(
  BuildContext context, {
  required String title,
  required Future<void> Function(String text, Attachment? attachment) onSubmit,
  String? subtitle,
  String label = 'Message',
  String submitLabel = 'Send',
  int minLength = 1,
  int maxLength = 1000,
  String? requiredMessage,
  bool allowAttachment = true,
  bool destructive = false,
}) async =>
    await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => ModalSheetSafeArea(
        child: ReplySheet(
          title: title,
          subtitle: subtitle,
          label: label,
          submitLabel: submitLabel,
          minLength: minLength,
          maxLength: maxLength,
          requiredMessage: requiredMessage,
          allowAttachment: allowAttachment,
          destructive: destructive,
          onSubmit: onSubmit,
        ),
      ),
    ) ??
    false;

class ReplySheet extends StatefulWidget {
  const ReplySheet({
    super.key,
    required this.title,
    required this.onSubmit,
    this.subtitle,
    this.label = 'Message',
    this.submitLabel = 'Send',
    this.minLength = 1,
    this.maxLength = 1000,
    this.requiredMessage,
    this.allowAttachment = true,
    this.destructive = false,
  });

  final String title;
  final String? subtitle;
  final String label;
  final String submitLabel;
  final int minLength;
  final int maxLength;
  final String? requiredMessage;
  final bool allowAttachment;
  final bool destructive;
  final Future<void> Function(String text, Attachment? attachment) onSubmit;

  @override
  State<ReplySheet> createState() => _ReplySheetState();
}

class _ReplySheetState extends State<ReplySheet> {
  final _text = TextEditingController();
  Attachment? _file;
  bool _sending = false;
  String? _error;

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final text = _text.text.trim();
    if (text.length < widget.minLength) {
      setState(
        () => _error =
            widget.requiredMessage ??
            (widget.minLength <= 1
                ? 'A message is required.'
                : 'Please write at least ${widget.minLength} characters.'),
      );
      return;
    }
    setState(() {
      _sending = true;
      _error = null;
    });
    try {
      await widget.onSubmit(text, _file);
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      if (!mounted) return;
      if (api.isConflict) {
        // The thread changed at the same moment: the caller reloads it
        showSnack(
          context,
          api.message,
          type: SnackType.error,
          title: 'Not sent',
        );
        Navigator.of(context).pop(true);
      } else {
        setState(() => _error = api.message);
      }
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(20, 0, 20, modalSheetBottomPadding(context)),
    child: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(widget.title, style: Theme.of(context).textTheme.titleLarge),
          if (widget.subtitle != null) ...[
            const SizedBox(height: 6),
            Text(
              widget.subtitle!,
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
          const SizedBox(height: 14),
          TextField(
            key: const Key('reply-text'),
            controller: _text,
            autofocus: true,
            maxLines: 5,
            maxLength: widget.maxLength,
            decoration: InputDecoration(labelText: widget.label),
          ),
          if (widget.allowAttachment) ...[
            AttachmentPickerField(
              value: _file,
              enabled: !_sending,
              onChanged: (a) => setState(() => _file = a),
            ),
            const SizedBox(height: 12),
          ],
          if (_error != null) ...[
            FormErrorBox(_error!),
            const SizedBox(height: 12),
          ],
          // Destructive decisions (reject, block) get a red button
          Theme(
            data: widget.destructive
                ? Theme.of(context).copyWith(
                    filledButtonTheme: FilledButtonThemeData(
                      style: Theme.of(context).filledButtonTheme.style
                          ?.copyWith(
                            backgroundColor: const WidgetStatePropertyAll(
                              AppColors.rose600,
                            ),
                          ),
                    ),
                  )
                : Theme.of(context),
            child: BusyButton(
              key: const Key('reply-submit'),
              label: widget.submitLabel,
              busy: _sending,
              onPressed: _submit,
            ),
          ),
        ],
      ),
    ),
  );
}
