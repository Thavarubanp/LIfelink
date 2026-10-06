import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../doctors/doctor_repository.dart';
import 'profile_repository.dart';

class HospitalProfileScreen extends ConsumerWidget {
  const HospitalProfileScreen({super.key, required this.hospitalId});
  final String hospitalId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(hospitalProfileProvider(hospitalId));
    return Scaffold(
      appBar: AppBar(title: const Text('Hospital profile')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(hospitalProfileProvider(hospitalId));
          await ref
              .read(hospitalProfileProvider(hospitalId).future)
              .then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(hospitalProfileProvider(hospitalId)),
          data: (h) => ContentWidth(
            maxWidth: 720,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: SectionCard(
                title: h.name,
                icon: Icons.local_hospital_outlined,
                trailing: StatusBadge(
                  h.isVerified ? 'Verified' : 'Pending',
                  variant: h.isVerified
                      ? BadgeVariant.success
                      : BadgeVariant.warning,
                ),
                child: Wrap(
                  spacing: 24,
                  runSpacing: 12,
                  children: [
                    LabeledValue(
                      'Address',
                      [h.address, h.city]
                          .whereType<String>()
                          .where((v) => v.isNotEmpty)
                          .join(', '),
                    ),
                    LabeledValue('Phone', h.contactNumber),
                    LabeledValue('Email', h.email),
                    LabeledValue('Active doctors', '${h.doctorCount}'),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

final doctorProfileProvider = FutureProvider.autoDispose
    .family<DoctorProfile, String>(
      (ref, id) => ref.watch(doctorRepositoryProvider).profile(id),
    );

class PublicDoctorProfileScreen extends ConsumerWidget {
  const PublicDoctorProfileScreen({super.key, required this.doctorId});
  final String doctorId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(doctorProfileProvider(doctorId));
    return Scaffold(
      appBar: AppBar(title: const Text('Doctor profile')),
      body: AsyncView(
        value: value,
        onRetry: () => ref.invalidate(doctorProfileProvider(doctorId)),
        data: (d) => ContentWidth(
          maxWidth: 720,
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: SectionCard(
              title: 'Dr. ${d.firstName} ${d.lastName}'.trim(),
              icon: Icons.medical_services_outlined,
              trailing: StatusBadge(
                d.isActive ? 'Active' : 'Inactive',
                variant: d.isActive
                    ? BadgeVariant.success
                    : BadgeVariant.neutral,
              ),
              child: Wrap(
                spacing: 24,
                runSpacing: 12,
                children: [
                  LabeledValue(
                    'Specialization',
                    d.specialization.isEmpty ? 'Not set' : d.specialization,
                  ),
                  LabeledValue('SLMC number', d.licenseNumber),
                  LabeledValue('Hospital', d.hospitalName),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
