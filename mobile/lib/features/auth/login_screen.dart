import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/common.dart';
import 'auth_widgets.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _email = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
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
      await ref
          .read(authControllerProvider.notifier)
          .login(_email.text.trim(), _password.text);
      // The router moves to the role's home
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _showRegistrationChooser() => showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    useSafeArea: true,
    builder: (sheetContext) => ModalSheetSafeArea(
      child: _RegistrationChooser(
        onUserRegistration: () {
          Navigator.of(sheetContext).pop();
          context.push(AppRoutes.register);
        },
        onHospitalRegistration: () {
          Navigator.of(sheetContext).pop();
          context.push(AppRoutes.registerHospital);
        },
      ),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final notice = ref.watch(
      authControllerProvider.select((s) => s.signOutMessage),
    );
    return AuthPage(
      child: Form(
        key: _formKey,
        child: AutofillGroup(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const BrandHeader(subtitle: 'Sign in to continue'),
              const SizedBox(height: 28),
              if (notice != null && _error == null) ...[
                InfoBanner(notice, icon: Icons.schedule),
                const SizedBox(height: 16),
              ],
              TextFormField(
                key: const Key('login-email'),
                controller: _email,
                keyboardType: TextInputType.emailAddress,
                autofillHints: const [AutofillHints.email],
                textInputAction: TextInputAction.next,
                decoration: const InputDecoration(
                  labelText: 'Email',
                  prefixIcon: Icon(Icons.mail_outline),
                ),
                validator: Validators.email,
              ),
              const SizedBox(height: 14),
              PasswordField(
                key: const Key('login-password'),
                controller: _password,
                textInputAction: TextInputAction.done,
                onSubmitted: (_) => _submit(),
                validator: (v) =>
                    (v == null || v.isEmpty) ? 'Password is required.' : null,
              ),
              Align(
                alignment: Alignment.centerRight,
                child: TextButton(
                  onPressed: () => context.push(AppRoutes.forgotPassword),
                  child: const Text('Forgot password?'),
                ),
              ),
              if (_error != null) ...[
                FormErrorBox(_error!),
                const SizedBox(height: 12),
              ],
              BusyButton(
                key: const Key('login-submit'),
                label: 'Sign in',
                busy: _busy,
                onPressed: _submit,
                icon: Icons.login,
              ),
              const SizedBox(height: 20),
              OutlinedButton.icon(
                key: const Key('login-create-account'),
                onPressed: _showRegistrationChooser,
                icon: const Icon(Icons.person_add_outlined),
                label: const Text('Create account'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _RegistrationChooser extends StatelessWidget {
  const _RegistrationChooser({
    required this.onUserRegistration,
    required this.onHospitalRegistration,
  });

  final VoidCallback onUserRegistration;
  final VoidCallback onHospitalRegistration;

  @override
  Widget build(BuildContext context) => SingleChildScrollView(
    padding: EdgeInsets.fromLTRB(
      24,
      8,
      24,
      modalSheetBottomPadding(context, base: 24),
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(
          'Join the LifeLink Network',
          style: Theme.of(context).textTheme.titleLarge
              ?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: 4),
        const Text(
          'Select the registration option that best suits your needs',
          style: TextStyle(color: AppColors.slate500),
        ),
        const SizedBox(height: 20),
        _RegistrationOption(
          key: const Key('register-user-option'),
          icon: Icons.favorite_outline,
          title: 'User Registration',
          description: 'For blood donors, patients requesting blood, and community members managing donations and requests.',
          onTap: onUserRegistration,
        ),
        const SizedBox(height: 12),
        _RegistrationOption(
          key: const Key('register-hospital-option'),
          icon: Icons.local_hospital_outlined,
          title: 'Hospital Registration',
          description: 'For hospitals, clinics, and medical facilities requiring inventory management, blood verification, and emergency transfers.',
          onTap: onHospitalRegistration,
        ),
        const SizedBox(height: 16),
        Container(
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: Theme.of(context).colorScheme.surfaceContainerHighest,
            borderRadius: BorderRadius.circular(12),
          ),
          child: const Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Icon(
                Icons.medical_services_outlined,
                size: 18,
                color: AppColors.emerald600,
              ),
              SizedBox(width: 10),
              Expanded(
                child: Text(
                  'Doctors: Accounts are provisioned directly by your affiliated hospital administrator.',
                  style: TextStyle(fontSize: 12, color: AppColors.slate600),
                ),
              ),
            ],
          ),
        ),
      ],
    ),
  );
}

class _RegistrationOption extends StatelessWidget {
  const _RegistrationOption({
    super.key,
    required this.icon,
    required this.title,
    required this.description,
    required this.onTap,
  });

  final IconData icon;
  final String title;
  final String description;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Card(
    child: InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(16),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(icon, color: Theme.of(context).colorScheme.primary),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    description,
                    style: const TextStyle(
                      fontSize: 12,
                      color: AppColors.slate500,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            const Icon(Icons.arrow_forward, size: 18),
          ],
        ),
      ),
    ),
  );
}
