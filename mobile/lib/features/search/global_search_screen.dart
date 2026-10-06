import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import 'search_repository.dart';

class GlobalSearchScreen extends ConsumerStatefulWidget {
  const GlobalSearchScreen({super.key});

  @override
  ConsumerState<GlobalSearchScreen> createState() => _GlobalSearchScreenState();
}

class _GlobalSearchScreenState extends ConsumerState<GlobalSearchScreen> {
  Timer? _timer;
  String _query = '';

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  void _changed(String value) {
    _timer?.cancel();
    if (value.trim().length < 2) {
      setState(() => _query = '');
      return;
    }
    _timer = Timer(const Duration(milliseconds: 350), () {
      if (mounted) setState(() => _query = value.trim());
    });
  }

  @override
  Widget build(BuildContext context) {
    final value = _query.isEmpty
        ? null
        : ref.watch(globalSearchProvider(_query));
    final isAdmin =
        ref.watch(authControllerProvider).user?.hasRole(Roles.admin) == true;
    return Scaffold(
      appBar: AppBar(title: const Text('Search LifeLink')),
      body: ContentWidth(
        maxWidth: 760,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            SearchField(
              hint: 'Search people, doctors or hospitals',
              onChanged: _changed,
            ),
            const SizedBox(height: 16),
            if (value == null)
              const EmptyView(
                icon: Icons.manage_search,
                message: 'Enter at least 2 characters to search LifeLink.',
              )
            else
              AsyncView(
                value: value,
                onRetry: () => ref.invalidate(globalSearchProvider(_query)),
                loadingMessage: 'Searching...',
                data: (results) {
                  if (results.totalCount == 0) {
                    return const EmptyView(
                      icon: Icons.search_off,
                      message: 'No matching profiles were found.',
                    );
                  }
                  return Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (results.hospitals.isNotEmpty)
                        _ResultSection(
                          title: 'Hospitals',
                          items: results.hospitals,
                          showUserContact: isAdmin,
                        ),
                      if (results.doctors.isNotEmpty)
                        _ResultSection(
                          title: 'Doctors',
                          items: results.doctors,
                          showUserContact: isAdmin,
                        ),
                      if (results.users.isNotEmpty)
                        _ResultSection(
                          title: 'Users',
                          items: results.users,
                          showUserContact: isAdmin,
                        ),
                    ],
                  );
                },
              ),
          ],
        ),
      ),
    );
  }
}

class _ResultSection extends StatelessWidget {
  const _ResultSection({
    required this.title,
    required this.items,
    required this.showUserContact,
  });
  final String title;
  final List<SearchItem> items;
  final bool showUserContact;

  @override
  Widget build(BuildContext context) => SectionCard(
    title: title,
    child: Column(
      children: [
        for (var i = 0; i < items.length; i++) ...[
          _ResultTile(item: items[i], showUserContact: showUserContact),
          if (i != items.length - 1) const Divider(height: 1),
        ],
      ],
    ),
  );
}

class _ResultTile extends StatelessWidget {
  const _ResultTile({required this.item, required this.showUserContact});
  final SearchItem item;
  final bool showUserContact;

  @override
  Widget build(BuildContext context) {
    final icon = switch (item.resultType) {
      'Hospital' => Icons.local_hospital_outlined,
      'Doctor' => Icons.medical_services_outlined,
      _ => Icons.person_outline,
    };
    return ListTile(
      contentPadding: EdgeInsets.zero,
      leading: CircleAvatar(
        child: item.avatarInitial?.isNotEmpty == true
            ? Text(item.avatarInitial!)
            : Icon(icon),
      ),
      title: Text(
        item.displayName,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      subtitle: Text(
        [
          if (item.resultType != 'User' || showUserContact) item.subText,
          ?item.extraInfo,
        ].where((v) => v.isNotEmpty).join(' · '),
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (item.status?.isNotEmpty == true)
            StatusBadge(
              item.status!,
              variant: item.status == 'Active'
                  ? BadgeVariant.success
                  : BadgeVariant.warning,
            ),
          const SizedBox(width: 4),
          const Icon(Icons.chevron_right),
        ],
      ),
      onTap: () => context.push(switch (item.resultType) {
        'Hospital' => AppRoutes.hospitalProfile(item.id),
        'Doctor' => AppRoutes.doctorProfile(item.id),
        _ => AppRoutes.userProfile(item.id),
      }),
    );
  }
}
