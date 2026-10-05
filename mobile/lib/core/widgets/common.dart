import 'package:flutter/material.dart';

import '../api/api_error.dart';
import '../theme/app_theme.dart';

enum SnackType { success, error, info, warning }

/// Short message at the bottom of the screen.
void showSnack(BuildContext context, String message, {SnackType type = SnackType.info, String? title}) {
  final color = switch (type) {
    SnackType.success => AppColors.emerald600,
    SnackType.error => AppColors.rose600,
    SnackType.warning => AppColors.amber600,
    SnackType.info => AppColors.slate800,
  };
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(
      backgroundColor: color,
      content: Text(title == null ? message : '$title: $message', style: const TextStyle(color: Colors.white)),
      duration: Duration(seconds: type == SnackType.error ? 5 : 3),
    ));
}

void showErrorSnack(BuildContext context, Object error, {String? title}) =>
    showSnack(context, ApiError.from(error).message, type: SnackType.error, title: title);

/// Yes/no question; returns true when confirmed.
Future<bool> confirmDialog(
  BuildContext context, {
  required String title,
  required String message,
  String confirmLabel = 'Confirm',
  bool destructive = false,
}) async {
  final result = await showDialog<bool>(
    context: context,
    builder: (ctx) => AlertDialog(
      title: Text(title),
      content: Text(message),
      actions: [
        TextButton(onPressed: () => Navigator.of(ctx).pop(false), child: const Text('Cancel')),
        FilledButton(
          style: destructive ? FilledButton.styleFrom(backgroundColor: AppColors.rose600) : null,
          onPressed: () => Navigator.of(ctx).pop(true),
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
  return result ?? false;
}

/// Asks for a required reason (reject, issue...). Returns null when cancelled.
Future<String?> reasonDialog(
  BuildContext context, {
  required String title,
  required String hint,
  String confirmLabel = 'Submit',
  int maxLength = 500,
}) {
  return showDialog<String>(
    context: context,
    builder: (ctx) => _ReasonDialog(title: title, hint: hint, confirmLabel: confirmLabel, maxLength: maxLength),
  );
}

class _ReasonDialog extends StatefulWidget {
  const _ReasonDialog({required this.title, required this.hint, required this.confirmLabel, required this.maxLength});

  final String title;
  final String hint;
  final String confirmLabel;
  final int maxLength;

  @override
  State<_ReasonDialog> createState() => _ReasonDialogState();
}

class _ReasonDialogState extends State<_ReasonDialog> {
  final _controller = TextEditingController();
  final _formKey = GlobalKey<FormState>();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: Text(widget.title),
        content: Form(
          key: _formKey,
          child: TextFormField(
            controller: _controller,
            autofocus: true,
            maxLines: 3,
            maxLength: widget.maxLength,
            decoration: InputDecoration(hintText: widget.hint),
            validator: (v) => (v == null || v.trim().isEmpty) ? 'A reason is required.' : null,
          ),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancel')),
          FilledButton(
            onPressed: () {
              if (_formKey.currentState!.validate()) Navigator.of(context).pop(_controller.text.trim());
            },
            child: Text(widget.confirmLabel),
          ),
        ],
      );
}

/// Search box with a clear button.
class SearchField extends StatefulWidget {
  const SearchField({super.key, required this.hint, required this.onChanged, this.initial = ''});

  final String hint;
  final String initial;
  final ValueChanged<String> onChanged;

  @override
  State<SearchField> createState() => _SearchFieldState();
}

class _SearchFieldState extends State<SearchField> {
  late final _controller = TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => TextField(
        controller: _controller,
        onChanged: (v) {
          widget.onChanged(v);
          setState(() {});
        },
        textInputAction: TextInputAction.search,
        decoration: InputDecoration(
          hintText: widget.hint,
          prefixIcon: const Icon(Icons.search),
          isDense: true,
          suffixIcon: _controller.text.isEmpty
              ? null
              : IconButton(
                  tooltip: 'Clear',
                  icon: const Icon(Icons.close),
                  onPressed: () {
                    _controller.clear();
                    widget.onChanged('');
                    setState(() {});
                  },
                ),
        ),
      );
}

/// One-choice filter chips in a horizontal scroll.
class FilterChips<T> extends StatelessWidget {
  const FilterChips({super.key, required this.options, required this.selected, required this.onSelected});

  final List<(T, String)> options;
  final T selected;
  final ValueChanged<T> onSelected;

  @override
  Widget build(BuildContext context) => SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: [
            for (final (value, label) in options)
              Padding(
                padding: const EdgeInsets.only(right: 8),
                child: ChoiceChip(
                  label: Text(label),
                  selected: value == selected,
                  onSelected: (_) => onSelected(value),
                  labelStyle: TextStyle(
                    color: value == selected ? Colors.white : null,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ),
          ],
        ),
      );
}

/// Card with a title row and content.
class SectionCard extends StatelessWidget {
  const SectionCard({super.key, this.title, this.icon, this.trailing, required this.child, this.padding = const EdgeInsets.all(16)});

  final String? title;
  final IconData? icon;
  final Widget? trailing;
  final Widget child;
  final EdgeInsets padding;

  @override
  Widget build(BuildContext context) => Card(
        child: Padding(
          padding: padding,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (title != null) ...[
                Row(
                  children: [
                    if (icon != null) ...[Icon(icon, size: 18, color: AppColors.red600), const SizedBox(width: 8)],
                    Expanded(child: Text(title!, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15))),
                    ?trailing,
                  ],
                ),
                const SizedBox(height: 12),
              ],
              child,
            ],
          ),
        ),
      );
}

/// Coloured information box (warning / info / error).
class InfoBanner extends StatelessWidget {
  const InfoBanner(this.message, {super.key, this.color = AppColors.amber600, this.icon = Icons.info_outline});

  final String message;
  final Color color;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Container(
        width: double.infinity,
        padding: const EdgeInsets.all(12),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.1),
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: color.withValues(alpha: 0.35)),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, size: 18, color: color),
            const SizedBox(width: 8),
            Expanded(child: Text(message, style: TextStyle(color: color, fontSize: 13))),
          ],
        ),
      );
}

/// Primary button with a spinner while busy.
class BusyButton extends StatelessWidget {
  const BusyButton({super.key, required this.label, required this.onPressed, this.busy = false, this.icon});

  final String label;
  final VoidCallback? onPressed;
  final bool busy;
  final IconData? icon;

  @override
  Widget build(BuildContext context) => FilledButton(
        onPressed: busy ? null : onPressed,
        child: busy
            ? const SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
            : Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (icon != null) ...[Icon(icon, size: 18), const SizedBox(width: 8)],
                  Flexible(child: Text(label, overflow: TextOverflow.ellipsis)),
                ],
              ),
      );
}

/// Label / value pair for detail screens.
class LabeledValue extends StatelessWidget {
  const LabeledValue(this.label, this.value, {super.key, this.mono = false});

  final String label;
  final String value;
  final bool mono;

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
          const SizedBox(height: 2),
          Text(value, style: TextStyle(fontWeight: FontWeight.w600, fontFamily: mono ? 'monospace' : null)),
        ],
      );
}

/// Shows [pageSize] items, then "Show N more of M".
class ShowMoreList<T> extends StatefulWidget {
  const ShowMoreList({super.key, required this.items, required this.itemBuilder, this.pageSize = 10, this.separator});

  final List<T> items;
  final Widget Function(BuildContext, T) itemBuilder;
  final int pageSize;
  final Widget? separator;

  @override
  State<ShowMoreList<T>> createState() => _ShowMoreListState<T>();
}

class _ShowMoreListState<T> extends State<ShowMoreList<T>> {
  late int _limit = widget.pageSize;

  @override
  Widget build(BuildContext context) {
    final shown = widget.items.take(_limit).toList();
    final rest = widget.items.length - shown.length;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var i = 0; i < shown.length; i++) ...[
          if (i > 0) widget.separator ?? const Divider(),
          widget.itemBuilder(context, shown[i]),
        ],
        if (rest > 0)
          TextButton(
            onPressed: () => setState(() => _limit += widget.pageSize),
            child: Text('Show ${rest < widget.pageSize ? rest : widget.pageSize} more of $rest'),
          ),
      ],
    );
  }
}

/// True on tablets and wide screens.
bool isWide(BuildContext context) => MediaQuery.sizeOf(context).width >= 600;

/// Limits content width on tablets so forms and lists stay readable.
class ContentWidth extends StatelessWidget {
  const ContentWidth({super.key, required this.child, this.maxWidth = 840});

  final Widget child;
  final double maxWidth;

  @override
  Widget build(BuildContext context) =>
      Center(child: ConstrainedBox(constraints: BoxConstraints(maxWidth: maxWidth), child: child));
}
