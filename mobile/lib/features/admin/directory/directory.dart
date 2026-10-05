import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/api/api_client.dart';
import '../../../core/api/json.dart';
import '../../../core/config/constants.dart';
import '../../../core/providers.dart';
import '../../../core/routing/routes.dart';
import '../../../core/theme/app_theme.dart';
import '../../../core/utils/format.dart';
import '../../../core/widgets/badges.dart';
import '../../../core/widgets/common.dart';
import '../../../core/widgets/state_views.dart';
import '../../activity/activity_log.dart';
import '../registrations/registration_repository.dart';

/// AdminUserResponseDto.
class AdminUser {
  const AdminUser({
    required this.userId,
    required this.firstName,
    required this.lastName,
    required this.email,
    required this.phoneNumber,
    required this.accountStatus,
    required this.isSuspended,
    required this.suspendedUntil,
    required this.suspensionReason,
    required this.isPermanentlyBlocked,
    required this.roles,
    required this.createdAt,
  });

  factory AdminUser.fromJson(Map<String, dynamic> j) => AdminUser(
        userId: str(j['userId']),
        firstName: str(j['firstName']),
        lastName: str(j['lastName']),
        email: str(j['email']),
        phoneNumber: str(j['phoneNumber']),
        accountStatus: str(j['accountStatus']),
        isSuspended: boolOf(j['isSuspended']),
        suspendedUntil: parseDate(j['suspendedUntil']),
        suspensionReason: j['suspensionReason']?.toString(),
        isPermanentlyBlocked: boolOf(j['isPermanentlyBlocked']),
        roles: stringList(j['roles']),
        createdAt: parseDate(j['createdAt']),
      );

  final String userId;
  final String firstName;
  final String lastName;
  final String email;
  final String phoneNumber;
  final String accountStatus;
  final bool isSuspended;
  final DateTime? suspendedUntil;
  final String? suspensionReason;
  final bool isPermanentlyBlocked;
  final List<String> roles;
  final DateTime? createdAt;

  String get name => '$firstName $lastName'.trim();

  String get roleLabel => roles.contains(Roles.admin)
      ? 'Administrator'
      : roles.contains(Roles.hospitalStaff)
          ? 'Hospital staff'
          : roles.contains(Roles.doctor)
              ? 'Doctor'
              : 'Donor / patient';

  /// Governance actions apply only to donor/patient accounts (never the Admin, doctors or hospital staff), as on the web.
  bool get isDonorPatient => !roles.contains(Roles.admin) && !roles.contains(Roles.doctor) && !roles.contains(Roles.hospitalStaff);

  String get statusLabel => isPermanentlyBlocked ? 'Permanently blocked' : isSuspended ? 'Suspended' : 'Active';

  BadgeVariant get statusVariant =>
      isPermanentlyBlocked ? BadgeVariant.danger : isSuspended ? BadgeVariant.warning : BadgeVariant.success;
}

class DirectoryRepository {
  DirectoryRepository(this._api);

  final ApiClient _api;

  Future<List<AdminUser>> users() async => unwrapList(await _api.get('/Admin/users')).map(AdminUser.fromJson).toList();
}

final directoryRepositoryProvider = Provider<DirectoryRepository>((ref) => DirectoryRepository(ref.watch(apiClientProvider)));

final adminUsersProvider = FutureProvider.autoDispose<List<AdminUser>>((ref) async {
  final list = await ref.watch(directoryRepositoryProvider).users();
  list.sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
  return list;
});

/// Extra actions on a profile (part 6 adds suspend / reinstate / message). Kept as hooks so the screens stay small.
typedef UserActions = List<Widget> Function(BuildContext context, WidgetRef ref, AdminUser user);
typedef HospitalActions = List<Widget> Function(BuildContext context, WidgetRef ref, AdminHospital hospital);

/// Users directory: search and a status filter; each opens the user's profile.
class AdminUsersScreen extends ConsumerStatefulWidget {
  const AdminUsersScreen({super.key});

  @override
  ConsumerState<AdminUsersScreen> createState() => _AdminUsersScreenState();
}

class _AdminUsersScreenState extends ConsumerState<AdminUsersScreen> {
  String _search = '';
  String _filter = 'all';

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(adminUsersProvider);
    final term = _search.trim().toLowerCase();
    return Scaffold(
      appBar: AppBar(title: const Text('Users')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminUsersProvider);
          await ref.read(adminUsersProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SearchField(hint: 'Search name, email or phone', onChanged: (v) => setState(() => _search = v)),
                const SizedBox(height: 10),
                FilterChips<String>(
                  options: const [('all', 'All'), ('active', 'Active'), ('suspended', 'Suspended'), ('blocked', 'Blocked')],
                  selected: _filter,
                  onSelected: (f) => setState(() => _filter = f),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(adminUsersProvider),
                  loadingMessage: 'Loading users...',
                  data: (all) {
                    final list = all
                        .where((u) => switch (_filter) {
                              'active' => !u.isSuspended && !u.isPermanentlyBlocked,
                              'suspended' => u.isSuspended,
                              'blocked' => u.isPermanentlyBlocked,
                              _ => true,
                            })
                        .where((u) => term.isEmpty || [u.name, u.email, u.phoneNumber].any((s) => s.toLowerCase().contains(term)))
                        .toList();
                    if (list.isEmpty) return const EmptyView(icon: Icons.people_outline, message: 'No users match.');
                    return Card(
                      child: Column(
                        children: [
                          for (final u in list)
                            ListTile(
                              key: Key('user-${u.userId}'),
                              onTap: () => context.push('${AppRoutes.adminUsers}/${u.userId}'),
                              leading: CircleAvatar(child: Text(u.firstName.isEmpty ? '?' : u.firstName[0].toUpperCase())),
                              title: Text(u.name.isEmpty ? u.email : u.name),
                              subtitle: Text('${u.roleLabel} · ${u.email}'),
                              trailing: StatusBadge(u.statusLabel, variant: u.statusVariant),
                            ),
                        ],
                      ),
                    );
                  },
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// A user's profile for the admin: details, suspension and the full activity log.
class AdminUserProfileScreen extends ConsumerWidget {
  const AdminUserProfileScreen({super.key, required this.userId, this.actions});

  final String userId;
  final UserActions? actions;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(adminUsersProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('User')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminUsersProvider);
          await ref.read(adminUsersProvider.future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(adminUsersProvider),
          data: (all) {
            final u = all.where((x) => x.userId == userId).firstOrNull;
            if (u == null) return const EmptyView(icon: Icons.search_off, message: 'This user was not found.');
            final extra = actions?.call(context, ref, u) ?? const <Widget>[];
            return ContentWidth(
              maxWidth: 720,
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SectionCard(
                      title: u.name.isEmpty ? u.email : u.name,
                      icon: Icons.person_outline,
                      trailing: StatusBadge(u.statusLabel, variant: u.statusVariant),
                      child: Wrap(spacing: 20, runSpacing: 12, children: [
                        LabeledValue('Role', u.roleLabel),
                        LabeledValue('Email', u.email),
                        LabeledValue('Phone', u.phoneNumber.isEmpty ? '-' : u.phoneNumber),
                        LabeledValue('Joined', Fmt.date(u.createdAt)),
                        if (u.isSuspended) LabeledValue('Suspended until', u.suspendedUntil == null ? 'Until lifted' : Fmt.date(u.suspendedUntil)),
                      ]),
                    ),
                    if (u.isSuspended && u.suspensionReason?.isNotEmpty == true) ...[
                      const SizedBox(height: 8),
                      InfoBanner('Suspension reason: ${u.suspensionReason}', color: AppColors.rose600),
                    ],
                    const SizedBox(height: 12),
                    if (extra.isNotEmpty) ...[Wrap(spacing: 8, runSpacing: 8, children: extra), const SizedBox(height: 12)],
                    Card(
                      child: ListTile(
                        key: const Key('user-activity'),
                        leading: const Icon(Icons.history, color: AppColors.red600),
                        title: const Text('Activity log'),
                        subtitle: const Text('What they did and what was done to their account'),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => context.push('${AppRoutes.adminUsers}/${u.userId}/activity'),
                      ),
                    ),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}

/// Hospitals directory (approved and suspended hospitals); registrations are on their own screen.
class AdminHospitalsScreen extends ConsumerStatefulWidget {
  const AdminHospitalsScreen({super.key});

  @override
  ConsumerState<AdminHospitalsScreen> createState() => _AdminHospitalsScreenState();
}

class _AdminHospitalsScreenState extends ConsumerState<AdminHospitalsScreen> {
  String _search = '';
  String _filter = 'all';

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(adminHospitalsProvider);
    final term = _search.trim().toLowerCase();
    return Scaffold(
      appBar: AppBar(title: const Text('Hospitals')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminHospitalsProvider);
          await ref.read(adminHospitalsProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SearchField(hint: 'Search name, city or email', onChanged: (v) => setState(() => _search = v)),
                const SizedBox(height: 10),
                FilterChips<String>(
                  options: const [('all', 'All approved'), ('active', 'Active'), ('suspended', 'Suspended')],
                  selected: _filter,
                  onSelected: (f) => setState(() => _filter = f),
                ),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(adminHospitalsProvider),
                  loadingMessage: 'Loading hospitals...',
                  data: (all) {
                    final list = all
                        .where((h) => h.approvalStatus == 'Approved')
                        .where((h) => _filter == 'all' || (_filter == 'suspended') == h.isSuspended)
                        .where((h) => term.isEmpty || [h.name, h.city ?? '', h.email].any((s) => s.toLowerCase().contains(term)))
                        .toList()
                      ..sort((a, b) => a.name.toLowerCase().compareTo(b.name.toLowerCase()));
                    if (list.isEmpty) return const EmptyView(icon: Icons.local_hospital_outlined, message: 'No hospitals match.');
                    return Card(
                      child: Column(
                        children: [
                          for (final h in list)
                            ListTile(
                              key: Key('hospital-${h.hospitalId}'),
                              onTap: () => context.push('${AppRoutes.adminHospitals}/${h.hospitalId}'),
                              leading: const Icon(Icons.local_hospital_outlined, color: AppColors.red600),
                              title: Text(h.name),
                              subtitle: Text('${h.city ?? h.address} · ${h.email}'),
                              trailing: StatusBadge(h.isSuspended ? 'Suspended' : 'Active',
                                  variant: h.isSuspended ? BadgeVariant.warning : BadgeVariant.success),
                            ),
                        ],
                      ),
                    );
                  },
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// A hospital's profile for the admin: details, suspension and the hospital's full activity log.
class AdminHospitalProfileScreen extends ConsumerWidget {
  const AdminHospitalProfileScreen({super.key, required this.hospitalId, this.actions});

  final String hospitalId;
  final HospitalActions? actions;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(adminHospitalsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Hospital')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(adminHospitalsProvider);
          await ref.read(adminHospitalsProvider.future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(adminHospitalsProvider),
          data: (all) {
            final h = all.where((x) => x.hospitalId == hospitalId).firstOrNull;
            if (h == null) return const EmptyView(icon: Icons.search_off, message: 'This hospital was not found.');
            final extra = actions?.call(context, ref, h) ?? const <Widget>[];
            return ContentWidth(
              maxWidth: 720,
              child: Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    SectionCard(
                      title: h.name,
                      icon: Icons.local_hospital_outlined,
                      trailing: StatusBadge(h.isSuspended ? 'Suspended' : h.approvalStatus,
                          variant: h.isSuspended ? BadgeVariant.warning : BadgeVariant.success),
                      child: Wrap(spacing: 20, runSpacing: 12, children: [
                        LabeledValue('Address', [h.address, h.city].whereType<String>().where((s) => s.isNotEmpty).join(', ')),
                        LabeledValue('Contact', h.contactNumber),
                        LabeledValue('Email', h.email),
                        LabeledValue('Registration no.', h.registrationNumber ?? h.licenseNumber),
                        if (h.isSuspended) LabeledValue('Suspended until', h.suspendedUntil == null ? 'Until lifted' : Fmt.date(h.suspendedUntil)),
                      ]),
                    ),
                    if (h.isSuspended && h.suspensionReason?.isNotEmpty == true) ...[
                      const SizedBox(height: 8),
                      InfoBanner('Suspension reason: ${h.suspensionReason}', color: AppColors.rose600),
                    ],
                    const SizedBox(height: 12),
                    if (extra.isNotEmpty) ...[Wrap(spacing: 8, runSpacing: 8, children: extra), const SizedBox(height: 12)],
                    Card(
                      child: ListTile(
                        key: const Key('hospital-activity'),
                        leading: const Icon(Icons.history, color: AppColors.red600),
                        title: const Text('Activity log'),
                        subtitle: const Text('Its staff, its doctors and admin actions on it'),
                        trailing: const Icon(Icons.chevron_right),
                        onTap: () => context.push('${AppRoutes.adminHospitals}/${h.hospitalId}/activity'),
                      ),
                    ),
                  ],
                ),
              ),
            );
          },
        ),
      ),
    );
  }
}

/// Route screens for the three activity logs.
Widget userActivityScreen(String userId) => ActivityLogScreen(
      title: 'User activity',
      load: (ref, q) => ref.read(activityRepositoryProvider).ofUser(userId, q),
    );

Widget hospitalActivityScreen(String hospitalId) => ActivityLogScreen(
      title: 'Hospital activity',
      load: (ref, q) => ref.read(activityRepositoryProvider).ofHospital(hospitalId, q),
    );

Widget myActivityScreen() => ActivityLogScreen(
      title: 'My activity',
      description: 'What you did, and actions taken on your account. Hospital staff see their hospital\'s log.',
      load: (ref, q) => ref.read(activityRepositoryProvider).mine(q),
    );
