import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/attachments/attachment.dart';
import '../../core/attachments/attachment_widgets.dart';
import '../../core/routing/routes.dart';
import '../../core/widgets/common.dart';
import 'auth_widgets.dart';
import 'hospital_registration_repository.dart';

const hospitalInstitutionTypes = [
  'Private Hospital',
  'Nursing Home',
  'Medical Laboratory',
  'Medical / Channeling Centre',
  'Government Hospital',
];

class HospitalRegisterScreen extends ConsumerStatefulWidget {
  const HospitalRegisterScreen({super.key, this.documentPicker});

  final Future<Attachment?> Function()? documentPicker;

  @override
  ConsumerState<HospitalRegisterScreen> createState() =>
      _HospitalRegisterScreenState();
}

class _HospitalRegisterScreenState
    extends ConsumerState<HospitalRegisterScreen> {
  static final _emailPattern = RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$');
  static final _phsrcPattern = RegExp(
    r'^PHSRC\/(PH|L|LAB|AS|AMB|MC)\/\d{1,4}$',
    caseSensitive: false,
  );

  final _formKey = GlobalKey<FormState>();
  final _name = TextEditingController();
  final _registrationNumber = TextEditingController();
  final _contactNumber = TextEditingController();
  final _email = TextEditingController();
  final _password = TextEditingController();
  final _address = TextEditingController();
  final _city = TextEditingController();
  final _contactPersonName = TextEditingController();
  final _contactPersonPhone = TextEditingController();

  String _institutionType = hospitalInstitutionTypes.first;
  Attachment? _licenseDocument;
  Attachment? _accreditationDocument;
  Map<String, String> _fieldErrors = const {};
  String? _error;
  bool _busy = false;

  bool get _isGovernment => _institutionType == 'Government Hospital';

  @override
  void dispose() {
    for (final controller in [
      _name,
      _registrationNumber,
      _contactNumber,
      _email,
      _password,
      _address,
      _city,
      _contactPersonName,
      _contactPersonPhone,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  String? _required(String? value, String label, {int? maxLength}) {
    final trimmed = value?.trim() ?? '';
    if (trimmed.isEmpty) return '$label is required.';
    if (maxLength != null && trimmed.length > maxLength) {
      return '$label cannot exceed $maxLength characters.';
    }
    return null;
  }

  String? _registrationValidator(String? value) {
    final required = _required(value, 'Registration number', maxLength: 100);
    if (required != null) return required;
    if (!_isGovernment && !_phsrcPattern.hasMatch(value!.trim())) {
      return 'Enter a valid PHSRC registration number (e.g., PHSRC/PH/123).';
    }
    return null;
  }

  String? _emailValidator(String? value) {
    final trimmed = value?.trim() ?? '';
    if (trimmed.isEmpty) return 'Official hospital email address is required.';
    if (!_emailPattern.hasMatch(trimmed)) return 'Enter a valid email address.';
    if (trimmed.length > 200) return 'Email cannot exceed 200 characters.';
    return null;
  }

  String? _passwordValidator(String? value) {
    final password = value ?? '';
    if (password.isEmpty) return 'Password is required.';
    if (password.length < 8 ||
        !RegExp(r'[A-Z]').hasMatch(password) ||
        !RegExp(r'[^a-zA-Z0-9]').hasMatch(password)) {
      return 'Password must contain min 8 characters, at least 1 uppercase letter & 1 special character.';
    }
    return null;
  }

  String? _phoneValidator(String? value, String label) {
    if (!RegExp(r'^\d{10}$').hasMatch(value?.trim() ?? '')) {
      return '$label must contain exactly 10 digits.';
    }
    return null;
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    setState(() => _fieldErrors = const {});
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final registration = await ref
          .read(hospitalRegistrationRepositoryProvider)
          .create(
            HospitalRegistrationDraft(
              name: _name.text,
              registrationNumber: _registrationNumber.text,
              email: _email.text,
              password: _password.text,
              contactNumber: _contactNumber.text,
              address: _address.text,
              city: _city.text,
              contactPersonName: _contactPersonName.text,
              contactPersonPhone: _contactPersonPhone.text,
              licenseDocument: _licenseDocument,
              accreditationDocument: _accreditationDocument,
            ),
          );
      if (!mounted) return;
      context.go(AppRoutes.waitingApproval, extra: registration);
    } catch (error) {
      final api = ApiError.from(error);
      if (mounted) {
        setState(() {
          _error = api.message;
          _fieldErrors = api.fieldErrors;
        });
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  InputDecoration _decoration(
    String label,
    String field, {
    IconData? icon,
    String? helper,
  }) => InputDecoration(
    labelText: label,
    prefixIcon: icon == null ? null : Icon(icon),
    helperText: helper,
    helperMaxLines: 2,
    errorText: _fieldErrors[field],
  );

  @override
  Widget build(BuildContext context) {
    const gap = SizedBox(height: 14);
    return AuthPage(
      title: 'Hospital registration',
      child: Form(
        key: _formKey,
        child: AutofillGroup(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const BrandHeader(),
              const SizedBox(height: 16),
              const Text(
                'Sri Lanka Healthcare Facility Registration',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 4),
              const Text(
                'Register medical institutions with PHSRC or Ministry of Health (MOH) accreditation',
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 12),
              ),
              const SizedBox(height: 20),
              DropdownButtonFormField<String>(
                key: const Key('hospital-institution-type'),
                initialValue: _institutionType,
                isExpanded: true,
                decoration: const InputDecoration(
                  labelText: 'Institution Type *',
                  prefixIcon: Icon(Icons.local_hospital_outlined),
                ),
                items: [
                  for (final type in hospitalInstitutionTypes)
                    DropdownMenuItem(value: type, child: Text(type)),
                ],
                onChanged: (value) {
                  if (value == null) return;
                  setState(() {
                    _institutionType = value;
                    _registrationNumber.clear();
                    _fieldErrors = Map.of(_fieldErrors)
                      ..remove('registrationNumber');
                  });
                },
              ),
              gap,
              TextFormField(
                key: const Key('hospital-name'),
                controller: _name,
                maxLength: 200,
                textCapitalization: TextCapitalization.words,
                decoration: _decoration(
                  'Hospital / Medical Center Name *',
                  'name',
                  icon: Icons.business_outlined,
                ),
                validator: (value) => _required(
                  value,
                  'Hospital / Medical Center Name',
                  maxLength: 200,
                ),
              ),
              gap,
              TextFormField(
                key: const Key('hospital-registration-number'),
                controller: _registrationNumber,
                maxLength: 100,
                textCapitalization: TextCapitalization.characters,
                onChanged: (value) {
                  if (!_isGovernment && value != value.toUpperCase()) {
                    final upper = value.toUpperCase();
                    _registrationNumber.value = TextEditingValue(
                      text: upper,
                      selection: TextSelection.collapsed(offset: upper.length),
                    );
                  }
                },
                decoration: _decoration(
                  _isGovernment
                      ? 'Health Institution Number (HIN) / Government Facility Code *'
                      : 'PHSRC Registration Number *',
                  'registrationNumber',
                  icon: Icons.badge_outlined,
                  helper: _isGovernment ? null : 'For private institutions registered under the Private Health Services Regulatory Council (PHSRC).',
                ),
                validator: _registrationValidator,
              ),
              gap,
              TextFormField(
                key: const Key('hospital-contact-number'),
                controller: _contactNumber,
                keyboardType: TextInputType.phone,
                inputFormatters: [
                  FilteringTextInputFormatter.digitsOnly,
                  LengthLimitingTextInputFormatter(10),
                ],
                decoration: _decoration(
                  'Hospital Contact Number *',
                  'contactNumber',
                  icon: Icons.phone_outlined,
                ),
                validator: (value) => _phoneValidator(value, 'Phone number'),
              ),
              gap,
              TextFormField(
                key: const Key('hospital-email'),
                controller: _email,
                keyboardType: TextInputType.emailAddress,
                autofillHints: const [AutofillHints.email],
                decoration: _decoration(
                  'Official Hospital Email Address *',
                  'email',
                  icon: Icons.mail_outline,
                ),
                validator: _emailValidator,
              ),
              gap,
              PasswordField(
                key: const Key('hospital-password'),
                controller: _password,
                label: 'Create HOSPITAL Account Password (Min 8 Chars, Uppercase, Special) *',
                errorText: _fieldErrors['password'],
                validator: _passwordValidator,
              ),
              gap,
              TextFormField(
                key: const Key('hospital-address'),
                controller: _address,
                maxLength: 500,
                textCapitalization: TextCapitalization.sentences,
                decoration: _decoration(
                  'Physical Hospital Address & City *',
                  'address',
                  icon: Icons.location_on_outlined,
                ),
                validator: (value) => _required(
                  value,
                  'Physical Hospital Address & City',
                  maxLength: 500,
                ),
              ),
              gap,
              TextFormField(
                key: const Key('hospital-city'),
                controller: _city,
                maxLength: 100,
                textCapitalization: TextCapitalization.words,
                decoration: _decoration(
                  'City / Region / District',
                  'city',
                  icon: Icons.location_city_outlined,
                ),
                validator: (value) => value != null && value.length > 100
                    ? 'City / Region / District cannot exceed 100 characters.'
                    : null,
              ),
              gap,
              TextFormField(
                key: const Key('hospital-contact-person-name'),
                controller: _contactPersonName,
                maxLength: 200,
                textCapitalization: TextCapitalization.words,
                decoration: _decoration(
                  'Authorized Person Name *',
                  'contactPersonName',
                  icon: Icons.person_outline,
                ),
                validator: (value) =>
                    _required(value, 'Authorized person name', maxLength: 200),
              ),
              gap,
              TextFormField(
                key: const Key('hospital-contact-person-phone'),
                controller: _contactPersonPhone,
                keyboardType: TextInputType.phone,
                inputFormatters: [
                  FilteringTextInputFormatter.digitsOnly,
                  LengthLimitingTextInputFormatter(10),
                ],
                decoration: _decoration(
                  'Authorized Person Phone Number *',
                  'contactPersonPhone',
                  icon: Icons.phone_android_outlined,
                ),
                validator: (value) =>
                    _phoneValidator(value, 'Authorized person phone number'),
              ),
              const SizedBox(height: 18),
              AttachmentPickerField(
                key: const Key('hospital-license-document'),
                value: _licenseDocument,
                documentPicker: widget.documentPicker,
                label: 'PHSRC / MOH License Document',
                onChanged: (value) => setState(() => _licenseDocument = value),
              ),
              const SizedBox(height: 14),
              AttachmentPickerField(
                key: const Key('hospital-accreditation-document'),
                value: _accreditationDocument,
                documentPicker: widget.documentPicker,
                label: 'Accreditation Certificate / Proof',
                onChanged: (value) =>
                    setState(() => _accreditationDocument = value),
              ),
              const SizedBox(height: 20),
              if (_error != null) ...[
                FormErrorBox(_error!),
                const SizedBox(height: 12),
              ],
              BusyButton(
                key: const Key('hospital-register-submit'),
                label: 'Submit Hospital Registration',
                busy: _busy,
                onPressed: _submit,
                icon: Icons.arrow_forward,
              ),
              TextButton(
                onPressed: () => context.go(AppRoutes.login),
                child: const Text('Already registered? Sign in'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
