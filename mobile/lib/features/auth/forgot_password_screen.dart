import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/providers.dart';
import '../../core/routing/routes.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import 'auth_widgets.dart';

enum _Step { email, otp, reset }

/// Forgot password: email → 6-digit code (10-minute expiry, resend) → new password. Same endpoints as the web app.
class ForgotPasswordScreen extends ConsumerStatefulWidget {
  const ForgotPasswordScreen({super.key});

  @override
  ConsumerState<ForgotPasswordScreen> createState() => _ForgotPasswordScreenState();
}

class _ForgotPasswordScreenState extends ConsumerState<ForgotPasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _otp = TextEditingController();
  final _password = TextEditingController();
  final _confirm = TextEditingController();
  _Step _step = _Step.email;
  String _resetToken = '';
  bool _busy = false;
  String? _error;
  String? _info;

  @override
  void dispose() {
    for (final c in [_email, _otp, _password, _confirm]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    FocusScope.of(context).unfocus();
    if (!_formKey.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action();
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _sendCode() => _run(() async {
        final message = await ref.read(authRepositoryProvider).forgotPassword(_email.text);
        setState(() {
          _info = message;
          _step = _Step.otp;
        });
      });

  Future<void> _verify() => _run(() async {
        _resetToken = await ref.read(authRepositoryProvider).verifyOtp(_email.text, _otp.text.trim());
        setState(() {
          _info = 'Code verified. Choose a new password.';
          _step = _Step.reset;
        });
      });

  Future<void> _resend() async {
    setState(() => _busy = true);
    try {
      final message = await ref.read(authRepositoryProvider).resendOtp(_email.text);
      if (mounted) setState(() => _info = message);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _reset() => _run(() async {
        final message = await ref.read(authRepositoryProvider).resetPassword(_email.text, _resetToken, _password.text);
        if (!mounted) return;
        showSnack(context, message, type: SnackType.success);
        context.go(AppRoutes.login);
      });

  @override
  Widget build(BuildContext context) {
    const gap = SizedBox(height: 14);
    return AuthPage(
      title: 'Reset password',
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (_info != null) ...[InfoBanner(_info!), gap],
            TextFormField(
              controller: _email,
              enabled: _step == _Step.email,
              keyboardType: TextInputType.emailAddress,
              decoration: const InputDecoration(labelText: 'Email', prefixIcon: Icon(Icons.mail_outline)),
              validator: Validators.email,
            ),
            gap,
            if (_step == _Step.otp) ...[
              TextFormField(
                controller: _otp,
                keyboardType: TextInputType.number,
                inputFormatters: [FilteringTextInputFormatter.digitsOnly, LengthLimitingTextInputFormatter(6)],
                decoration: const InputDecoration(labelText: '6-digit code', helperText: 'The code expires after 10 minutes.'),
                validator: Validators.otp,
              ),
              Align(
                alignment: Alignment.centerRight,
                child: TextButton(onPressed: _busy ? null : _resend, child: const Text('Send a new code')),
              ),
            ],
            if (_step == _Step.reset) ...[
              PasswordField(
                controller: _password,
                label: 'New password',
                helperText: 'At least 8 characters with upper and lower case letters, a number and a symbol.',
                validator: Validators.strongPassword,
              ),
              gap,
              PasswordField(
                controller: _confirm,
                label: 'Confirm new password',
                textInputAction: TextInputAction.done,
                validator: (v) => v != _password.text ? 'Passwords do not match.' : null,
              ),
              gap,
            ],
            if (_error != null) ...[FormErrorBox(_error!), gap],
            BusyButton(
              busy: _busy,
              label: switch (_step) {
                _Step.email => 'Send code',
                _Step.otp => 'Verify code',
                _Step.reset => 'Reset password',
              },
              onPressed: switch (_step) {
                _Step.email => _sendCode,
                _Step.otp => _verify,
                _Step.reset => _reset,
              },
            ),
          ],
        ),
      ),
    );
  }
}
