import 'package:flutter/material.dart';

class ShellPageHeader extends StatelessWidget {
  const ShellPageHeader({super.key, required this.title, this.actions});

  final String title;
  final Widget? actions;

  @override
  Widget build(BuildContext context) {
    final heading = Text(title, style: Theme.of(context).textTheme.titleLarge);
    final actions = this.actions;
    if (actions == null) return heading;

    return Wrap(
      alignment: WrapAlignment.spaceBetween,
      crossAxisAlignment: WrapCrossAlignment.center,
      spacing: 16,
      runSpacing: 8,
      children: [heading, actions],
    );
  }
}
