import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/api/api_error.dart';
import '../../core/attachments/attachment.dart';
import '../../core/attachments/attachment_widgets.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/thread_view.dart';
import '../auth/auth_widgets.dart';
import '../auth/hospital_registration_repository.dart';

class WaitingApprovalScreen extends ConsumerStatefulWidget {
  const WaitingApprovalScreen({super.key, this.initialRegistration});

  final HospitalRegistration? initialRegistration;

  @override
  ConsumerState<WaitingApprovalScreen> createState() =>
      _WaitingApprovalScreenState();
}

class _WaitingApprovalScreenState extends ConsumerState<WaitingApprovalScreen> {
  static final _emailPattern = RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$');

  final _replyFormKey = GlobalKey<FormState>();
  final _message = TextEditingController();
  final Map<String, TextEditingController> _corrections = {
    'name': TextEditingController(),
    'licenseNumber': TextEditingController(),
    'registrationNumber': TextEditingController(),
    'contactNumber': TextEditingController(),
    'city': TextEditingController(),
    'contactPersonName': TextEditingController(),
    'contactPersonPhone': TextEditingController(),
    'contactPersonEmail': TextEditingController(),
    'address': TextEditingController(),
  };

  HospitalRegistration? _hospital;
  Attachment? _replyAttachment;
  Attachment? _licenseDocument;
  Attachment? _accreditationDocument;
  String? _loadError;
  String? _formError;
  bool _loading = false;
  bool _refreshing = false;
  bool _sending = false;
  bool _showCorrections = false;
  bool _sent = false;

  bool get _signedIn => ref.read(authControllerProvider).isSignedIn;

  @override
  void initState() {
    super.initState();
    _hospital = widget.initialRegistration;
    if (_hospital != null) _syncCorrections(_hospital!);
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_signedIn) _load(showLoading: _hospital == null);
    });
  }

  @override
  void dispose() {
    _message.dispose();
    for (final controller in _corrections.values) {
      controller.dispose();
    }
    super.dispose();
  }

  void _syncCorrections(HospitalRegistration hospital) {
    final values = {
      'name': hospital.name,
      'licenseNumber': hospital.licenseNumber,
      'registrationNumber': hospital.registrationNumber ?? '',
      'contactNumber': hospital.contactNumber,
      'city': hospital.city ?? '',
      'contactPersonName': hospital.contactPersonName ?? '',
      'contactPersonPhone': hospital.contactPersonPhone ?? '',
      'contactPersonEmail': hospital.contactPersonEmail ?? '',
      'address': hospital.address,
    };
    for (final entry in values.entries) {
      _corrections[entry.key]!.text = entry.value;
    }
  }

  void _showHospital(HospitalRegistration hospital) {
    setState(() {
      _hospital = hospital;
      _loadError = null;
    });
    _syncCorrections(hospital);
  }

  Future<void> _load({bool showLoading = false}) async {
    if (!_signedIn) return;
    if (showLoading) setState(() => _loading = true);
    try {
      final hospital = await ref
          .read(hospitalRegistrationRepositoryProvider)
          .mine();
      if (mounted) _showHospital(hospital);
    } catch (error) {
      if (mounted) {
        setState(() => _loadError = ApiError.from(error).message);
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _refresh() async {
    setState(() => _refreshing = true);
    await _load();
    if (mounted) setState(() => _refreshing = false);
  }

  String? _replyValidator(String? value) {
    final length = value?.trim().length ?? 0;
    if (length < 3) return 'Please write a message of at least 3 characters.';
    if (length > 1000) {
      return 'The reply message cannot exceed 1000 characters.';
    }
    return null;
  }

  String? _requiredCorrection(String? value, String message) =>
      value?.trim().isEmpty == true ? message : null;

  String? _phoneCorrection(String? value, String message) =>
      RegExp(r'^\d{10}$').hasMatch(value?.trim() ?? '') ? null : message;

  Future<void> _sendReply() async {
    final hospital = _hospital;
    if (hospital == null || !hospital.isRejected || !_signedIn) return;
    FocusScope.of(context).unfocus();
    setState(() {
      _formError = null;
      _sent = false;
    });
    if (!_replyFormKey.currentState!.validate()) return;
    setState(() => _sending = true);
    try {
      final corrections = _showCorrections
          ? {
              for (final entry in _corrections.entries)
                entry.key: entry.value.text,
            }
          : const <String, String>{};
      final updated = await ref
          .read(hospitalRegistrationRepositoryProvider)
          .reply(
            hospital.hospitalId,
            HospitalRegistrationReply(
              message: _message.text,
              attachment: _replyAttachment,
              corrections: corrections,
              licenseDocument: _showCorrections ? _licenseDocument : null,
              accreditationDocument: _showCorrections
                  ? _accreditationDocument
                  : null,
            ),
          );
      if (!mounted) return;
      _showHospital(updated);
      setState(() {
        _message.clear();
        _replyAttachment = null;
        _licenseDocument = null;
        _accreditationDocument = null;
        _showCorrections = false;
        _sent = true;
      });
    } catch (error) {
      final api = ApiError.from(error);
      if (mounted) setState(() => _formError = api.message);
      if (api.isConflict) await _load();
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  Future<void> _openDashboard() async {
    try {
      await ref.read(authControllerProvider.notifier).refreshUser();
    } finally {
      if (mounted) context.go(AppRoutes.hospitalHome);
    }
  }

  BadgeVariant _statusVariant(HospitalRegistration hospital) =>
      hospital.isApproved
      ? BadgeVariant.success
      : hospital.isRejected
      ? BadgeVariant.danger
      : hospital.isAwaitingAdminReview
      ? BadgeVariant.info
      : BadgeVariant.warning;

  String _statusLabel(HospitalRegistration hospital) => hospital.isApproved
      ? 'Registration Approved'
      : hospital.isRejected
      ? 'Rejected · Please Reply or Send Corrections'
      : hospital.isAwaitingAdminReview
      ? 'Awaiting Admin Review'
      : 'Pending Administrator Review';

  List<ThreadMessage> _thread(HospitalRegistration hospital) => [
    for (final entry in hospital.history)
      ThreadMessage(
        author: entry.fromAdmin
            ? entry.adminName?.isNotEmpty == true
                  ? 'LifeLink Admin (${entry.adminName})'
                  : 'LifeLink Admin'
            : 'Hospital',
        fromAdmin: entry.fromAdmin,
        at: entry.timestamp,
        title: entry.title,
        text: entry.message,
        detail: entry.changedFields?.trim().isNotEmpty == true
            ? 'Corrected details\n${entry.changedFields!.replaceAll(' -> ', ' → ')}'
            : null,
        attachment: entry.attachment,
      ),
  ];

  @override
  Widget build(BuildContext context) {
    final signedIn = ref.watch(authControllerProvider).isSignedIn;
    return Scaffold(
      appBar: AppBar(title: const Text('Hospital registration')),
      body: ContentWidth(
        maxWidth: 680,
        child: ListView(
          padding: const EdgeInsets.all(20),
          children: [
            const BrandHeader(),
            const SizedBox(height: 12),
            const Text(
              'Healthcare Facility Portal',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700),
            ),
            const Text(
              'Hospital registration status and review conversation',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 12, color: AppColors.slate500),
            ),
            const SizedBox(height: 20),
            if (_loading)
              const Padding(
                padding: EdgeInsets.all(36),
                child: Center(child: CircularProgressIndicator()),
              )
            else if (_hospital == null)
              _MissingRegistration(
                error: _loadError,
                signedIn: signedIn,
                onRetry: _refresh,
              )
            else
              ..._registrationContent(_hospital!, signedIn),
          ],
        ),
      ),
    );
  }

  List<Widget> _registrationContent(
    HospitalRegistration hospital,
    bool signedIn,
  ) => [
    Center(
      child: StatusBadge(
        _statusLabel(hospital),
        variant: _statusVariant(hospital),
      ),
    ),
    const SizedBox(height: 12),
    Text(
      hospital.name,
      textAlign: TextAlign.center,
      style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w800),
    ),
    const SizedBox(height: 4),
    Text(
      'Email: ${hospital.email}${hospital.licenseNumber.isEmpty ? '' : ' · License: ${hospital.licenseNumber}'}',
      textAlign: TextAlign.center,
      style: const TextStyle(fontSize: 12, color: AppColors.slate500),
    ),
    if (!signedIn) ...[
      const SizedBox(height: 16),
      const InfoBanner(
        'Your registration was submitted. Sign in with your hospital account to check its latest status, read the administrator\'s messages and reply.',
        color: AppColors.blue600,
        icon: Icons.verified_user_outlined,
      ),
    ],
    if (_loadError != null) ...[
      const SizedBox(height: 12),
      FormErrorBox(_loadError!),
    ],
    if (hospital.rejectionReason?.trim().isNotEmpty == true ||
        hospital.rejectionReport != null) ...[
      const SizedBox(height: 16),
      SectionCard(
        title: 'Rejection report',
        icon: Icons.report_outlined,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (hospital.rejectionReason?.trim().isNotEmpty == true)
              Text(hospital.rejectionReason!),
            if (hospital.rejectionReport != null) ...[
              const SizedBox(height: 10),
              AttachmentView(
                attachment: hospital.rejectionReport!,
                label: 'Administrator report',
              ),
            ],
          ],
        ),
      ),
    ],
    const SizedBox(height: 16),
    SectionCard(
      title: 'Registration Details',
      icon: Icons.local_hospital_outlined,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Wrap(
            spacing: 24,
            runSpacing: 12,
            children: [
              LabeledValue(
                'Hospital Contact Number',
                hospital.contactNumber.isEmpty
                    ? 'Not provided'
                    : hospital.contactNumber,
              ),
              LabeledValue(
                'Registration Code',
                hospital.registrationNumber?.isNotEmpty == true
                    ? hospital.registrationNumber!
                    : 'Not provided',
                mono: true,
              ),
              LabeledValue(
                'Authorized Person',
                hospital.contactPersonName?.isNotEmpty == true
                    ? hospital.contactPersonName!
                    : 'Not provided',
              ),
              LabeledValue(
                'Authorized Person Phone',
                hospital.contactPersonPhone?.isNotEmpty == true
                    ? hospital.contactPersonPhone!
                    : 'Not provided',
              ),
              LabeledValue(
                'Address',
                [
                  hospital.address,
                  if (hospital.city?.isNotEmpty == true) hospital.city!,
                ].where((value) => value.isNotEmpty).join(', '),
              ),
            ],
          ),
          if (hospital.licenseDocument != null ||
              hospital.accreditationDocument != null) ...[
            const SizedBox(height: 14),
            if (hospital.licenseDocument != null)
              AttachmentView(
                attachment: hospital.licenseDocument!,
                label: 'License',
              ),
            if (hospital.accreditationDocument != null) ...[
              const SizedBox(height: 8),
              AttachmentView(
                attachment: hospital.accreditationDocument!,
                label: 'Accreditation',
              ),
            ],
          ],
        ],
      ),
    ),
    const SizedBox(height: 16),
    SectionCard(
      title: 'Registration Conversation',
      icon: Icons.forum_outlined,
      child: ThreadView(
        messages: _thread(hospital),
        emptyText: 'No registration activity yet.',
      ),
    ),
    if (_sent) ...[
      const SizedBox(height: 12),
      const InfoBanner(
        'Your reply was sent to the administrator.',
        color: AppColors.emerald600,
        icon: Icons.check_circle_outline,
      ),
    ],
    if (hospital.isAwaitingAdminReview) ...[
      const SizedBox(height: 12),
      const InfoBanner(
        'Your reply is with the administrator. You can reply again after the administrator responds; you will be notified by email.',
        color: AppColors.blue600,
        icon: Icons.schedule,
      ),
    ],
    if (signedIn && hospital.isRejected) ...[
      const SizedBox(height: 16),
      _replyForm(hospital),
    ],
    const SizedBox(height: 16),
    if (hospital.isApproved && signedIn)
      BusyButton(
        label: 'Go to Hospital Dashboard',
        icon: Icons.arrow_forward,
        onPressed: _openDashboard,
      )
    else if (signedIn)
      BusyButton(
        label: _refreshing ? 'Checking Status...' : 'Check Approval Status',
        icon: Icons.refresh,
        busy: _refreshing,
        onPressed: _refresh,
      )
    else
      BusyButton(
        label: 'Sign In to Check Status',
        icon: Icons.login,
        onPressed: () => context.go(AppRoutes.login),
      ),
    if (signedIn) ...[
      const SizedBox(height: 8),
      OutlinedButton.icon(
        onPressed: () => ref.read(authControllerProvider.notifier).logout(),
        icon: const Icon(Icons.logout),
        label: const Text('Sign Out'),
      ),
    ],
  ];

  Widget _replyForm(HospitalRegistration hospital) => SectionCard(
    title: 'Reply to the Administrator',
    icon: Icons.send_outlined,
    child: Form(
      key: _replyFormKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text(
            'Your registration stays in this conversation until the administrator approves it. Explain your changes and, if needed, correct your details or upload new documents.',
            style: TextStyle(fontSize: 12, color: AppColors.slate500),
          ),
          const SizedBox(height: 12),
          if (_formError != null) ...[
            FormErrorBox(_formError!),
            const SizedBox(height: 12),
          ],
          TextFormField(
            key: const Key('registration-reply-message'),
            controller: _message,
            maxLines: 3,
            maxLength: 1000,
            decoration: const InputDecoration(
              hintText: 'Write your reply to the administrator...',
            ),
            validator: _replyValidator,
          ),
          const SizedBox(height: 12),
          AttachmentPickerField(
            value: _replyAttachment,
            label: 'Attach a file (optional, max 2 MB)',
            onChanged: (value) => setState(() => _replyAttachment = value),
          ),
          const SizedBox(height: 10),
          TextButton.icon(
            key: const Key('registration-toggle-corrections'),
            onPressed: () =>
                setState(() => _showCorrections = !_showCorrections),
            icon: Icon(
              _showCorrections ? Icons.expand_less : Icons.expand_more,
            ),
            label: Text(
              _showCorrections
                  ? 'Hide registration details'
                  : 'Also correct registration details or documents',
            ),
          ),
          if (_showCorrections) ...[
            const SizedBox(height: 8),
            _correctionFields(),
          ],
          const SizedBox(height: 16),
          BusyButton(
            key: const Key('registration-reply-submit'),
            label: 'Send Reply',
            icon: Icons.send,
            busy: _sending,
            onPressed: hospital.isRejected ? _sendReply : null,
          ),
        ],
      ),
    ),
  );

  Widget _correctionFields() => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      _correctionField('name', 'Hospital Name'),
      _correctionField('licenseNumber', 'License Number / PHSRC Code'),
      _correctionField('registrationNumber', 'Registration / Facility Code'),
      _correctionField(
        'contactNumber',
        'Hospital Contact Number *',
        phone: true,
        validator: (value) => _phoneCorrection(
          value,
          'Hospital contact number must be exactly 10 digits.',
        ),
      ),
      _correctionField('city', 'City / District / Region'),
      _correctionField(
        'contactPersonName',
        'Authorized Person Name *',
        validator: (value) =>
            _requiredCorrection(value, 'Authorized person name is required.'),
      ),
      _correctionField(
        'contactPersonPhone',
        'Authorized Person Phone Number *',
        phone: true,
        validator: (value) => _phoneCorrection(
          value,
          'Authorized person phone number must be exactly 10 digits.',
        ),
      ),
      _correctionField(
        'contactPersonEmail',
        'Authorized Person Email',
        email: true,
        validator: (value) {
          final email = value?.trim() ?? '';
          return email.isNotEmpty && !_emailPattern.hasMatch(email)
              ? 'Enter a valid email address.'
              : null;
        },
      ),
      _correctionField('address', 'Physical Hospital Address', maxLines: 2),
      const SizedBox(height: 8),
      AttachmentPickerField(
        value: _licenseDocument,
        label: 'New PHSRC / MOH License File',
        onChanged: (value) => setState(() => _licenseDocument = value),
      ),
      const SizedBox(height: 12),
      AttachmentPickerField(
        value: _accreditationDocument,
        label: 'New Accreditation Certificate File',
        onChanged: (value) => setState(() => _accreditationDocument = value),
      ),
    ],
  );

  Widget _correctionField(
    String name,
    String label, {
    bool phone = false,
    bool email = false,
    int maxLines = 1,
    String? Function(String?)? validator,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: TextFormField(
      key: Key('registration-correction-$name'),
      controller: _corrections[name],
      keyboardType: phone
          ? TextInputType.phone
          : email
          ? TextInputType.emailAddress
          : TextInputType.text,
      inputFormatters: phone
          ? [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(10),
            ]
          : null,
      maxLines: maxLines,
      decoration: InputDecoration(labelText: label),
      validator: validator,
    ),
  );
}

class _MissingRegistration extends StatelessWidget {
  const _MissingRegistration({
    required this.error,
    required this.signedIn,
    required this.onRetry,
  });

  final String? error;
  final bool signedIn;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => SectionCard(
    child: Column(
      children: [
        Text(
          error ??
              'Sign in with your hospital account to see your registration.',
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 12),
        if (signedIn)
          OutlinedButton.icon(
            onPressed: onRetry,
            icon: const Icon(Icons.refresh),
            label: const Text('Retry'),
          )
        else
          FilledButton.icon(
            onPressed: () => context.go(AppRoutes.login),
            icon: const Icon(Icons.login),
            label: const Text('Sign In'),
          ),
      ],
    ),
  );
}
