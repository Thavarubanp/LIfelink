import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/providers.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import 'auth_widgets.dart';

/// Change password. A doctor with a temporary password lands here first and cannot leave until it is changed.
class ChangePasswordScreen extends ConsumerStatefulWidget {
  const ChangePasswordScreen({super.key});

  @override
  ConsumerState<ChangePasswordScreen> createState() => _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends ConsumerState<ChangePasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _current = TextEditingController();
  final _password = TextEditingController();
  final _confirm = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [_current, _password, _confirm]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _submit() async {
    FocusScope.of(context).unfocus();
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final message = await ref.read(authRepositoryProvider).changePassword(_current.text, _password.text);
      await ref.read(authControllerProvider.notifier).refreshUser();
      if (!mounted) return;
      showSnack(context, message, type: SnackType.success);
      if (context.canPop()) context.pop();
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final user = ref.watch(authControllerProvider).user;
    final forced = user != null && user.hasRole(Roles.doctor) && user.mustChangePassword;
    const gap = SizedBox(height: 14);
    return AuthPage(
      title: 'Change password',
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (forced) ...[
              const InfoBanner('Your hospital gave you a temporary password. Choose your own password to continue.'),
              gap,
            ],
            PasswordField(
              controller: _current,
              label: forced ? 'Temporary password' : 'Current password',
              validator: (v) => (v == null || v.isEmpty) ? 'Current password is required.' : null,
            ),
            gap,
            PasswordField(
              controller: _password,
              label: 'New password',
              helperText: 'At least 8 characters with upper and lower case letters, a number and a symbol.',
              validator: (v) => Validators.strongPassword(v) ?? (v == _current.text ? 'Choose a different password.' : null),
            ),
            gap,
            PasswordField(
              controller: _confirm,
              label: 'Confirm new password',
              textInputAction: TextInputAction.done,
              validator: (v) => v != _password.text ? 'Passwords do not match.' : null,
            ),
            const SizedBox(height: 20),
            if (_error != null) ...[FormErrorBox(_error!), gap],
            BusyButton(label: 'Change password', busy: _busy, onPressed: _submit),
            if (forced)
              TextButton(
                onPressed: () => ref.read(authControllerProvider.notifier).logout(),
                child: const Text('Sign out'),
              ),
          ],
        ),
      ),
    );
  }
}
