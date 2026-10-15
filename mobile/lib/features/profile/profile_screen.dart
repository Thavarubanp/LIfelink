import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/routing/routes.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/floating_overlay_padding.dart';
import '../../core/widgets/state_views.dart';
import '../doctors/doctor_profile_card.dart';
import 'hospital_profile_edit_sheet.dart';
import 'profile_repository.dart';

/// The signed-in account, the hospital for hospital staff, theme and sign out.
class ProfileScreen extends ConsumerWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final user = ref.watch(authControllerProvider).user;
    if (user == null) return const Scaffold(body: LoadingView());
    final isHospital = user.hasRole(Roles.hospitalStaff);

    return Scaffold(
      appBar: AppBar(title: const Text('Profile')),
      body: ContentWidth(
        child: ListView(
          padding: EdgeInsets.fromLTRB(
            16,
            16,
            16,
            16 + FloatingOverlayPadding.bottomOf(context),
          ),
          children: [
            SectionCard(
              title: user.fullName,
              icon: Icons.person_outline,
              trailing: StatusBadge(user.roleLabel, variant: BadgeVariant.info),
              child: Wrap(
                spacing: 24,
                runSpacing: 12,
                children: [
                  LabeledValue('Email', user.email),
                  LabeledValue(
                    'Account status',
                    user.accountStatus.isEmpty ? 'Active' : user.accountStatus,
                  ),
                ],
              ),
            ),
            if (user.isDonorCapable) ...[
              const SizedBox(height: 12),
              OutlinedButton.icon(
                onPressed: () =>
                    context.push(AppRoutes.userProfile(user.userId)),
                icon: const Icon(Icons.manage_accounts_outlined),
                label: const Text('View and edit full profile'),
              ),
            ],
            if (user.hasRole(Roles.doctor)) ...[
              const SizedBox(height: 12),
              const DoctorProfileCard(),
            ],
            if (isHospital) ...[
              const SizedBox(height: 12),
              const _HospitalCard(),
            ],
          ],
        ),
      ),
    );
  }
}

class _HospitalCard extends ConsumerWidget {
  const _HospitalCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(myHospitalProvider);
    final profile = value.value;
    final user = ref.watch(authControllerProvider).user;
    return SectionCard(
      title: 'Hospital',
      icon: Icons.local_hospital_outlined,
      trailing: profile != null && canEditHospitalProfile(profile, user)
          ? TextButton.icon(
              key: const Key('edit-hospital-profile'),
              onPressed: () =>
                  showHospitalProfileEditSheet(context, ref, profile),
              icon: const Icon(Icons.edit_outlined, size: 18),
              label: const Text('Edit'),
            )
          : null,
      child: AsyncView(
        value: value,
        onRetry: () => ref.invalidate(myHospitalProvider),
        data: (h) => Wrap(
          spacing: 24,
          runSpacing: 12,
          children: [
            LabeledValue('Name', h.name),
            LabeledValue(
              'Address',
              [
                h.address,
                h.city,
              ].whereType<String>().where((s) => s.isNotEmpty).join(', '),
            ),
            LabeledValue('Contact', h.contactNumber),
            LabeledValue('Email', h.email),
            LabeledValue('Packet shelf life', '${h.packetShelfLifeDays} days'),
            LabeledValue('Expiry alert window', '${h.expiryAlertDays} days'),
          ],
        ),
      ),
    );
  }
}
