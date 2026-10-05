import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import 'doctor_repository.dart';

/// The doctor's own profile on the Profile screen (web DoctorProfilePage): details and an edit form (name, phone,
/// specialization, SLMC number; email and hospital cannot be changed here).
class DoctorProfileCard extends ConsumerWidget {
  const DoctorProfileCard({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final value = ref.watch(myDoctorProfileProvider);
    return SectionCard(
      title: 'Doctor profile',
      icon: Icons.medical_services_outlined,
      trailing: value.value?.canEdit == true
          ? TextButton.icon(
              key: const Key('edit-doctor-profile'),
              onPressed: () async {
                final saved = await showModalBottomSheet<bool>(
                  context: context,
                  isScrollControlled: true,
                  builder: (_) => EditDoctorProfileSheet(profile: value.value!),
                );
                if (saved == true) {
                  ref.invalidate(myDoctorProfileProvider);
                  // Name and phone are kept in sync with the sign-in account
                  try {
                    await ref.read(authControllerProvider.notifier).refreshUser();
                  } catch (_) {
                    // The profile itself saved
                  }
                }
              },
              icon: const Icon(Icons.edit_outlined, size: 18),
              label: const Text('Edit'),
            )
          : null,
      child: AsyncView(
        value: value,
        onRetry: () => ref.invalidate(myDoctorProfileProvider),
        data: (p) => Wrap(
          spacing: 24,
          runSpacing: 12,
          children: [
            LabeledValue('Name', 'Dr. ${p.firstName} ${p.lastName}'.trim()),
            LabeledValue('SLMC number', p.licenseNumber),
            LabeledValue('Specialization', p.specialization.isEmpty ? '-' : p.specialization),
            LabeledValue('Phone', p.phoneNumber),
            LabeledValue('Email', p.email),
            LabeledValue('Hospital', [p.hospitalName, p.hospitalAddress].where((s) => s.isNotEmpty).join(', ')),
            LabeledValue('Status', p.isActive ? 'Active' : 'Inactive'),
          ],
        ),
      ),
    );
  }
}

class EditDoctorProfileSheet extends ConsumerStatefulWidget {
  const EditDoctorProfileSheet({super.key, required this.profile});

  final DoctorProfile profile;

  @override
  ConsumerState<EditDoctorProfileSheet> createState() => _EditDoctorProfileSheetState();
}

class _EditDoctorProfileSheetState extends ConsumerState<EditDoctorProfileSheet> {
  late final _first = TextEditingController(text: widget.profile.firstName);
  late final _last = TextEditingController(text: widget.profile.lastName);
  late final _phone = TextEditingController(text: widget.profile.phoneNumber);
  late final _slmc = TextEditingController(text: widget.profile.licenseNumber);
  late final _specialization = TextEditingController(text: widget.profile.specialization);
  Map<String, String> _errors = const {};
  String? _error;
  bool _busy = false;

  @override
  void dispose() {
    for (final c in [_first, _last, _phone, _slmc, _specialization]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    final form = DoctorForm(
      firstName: _first.text,
      lastName: _last.text,
      phoneNumber: _phone.text,
      licenseNumber: _slmc.text,
      specialization: _specialization.text,
    );
    final errors = form.errors(isNew: false);
    setState(() {
      _errors = errors;
      _error = null;
    });
    if (errors.isNotEmpty) return;
    setState(() => _busy = true);
    try {
      await ref.read(doctorRepositoryProvider).updateProfile(widget.profile.doctorId, form);
      if (!mounted) return;
      showSnack(context, 'Your profile was saved.', type: SnackType.success);
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
      // A duplicate SLMC number comes back as a 409 with the API's message
      if (mounted) {
        setState(() {
          _error = api.message;
          _errors = api.fieldErrors;
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _field(String key, String label, TextEditingController c, {TextInputType? type, List<TextInputFormatter>? formatters}) => Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextField(
          key: Key('profile-$key'),
          controller: c,
          keyboardType: type,
          inputFormatters: formatters,
          maxLength: key == 'phoneNumber' ? null : 100,
          decoration: InputDecoration(labelText: label, errorText: _errors[key], counterText: ''),
        ),
      );

  @override
  Widget build(BuildContext context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Text('Edit profile', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
              const SizedBox(height: 14),
              Row(children: [
                Expanded(child: _field('firstName', 'First name', _first)),
                const SizedBox(width: 12),
                Expanded(child: _field('lastName', 'Last name', _last)),
              ]),
              _field('phoneNumber', 'Phone number (10 digits)', _phone,
                  type: TextInputType.phone, formatters: [FilteringTextInputFormatter.digitsOnly, LengthLimitingTextInputFormatter(10)]),
              _field('licenseNumber', 'SLMC registration number', _slmc),
              _field('specialization', 'Specialization (optional)', _specialization),
              if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
              BusyButton(key: const Key('profile-save'), label: 'Save', busy: _busy, onPressed: _save),
            ],
          ),
        ),
      );
}
