import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import '../profile/profile_repository.dart';
import 'doctor_repository.dart';

/// Hospital staff manage their doctors: list ("Pending first login" until the doctor changes the temporary password),
/// add a doctor, remove a doctor (soft delete; their history shows "Removed doctor").
class DoctorsScreen extends ConsumerStatefulWidget {
  const DoctorsScreen({super.key});

  @override
  ConsumerState<DoctorsScreen> createState() => _DoctorsScreenState();
}

class _DoctorsScreenState extends ConsumerState<DoctorsScreen> {
  String _search = '';

  Future<void> _remove(Doctor d) async {
    final ok = await confirmDialog(
      context,
      title: 'Remove ${d.name}?',
      message: 'They can no longer sign in or be assigned. Requests and decisions they handled keep their history '
          '(shown as "Removed doctor").',
      confirmLabel: 'Remove',
      destructive: true,
    );
    if (!ok) return;
    try {
      await ref.read(doctorRepositoryProvider).remove(d.doctorId);
      if (mounted) {
        showSnack(context, '${d.name} no longer has access. Request history was kept.', type: SnackType.success, title: 'Doctor removed');
      }
    } catch (e) {
      if (mounted) showErrorSnack(context, e, title: 'Delete failed');
    } finally {
      ref.invalidate(hospitalDoctorsProvider);
    }
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(hospitalDoctorsProvider);
    final term = _search.trim().toLowerCase();
    return Scaffold(
      appBar: AppBar(title: const Text('Doctors')),
      floatingActionButton: FloatingActionButton.extended(
        key: const Key('add-doctor'),
        backgroundColor: AppColors.red600,
        foregroundColor: Colors.white,
        onPressed: () async {
          final existing = value.value?.map((d) => d.licenseNumber).toList() ?? const <String>[];
          final created = await showModalBottomSheet<bool>(
            context: context,
            isScrollControlled: true,
            builder: (_) => AddDoctorSheet(existingSlmc: existing),
          );
          if (created == true) ref.invalidate(hospitalDoctorsProvider);
        },
        icon: const Icon(Icons.person_add_alt),
        label: const Text('Add doctor'),
      ),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(hospitalDoctorsProvider);
          await ref.read(hospitalDoctorsProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Text(
                  'Doctors change their temporary password at first sign-in. Until then they show as "Pending first login" and '
                  'cannot be assigned to requests.',
                  style: TextStyle(fontSize: 12, color: AppColors.slate500),
                ),
                const SizedBox(height: 10),
                SearchField(hint: 'Search name, email, SLMC no. or specialization', onChanged: (v) => setState(() => _search = v)),
                const SizedBox(height: 12),
                AsyncView(
                  value: value,
                  onRetry: () => ref.invalidate(hospitalDoctorsProvider),
                  loadingMessage: 'Loading doctors...',
                  data: (all) {
                    final list = all
                        .where((d) => term.isEmpty || [d.name, d.email, d.licenseNumber, d.specialization].any((s) => s.toLowerCase().contains(term)))
                        .toList();
                    if (list.isEmpty) {
                      return EmptyView(
                        icon: Icons.medical_services_outlined,
                        message: all.isEmpty ? 'No doctors yet. Add your first doctor.' : 'No doctors match.',
                      );
                    }
                    return Column(
                      children: [
                        for (final d in list)
                          Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: Card(
                              child: ListTile(
                                key: Key('doctor-${d.doctorId}'),
                                leading: const CircleAvatar(child: Icon(Icons.medical_services_outlined)),
                                title: Text(d.name, style: const TextStyle(fontWeight: FontWeight.w700)),
                                subtitle: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text([d.specialization, 'SLMC ${d.licenseNumber}'].where((s) => s.isNotEmpty).join(' · ')),
                                    Text('${d.email} · ${d.phoneNumber}', style: const TextStyle(fontSize: 12)),
                                    Text('Added ${Fmt.date(d.createdAt)}', style: const TextStyle(fontSize: 12)),
                                    const SizedBox(height: 4),
                                    Wrap(spacing: 6, children: [
                                      if (d.mustChangePassword)
                                        const StatusBadge('Pending first login', variant: BadgeVariant.warning)
                                      else if (d.isActive)
                                        const StatusBadge('Active', variant: BadgeVariant.success)
                                      else
                                        const StatusBadge('Inactive'),
                                    ]),
                                  ],
                                ),
                                trailing: IconButton(
                                  key: Key('remove-${d.doctorId}'),
                                  tooltip: 'Remove doctor',
                                  icon: const Icon(Icons.delete_outline, color: AppColors.rose600),
                                  onPressed: () => _remove(d),
                                ),
                              ),
                            ),
                          ),
                      ],
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

/// New doctor account (CreateDoctorDto rules). The doctor must change the temporary password at first sign-in.
class AddDoctorSheet extends ConsumerStatefulWidget {
  const AddDoctorSheet({super.key, this.existingSlmc = const []});

  final List<String> existingSlmc;

  @override
  ConsumerState<AddDoctorSheet> createState() => _AddDoctorSheetState();
}

class _AddDoctorSheetState extends ConsumerState<AddDoctorSheet> {
  final _first = TextEditingController();
  final _last = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _slmc = TextEditingController();
  final _specialization = TextEditingController();
  final _phone = TextEditingController();
  Map<String, String> _errors = const {};
  String? _error;
  bool _busy = false;

  @override
  void dispose() {
    for (final c in [_first, _last, _email, _password, _slmc, _specialization, _phone]) {
      c.dispose();
    }
    super.dispose();
  }

  DoctorForm get _form => DoctorForm(
        firstName: _first.text,
        lastName: _last.text,
        email: _email.text,
        password: _password.text,
        licenseNumber: _slmc.text,
        specialization: _specialization.text,
        phoneNumber: _phone.text,
      );

  Future<void> _save() async {
    final errors = _form.errors(existingSlmc: widget.existingSlmc);
    setState(() {
      _errors = errors;
      _error = null;
    });
    if (errors.isNotEmpty) return;
    setState(() => _busy = true);
    try {
      final hospital = await ref.read(myHospitalProvider.future);
      await ref.read(doctorRepositoryProvider).create(hospital.hospitalId, _form);
      if (!mounted) return;
      showSnack(context, 'Doctor account created for ${_first.text.trim()} ${_last.text.trim()}. They must change the password at first sign-in.',
          type: SnackType.success);
      Navigator.of(context).pop(true);
    } catch (e) {
      final api = ApiError.from(e);
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

  Widget _field(String key, String label, TextEditingController c,
          {TextInputType? type, List<TextInputFormatter>? formatters, int? maxLength, TextCapitalization caps = TextCapitalization.none}) =>
      Padding(
        padding: const EdgeInsets.only(bottom: 12),
        child: TextField(
          key: Key('doctor-$key'),
          controller: c,
          keyboardType: type,
          inputFormatters: formatters,
          maxLength: maxLength,
          textCapitalization: caps,
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
              const Text('Add doctor', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
              const SizedBox(height: 14),
              Row(children: [
                Expanded(child: _field('firstName', 'First name', _first, maxLength: 100, caps: TextCapitalization.words)),
                const SizedBox(width: 12),
                Expanded(child: _field('lastName', 'Last name', _last, maxLength: 100, caps: TextCapitalization.words)),
              ]),
              _field('email', 'Email', _email, type: TextInputType.emailAddress, maxLength: 200),
              _field('licenseNumber', 'SLMC registration number', _slmc, maxLength: 100, caps: TextCapitalization.characters),
              _field('specialization', 'Specialization (optional)', _specialization, maxLength: 100, caps: TextCapitalization.words),
              _field('phoneNumber', 'Phone number (10 digits)', _phone,
                  type: TextInputType.phone, formatters: [FilteringTextInputFormatter.digitsOnly, LengthLimitingTextInputFormatter(10)]),
              PasswordField(
                key: const Key('doctor-password'),
                controller: _password,
                label: 'Temporary password',
                errorText: _errors['password'],
                helperText: 'At least 8 characters with upper and lower case letters, a number and a symbol.',
              ),
              const SizedBox(height: 16),
              if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
              BusyButton(key: const Key('doctor-save'), label: 'Create doctor account', busy: _busy, onPressed: _save),
            ],
          ),
        ),
      );
}
