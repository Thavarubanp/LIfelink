import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/auth/current_user.dart';
import '../../core/routing/routes.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import '../auth/auth_widgets.dart';
import 'profile_repository.dart';

bool canEditHospitalProfile(HospitalProfile profile, CurrentUser? user) =>
    profile.canEdit && user != null && !user.isSuspended;

Future<bool> showHospitalProfileEditSheet(
  BuildContext context,
  WidgetRef ref,
  HospitalProfile profile,
) async {
  final user = ref.read(authControllerProvider).user;
  if (!canEditHospitalProfile(profile, user)) return false;

  final saved = await showModalBottomSheet<bool>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    builder: (_) =>
        ModalSheetSafeArea(child: HospitalProfileEditSheet(profile: profile)),
  );
  if (saved != true) return false;

  ref.invalidate(myHospitalProvider);
  ref.invalidate(hospitalProfileProvider(profile.hospitalId));
  return true;
}

class HospitalProfileEditSheet extends ConsumerStatefulWidget {
  const HospitalProfileEditSheet({super.key, required this.profile});

  final HospitalProfile profile;

  @override
  ConsumerState<HospitalProfileEditSheet> createState() =>
      _HospitalProfileEditSheetState();
}

class _HospitalProfileEditSheetState
    extends ConsumerState<HospitalProfileEditSheet> {
  late final _name = TextEditingController(text: widget.profile.name);
  late final _address = TextEditingController(text: widget.profile.address);
  late final _city = TextEditingController(text: widget.profile.city ?? '');
  late final _contact = TextEditingController(
    text: widget.profile.contactNumber,
  );
  late final _contactPersonName = TextEditingController(
    text: widget.profile.contactPersonName ?? '',
  );
  late final _contactPersonPhone = TextEditingController(
    text: widget.profile.contactPersonPhone ?? '',
  );
  late final _contactPersonEmail = TextEditingController(
    text: widget.profile.contactPersonEmail ?? '',
  );
  late final _shelfLife = TextEditingController(
    text: '${widget.profile.packetShelfLifeDays}',
  );
  late final _expiryAlert = TextEditingController(
    text: '${widget.profile.expiryAlertDays}',
  );

  Map<String, String> _errors = const {};
  String? _error;
  bool _underReview = false;
  bool _busy = false;
  bool _openingRegistration = false;

  @override
  void dispose() {
    for (final controller in [
      _name,
      _address,
      _city,
      _contact,
      _contactPersonName,
      _contactPersonPhone,
      _contactPersonEmail,
      _shelfLife,
      _expiryAlert,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  String? _requiredMax(String value, String label, int maximum) =>
      Validators.required(value, label) ??
      Validators.maxLength(value.trim(), maximum, label);

  String? _optionalMax(String value, String label, int maximum) =>
      value.trim().isEmpty
      ? null
      : Validators.maxLength(value.trim(), maximum, label);

  String? _phone(String value, String label) {
    final required = Validators.required(value, label);
    if (required != null) return required;
    return Validators.phone(value);
  }

  String? _optionalEmail(String value) {
    if (value.trim().isEmpty) return null;
    return Validators.email(value) ??
        Validators.maxLength(value.trim(), 200, 'Authorized person email');
  }

  String? _optionalRange(
    String value,
    String label,
    int minimum,
    int maximum,
  ) => value.trim().isEmpty
      ? null
      : Validators.intRange(value, label, min: minimum, max: maximum);

  Map<String, String> _validate() {
    final errors = <String, String>{};
    void add(String field, String? message) {
      if (message != null) errors[field] = message;
    }

    add('name', _requiredMax(_name.text, 'Hospital name', 200));
    add('address', _requiredMax(_address.text, 'Address', 500));
    add('city', _optionalMax(_city.text, 'City', 100));
    add('contactNumber', _phone(_contact.text, 'Hospital contact number'));
    add(
      'contactPersonName',
      _requiredMax(_contactPersonName.text, 'Authorized person name', 200),
    );
    add(
      'contactPersonPhone',
      _phone(_contactPersonPhone.text, 'Authorized person phone number'),
    );
    add('contactPersonEmail', _optionalEmail(_contactPersonEmail.text));
    add(
      'packetShelfLifeDays',
      _optionalRange(_shelfLife.text, 'Packet shelf life', 21, 35),
    );
    add(
      'expiryAlertDays',
      _optionalRange(_expiryAlert.text, 'Expiry alert window', 1, 20),
    );

    final shelfLife = int.tryParse(_shelfLife.text.trim());
    final expiryAlert = int.tryParse(_expiryAlert.text.trim());
    if (!errors.containsKey('packetShelfLifeDays') &&
        !errors.containsKey('expiryAlertDays') &&
        shelfLife != null &&
        expiryAlert != null &&
        expiryAlert >= shelfLife) {
      errors['expiryAlertDays'] =
          'The expiry alert window must be shorter than the packet shelf life.';
    }
    return errors;
  }

  Future<void> _save() async {
    FocusScope.of(context).unfocus();
    final errors = _validate();
    setState(() {
      _errors = errors;
      _error = null;
      _underReview = false;
    });
    if (errors.isNotEmpty) return;

    setState(() => _busy = true);
    try {
      await ref
          .read(profileRepositoryProvider)
          .updateHospital(
            widget.profile.hospitalId,
            name: _name.text.trim(),
            address: _address.text.trim(),
            city: _nullable(_city.text),
            contactNumber: _contact.text.trim(),
            contactPersonName: _contactPersonName.text.trim(),
            contactPersonPhone: _contactPersonPhone.text.trim(),
            contactPersonEmail: _nullable(_contactPersonEmail.text),
            packetShelfLifeDays: int.tryParse(_shelfLife.text.trim()),
            expiryAlertDays: int.tryParse(_expiryAlert.text.trim()),
          );
      if (!mounted) return;
      showSnack(
        context,
        'Your hospital profile was saved.',
        type: SnackType.success,
      );
      Navigator.of(context).pop(true);
    } catch (error) {
      final api = ApiError.from(error);
      if (mounted) {
        setState(() {
          _errors = api.fieldErrors;
          _underReview = api.isConflict;
          _error = api.isConflict
              ? 'Your hospital registration is still under review. Make corrections in the registration conversation instead.'
              : api.message;
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String? _nullable(String value) {
    final trimmed = value.trim();
    return trimmed.isEmpty ? null : trimmed;
  }

  Future<void> _openRegistrationThread() async {
    setState(() => _openingRegistration = true);
    try {
      await ref.read(authControllerProvider.notifier).refreshUser();
      if (!mounted) return;
      final router = GoRouter.of(context);
      Navigator.of(context).pop();
      router.go(AppRoutes.waitingApproval);
    } catch (error) {
      if (mounted) {
        setState(() {
          _error =
              'Could not open the registration thread. ${ApiError.from(error).message}';
          _openingRegistration = false;
        });
      }
    }
  }

  void _clearFieldError(String field) {
    if (!_errors.containsKey(field)) return;
    setState(() => _errors = {..._errors}..remove(field));
  }

  Widget _field({
    required String field,
    required String label,
    required TextEditingController controller,
    int maxLines = 1,
    int? maxLength,
    TextInputType? keyboardType,
    List<TextInputFormatter>? inputFormatters,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: TextField(
      key: Key('hospital-profile-$field'),
      controller: controller,
      maxLines: maxLines,
      maxLength: maxLength,
      keyboardType: keyboardType,
      inputFormatters: inputFormatters,
      textCapitalization: keyboardType == TextInputType.emailAddress
          ? TextCapitalization.none
          : TextCapitalization.sentences,
      onChanged: (_) => _clearFieldError(field),
      decoration: InputDecoration(
        labelText: label,
        errorText: _errors[field],
        counterText: '',
      ),
    ),
  );

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(20, 8, 20, modalSheetBottomPadding(context)),
    child: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Edit hospital profile',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 4),
          const Text(
            'Update the approved contact and operational details for your hospital.',
          ),
          const SizedBox(height: 16),
          _field(
            field: 'name',
            label: 'Hospital name',
            controller: _name,
            maxLength: 200,
          ),
          _field(
            field: 'address',
            label: 'Address',
            controller: _address,
            maxLines: 3,
            maxLength: 500,
          ),
          _field(
            field: 'city',
            label: 'City (optional)',
            controller: _city,
            maxLength: 100,
          ),
          _field(
            field: 'contactNumber',
            label: 'Hospital contact number',
            controller: _contact,
            keyboardType: TextInputType.phone,
            inputFormatters: _phoneFormatters,
          ),
          _field(
            field: 'contactPersonName',
            label: 'Authorized person name',
            controller: _contactPersonName,
            maxLength: 200,
          ),
          _field(
            field: 'contactPersonPhone',
            label: 'Authorized person phone number',
            controller: _contactPersonPhone,
            keyboardType: TextInputType.phone,
            inputFormatters: _phoneFormatters,
          ),
          _field(
            field: 'contactPersonEmail',
            label: 'Authorized person email (optional)',
            controller: _contactPersonEmail,
            maxLength: 200,
            keyboardType: TextInputType.emailAddress,
          ),
          _field(
            field: 'packetShelfLifeDays',
            label: 'Blood packet shelf life (21–35 days)',
            controller: _shelfLife,
            keyboardType: TextInputType.number,
            inputFormatters: _numberFormatters,
          ),
          _field(
            field: 'expiryAlertDays',
            label: 'Expiry alert window (1–20 days)',
            controller: _expiryAlert,
            keyboardType: TextInputType.number,
            inputFormatters: _numberFormatters,
          ),
          if (_error != null) ...[
            FormErrorBox(_error!),
            if (_underReview) ...[
              const SizedBox(height: 8),
              OutlinedButton.icon(
                key: const Key('open-registration-thread'),
                onPressed: _openingRegistration
                    ? null
                    : _openRegistrationThread,
                icon: const Icon(Icons.forum_outlined),
                label: Text(
                  _openingRegistration
                      ? 'Opening registration…'
                      : 'Open registration thread',
                ),
              ),
            ],
            const SizedBox(height: 12),
          ],
          BusyButton(
            key: const Key('hospital-profile-save'),
            label: 'Save changes',
            busy: _busy,
            onPressed: _save,
          ),
        ],
      ),
    ),
  );

  static final _phoneFormatters = <TextInputFormatter>[
    FilteringTextInputFormatter.digitsOnly,
    LengthLimitingTextInputFormatter(10),
  ];

  static final _numberFormatters = <TextInputFormatter>[
    FilteringTextInputFormatter.digitsOnly,
  ];
}
