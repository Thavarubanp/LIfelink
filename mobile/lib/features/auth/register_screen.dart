import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/config/constants.dart';
import '../../core/providers.dart';
import '../../core/routing/routes.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import 'auth_widgets.dart';

/// Donor / patient registration (same fields and rules as the web app's Register page).
class RegisterScreen extends ConsumerStatefulWidget {
  const RegisterScreen({super.key});

  @override
  ConsumerState<RegisterScreen> createState() => _RegisterScreenState();
}

class _RegisterScreenState extends ConsumerState<RegisterScreen> {
  final _formKey = GlobalKey<FormState>();
  final _first = TextEditingController();
  final _last = TextEditingController();
  final _email = TextEditingController();
  final _phone = TextEditingController();
  final _address = TextEditingController();
  final _password = TextEditingController();
  final _confirm = TextEditingController();
  String _gender = 'Other';
  String? _bloodGroup;
  bool _busy = false;
  String? _error;
  Map<String, String> _fieldErrors = const {};

  @override
  void dispose() {
    for (final c in [_first, _last, _email, _phone, _address, _password, _confirm]) {
      c.dispose();
    }
    super.dispose();
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
      final message = await ref.read(authRepositoryProvider).register({
        'firstName': _first.text.trim(),
        'lastName': _last.text.trim(),
        'email': _email.text.trim(),
        'password': _password.text,
        'phoneNumber': _phone.text.trim(),
        'gender': _gender,
        'address': _address.text.trim(),
        'bloodGroup': _bloodGroup,
      });
      if (!mounted) return;
      showSnack(context, message, type: SnackType.success);
      context.go(AppRoutes.login);
    } catch (e) {
      final api = ApiError.from(e);
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

  @override
  Widget build(BuildContext context) {
    const gap = SizedBox(height: 14);
    return AuthPage(
      title: 'Create account',
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text('Donor / patient account', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: TextFormField(
                    key: const Key('reg-first'),
                    controller: _first,
                    textCapitalization: TextCapitalization.words,
                    decoration: InputDecoration(labelText: 'First name', errorText: _fieldErrors['firstName']),
                    validator: (v) => Validators.name(v, 'First name'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextFormField(
                    key: const Key('reg-last'),
                    controller: _last,
                    textCapitalization: TextCapitalization.words,
                    decoration: InputDecoration(labelText: 'Last name', errorText: _fieldErrors['lastName']),
                    validator: (v) => Validators.name(v, 'Last name'),
                  ),
                ),
              ],
            ),
            gap,
            TextFormField(
              key: const Key('reg-email'),
              controller: _email,
              keyboardType: TextInputType.emailAddress,
              decoration: InputDecoration(labelText: 'Email', prefixIcon: const Icon(Icons.mail_outline), errorText: _fieldErrors['email']),
              validator: Validators.email,
            ),
            gap,
            TextFormField(
              key: const Key('reg-phone'),
              controller: _phone,
              keyboardType: TextInputType.phone,
              inputFormatters: [FilteringTextInputFormatter.digitsOnly, LengthLimitingTextInputFormatter(10)],
              decoration: InputDecoration(labelText: 'Phone number', prefixIcon: const Icon(Icons.phone_outlined), errorText: _fieldErrors['phoneNumber']),
              validator: Validators.phone,
            ),
            gap,
            Row(
              children: [
                Expanded(
                  child: DropdownButtonFormField<String>(
                    initialValue: _gender,
                    decoration: const InputDecoration(labelText: 'Gender'),
                    items: const [
                      DropdownMenuItem(value: 'Male', child: Text('Male')),
                      DropdownMenuItem(value: 'Female', child: Text('Female')),
                      DropdownMenuItem(value: 'Other', child: Text('Other')),
                    ],
                    onChanged: (v) => setState(() => _gender = v ?? 'Other'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: DropdownButtonFormField<String?>(
                    initialValue: _bloodGroup,
                    decoration: InputDecoration(labelText: 'Blood group', errorText: _fieldErrors['bloodGroup']),
                    items: [
                      const DropdownMenuItem<String?>(value: null, child: Text("Don't know")),
                      for (final g in AppConstants.bloodGroups) DropdownMenuItem<String?>(value: g, child: Text(g)),
                    ],
                    onChanged: (v) => setState(() => _bloodGroup = v),
                  ),
                ),
              ],
            ),
            gap,
            TextFormField(
              controller: _address,
              maxLength: 500,
              decoration: InputDecoration(labelText: 'Address (optional)', errorText: _fieldErrors['address']),
            ),
            gap,
            PasswordField(
              key: const Key('reg-password'),
              controller: _password,
              errorText: _fieldErrors['password'],
              helperText: 'At least 8 characters with upper and lower case letters, a number and a symbol.',
              validator: Validators.strongPassword,
            ),
            gap,
            PasswordField(
              key: const Key('reg-confirm'),
              controller: _confirm,
              label: 'Confirm password',
              textInputAction: TextInputAction.done,
              validator: (v) => v != _password.text ? 'Passwords do not match.' : null,
            ),
            const SizedBox(height: 20),
            if (_error != null) ...[FormErrorBox(_error!), const SizedBox(height: 12)],
            BusyButton(key: const Key('reg-submit'), label: 'Create account', busy: _busy, onPressed: _submit),
            TextButton(onPressed: () => context.go(AppRoutes.login), child: const Text('I already have an account')),
          ],
        ),
      ),
    );
  }
}
