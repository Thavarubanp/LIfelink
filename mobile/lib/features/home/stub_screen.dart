import 'package:flutter/material.dart';

import '../../core/widgets/state_views.dart';

/// Placeholder for a screen built in a later step. The route already exists, so the later step only replaces
/// this widget.
class StubScreen extends StatelessWidget {
  const StubScreen({super.key, required this.title, required this.step, required this.owner, this.description});

  final String title;
  final int step;
  final String owner;
  final String? description;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: Text(title)),
        body: EmptyView(
          icon: Icons.construction_rounded,
          title: 'Coming in Step $step',
          message: '${description ?? '$title is part of a later step.'}\nBuilt by $owner. The web app has it today.',
        ),
      );
}
