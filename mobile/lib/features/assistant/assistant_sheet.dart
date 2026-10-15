import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import 'assistant_controller.dart';
import 'assistant_models.dart';

Future<void> showAssistantSheet(BuildContext context) =>
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      builder: (_) => const ModalSheetSafeArea(
        child: FractionallySizedBox(
          heightFactor: 0.92,
          child: AssistantSheet(),
        ),
      ),
    );

String? mobileRouteForAssistantAction(String route) {
  const routes = <String, String>{
    '/donor/acceptances': AppRoutes.donorAcceptances,
    '/donor/requests/create': AppRoutes.createRequest,
    '/donor/requests': AppRoutes.donorRequests,
    '/doctor/screenings': AppRoutes.doctorReports,
    '/doctor/dashboard': AppRoutes.doctorHome,
    '/hospital/inventory': AppRoutes.hospitalInventory,
    '/hospital/transfers': AppRoutes.hospitalTransfers,
    '/hospital/requests/verify': AppRoutes.hospitalVerifyRequests,
    '/admin/hospitals/pending': AppRoutes.adminRegistrations,
    '/admin/complaints': AppRoutes.adminComplaints,
    '/admin/appeals': AppRoutes.adminAppeals,
  };
  final fixed = routes[route];
  if (fixed != null) return fixed;
  if (RegExp(r'^/donor/acceptances/[0-9a-fA-F-]{36}/screening$')
      .hasMatch(route)) {
    return route;
  }
  return null;
}

const _suggestions = <String, List<String>>{
  Roles.admin: ['What needs my attention?', 'How does hospital approval work?'],
  Roles.hospitalStaff: [
    'What needs my attention?',
    'Which packets expire soon?',
    'How do I offer blood to another hospital?',
  ],
  Roles.doctor: [
    'What needs my attention?',
    'How does donor screening review work?',
  ],
  Roles.user: [
    'Can I donate after a tattoo?',
    'Who can receive O+ blood?',
    'What is my donation status?',
  ],
};

class AssistantSheet extends ConsumerStatefulWidget {
  const AssistantSheet({super.key});

  @override
  ConsumerState<AssistantSheet> createState() => _AssistantSheetState();
}

class _AssistantSheetState extends ConsumerState<AssistantSheet> {
  final _input = TextEditingController();
  final _scroll = ScrollController();

  @override
  void dispose() {
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _send([String? value]) {
    final text = (value ?? _input.text).trim();
    if (text.isEmpty) return;
    _input.clear();
    ref.read(assistantConversationProvider.notifier).send(text);
  }

  void _scrollToEnd() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scroll.hasClients) {
        _scroll.animateTo(
          _scroll.position.maxScrollExtent,
          duration: const Duration(milliseconds: 220),
          curve: Curves.easeOut,
        );
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final conversation = ref.watch(assistantConversationProvider);
    final user = ref.watch(authControllerProvider).user;
    ref.listen(assistantConversationProvider, (_, _) => _scrollToEnd());
    final role = user?.primaryRole ?? Roles.user;

    return Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 4, 8, 10),
          child: Row(
            children: [
              const CircleAvatar(
                backgroundColor: AppColors.red600,
                foregroundColor: Colors.white,
                child: Icon(Icons.chat_bubble_outline),
              ),
              const SizedBox(width: 10),
              const Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'LifeLink Assistant',
                      style: TextStyle(fontWeight: FontWeight.w700),
                    ),
                    Text(
                      'Guidance only. Doctors make all medical decisions.',
                      style: TextStyle(fontSize: 11, color: AppColors.slate500),
                    ),
                  ],
                ),
              ),
              if (conversation.messages.isNotEmpty)
                IconButton(
                  tooltip: 'Clear conversation',
                  onPressed: conversation.thinking
                      ? null
                      : ref.read(assistantConversationProvider.notifier).clear,
                  icon: const Icon(Icons.delete_outline),
                ),
              IconButton(
                tooltip: 'Close assistant',
                onPressed: () => Navigator.of(context).pop(),
                icon: const Icon(Icons.close),
              ),
            ],
          ),
        ),
        const Divider(),
        Expanded(
          child: ListView(
            controller: _scroll,
            padding: const EdgeInsets.all(16),
            children: [
              if (conversation.messages.isEmpty)
                _Welcome(
                  firstName: user?.firstName ?? '',
                  suggestions: _suggestions[role] ?? _suggestions[Roles.user]!,
                  onSelected: _send,
                )
              else
                for (final message in conversation.messages)
                  _MessageBubble(message: message),
              if (conversation.thinking)
                const Padding(
                  padding: EdgeInsets.fromLTRB(38, 6, 0, 8),
                  child: Row(
                    children: [
                      SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      ),
                      SizedBox(width: 8),
                      Text(
                        'Thinking...',
                        style: TextStyle(
                          fontSize: 12,
                          color: AppColors.slate500,
                        ),
                      ),
                    ],
                  ),
                ),
              if (conversation.error != null) ...[
                InfoBanner(
                  'The assistant could not respond: ${conversation.error}',
                  color: AppColors.rose600,
                  icon: Icons.cloud_off_outlined,
                ),
                const SizedBox(height: 8),
                AdaptiveActions(
                  children: [
                    OutlinedButton(
                      onPressed: ref
                          .read(assistantConversationProvider.notifier)
                          .dismissError,
                      child: const Text('Dismiss'),
                    ),
                    FilledButton.icon(
                      key: const Key('assistant-retry'),
                      onPressed: ref
                          .read(assistantConversationProvider.notifier)
                          .retry,
                      icon: const Icon(Icons.refresh),
                      label: const Text('Try again'),
                    ),
                  ],
                ),
              ],
            ],
          ),
        ),
        const Divider(),
        AnimatedPadding(
          duration: const Duration(milliseconds: 120),
          padding: EdgeInsets.fromLTRB(
            12,
            10,
            12,
            modalSheetBottomPadding(context, base: 10),
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: TextField(
                  key: const Key('assistant-input'),
                  controller: _input,
                  minLines: 1,
                  maxLines: 4,
                  maxLength: 2000,
                  textInputAction: TextInputAction.send,
                  decoration: const InputDecoration(
                    hintText: 'Type your question...',
                    counterText: '',
                  ),
                  onSubmitted: conversation.thinking ? null : _send,
                ),
              ),
              const SizedBox(width: 8),
              IconButton.filled(
                tooltip: 'Send',
                onPressed: conversation.thinking ? null : _send,
                icon: const Icon(Icons.send),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _Welcome extends StatelessWidget {
  const _Welcome({
    required this.firstName,
    required this.suggestions,
    required this.onSelected,
  });

  final String firstName;
  final List<String> suggestions;
  final ValueChanged<String> onSelected;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      Text(
        'Hi ${firstName.isEmpty ? 'there' : firstName}! Ask about blood donation, how LifeLink works, or your own requests and tasks.',
      ),
      const SizedBox(height: 14),
      for (final suggestion in suggestions)
        Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: OutlinedButton(
            onPressed: () => onSelected(suggestion),
            style: OutlinedButton.styleFrom(alignment: Alignment.centerLeft),
            child: Text(suggestion),
          ),
        ),
      const SizedBox(height: 4),
      const Text(
        'Messages are processed by Google Gemini. The assistant only sees your own information.',
        style: TextStyle(fontSize: 11, color: AppColors.slate500),
      ),
    ],
  );
}

class _MessageBubble extends StatelessWidget {
  const _MessageBubble({required this.message});

  final AssistantMessage message;

  @override
  Widget build(BuildContext context) => Align(
    alignment: message.isUser ? Alignment.centerRight : Alignment.centerLeft,
    child: Container(
      constraints: const BoxConstraints(maxWidth: 620),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: message.isUser
            ? AppColors.red600
            : Theme.of(context).colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(14),
      ),
      child: message.isUser
          ? Text(message.text, style: const TextStyle(color: Colors.white))
          : Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (message.segments.isNotEmpty)
                  for (var i = 0; i < message.segments.length; i++) ...[
                    if (i > 0) const SizedBox(height: 14),
                    _SegmentView(segment: message.segments[i]),
                  ]
                else
                  Text(message.text),
                if (message.actions.isNotEmpty) ...[
                  const SizedBox(height: 10),
                  Wrap(
                    spacing: 8,
                    runSpacing: 6,
                    children: [
                      for (final action in message.actions)
                        _ActionView(action: action),
                    ],
                  ),
                ],
              ],
            ),
    ),
  );
}

class _SegmentView extends StatelessWidget {
  const _SegmentView({required this.segment});

  final AssistantSegment segment;

  @override
  Widget build(BuildContext context) {
    final (label, icon, color) = switch (segment.type) {
      'medical' => (
        'Medical guidance',
        Icons.menu_book_outlined,
        AppColors.blue600,
      ),
      'account' => ('Your account', Icons.person_outline, AppColors.emerald600),
      'screening' => (
        'Screening',
        Icons.medical_services_outlined,
        AppColors.red600,
      ),
      _ => ('LifeLink guide', Icons.explore_outlined, AppColors.violet600),
    };
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Chip(
          avatar: Icon(icon, size: 16, color: color),
          label: Text(label),
          visualDensity: VisualDensity.compact,
        ),
        const SizedBox(height: 5),
        Text(segment.text),
        if (segment.sources.isNotEmpty) ...[
          const SizedBox(height: 8),
          for (var i = 0; i < segment.sources.length; i++)
            _SourceView(index: i + 1, source: segment.sources[i]),
        ],
      ],
    );
  }
}

class _SourceView extends StatelessWidget {
  const _SourceView({required this.index, required this.source});

  final int index;
  final AssistantSource source;

  @override
  Widget build(BuildContext context) {
    final uri = Uri.tryParse(source.url ?? '');
    final canOpen =
        uri != null && (uri.scheme == 'https' || uri.scheme == 'http');
    final label =
        '$index. ${source.title}${source.publisher?.isNotEmpty == true ? ' - ${source.publisher}' : ''}';
    if (!canOpen) {
      return Padding(
        padding: const EdgeInsets.only(bottom: 3),
        child: Text(label, style: const TextStyle(fontSize: 11)),
      );
    }
    return InkWell(
      onTap: () async {
        if (!await launchUrl(uri, mode: LaunchMode.externalApplication) &&
            context.mounted) {
          showSnack(
            context,
            'Could not open this source.',
            type: SnackType.error,
          );
        }
      },
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Flexible(
              child: Text(
                label,
                style: TextStyle(
                  fontSize: 11,
                  color: Theme.of(context).colorScheme.primary,
                  decoration: TextDecoration.underline,
                ),
              ),
            ),
            const SizedBox(width: 4),
            const Icon(Icons.open_in_new, size: 13),
          ],
        ),
      ),
    );
  }
}

class _ActionView extends StatelessWidget {
  const _ActionView({required this.action});

  final AssistantAction action;

  @override
  Widget build(BuildContext context) {
    final route = mobileRouteForAssistantAction(action.route);
    if (route == null) {
      return Text(
        action.label,
        style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
      );
    }
    return OutlinedButton.icon(
      onPressed: () {
        final router = GoRouter.of(context);
        Navigator.of(context).pop();
        router.push(route);
      },
      icon: const Icon(Icons.arrow_forward, size: 16),
      label: Text(action.label),
    );
  }
}
