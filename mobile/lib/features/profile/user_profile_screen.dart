import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_error.dart';
import '../../core/auth/auth_controller.dart';
import '../../core/config/constants.dart';
import '../../core/utils/format.dart';
import '../../core/utils/validators.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/state_views.dart';
import '../auth/auth_widgets.dart';
import 'profile_repository.dart';

class UserProfileScreen extends ConsumerStatefulWidget {
  const UserProfileScreen({super.key, required this.userId});

  final String userId;

  @override
  ConsumerState<UserProfileScreen> createState() => _UserProfileScreenState();
}

class _UserProfileScreenState extends ConsumerState<UserProfileScreen> {
  bool _privacyBusy = false;

  Future<void> _refresh() async {
    ref.invalidate(userProfileProvider(widget.userId));
    await ref
        .read(userProfileProvider(widget.userId).future)
        .then((_) {}, onError: (_) {});
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(userProfileProvider(widget.userId));
    return Scaffold(
      appBar: AppBar(title: const Text('User profile')),
      body: RefreshableScroll(
        onRefresh: _refresh,
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(userProfileProvider(widget.userId)),
          loadingMessage: 'Loading profile...',
          data: (profile) => ContentWidth(
            maxWidth: 720,
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  SectionCard(
                    title: profile.fullName,
                    icon: Icons.person_outline,
                    trailing: StatusBadge(
                      profile.displayStatus,
                      variant: profile.displayStatus == 'Active'
                          ? BadgeVariant.success
                          : BadgeVariant.danger,
                    ),
                    child: Wrap(
                      spacing: 24,
                      runSpacing: 12,
                      children: [
                        LabeledValue(
                          'Role',
                          profile.roles.isEmpty
                              ? 'User'
                              : profile.roles.join(', '),
                        ),
                        LabeledValue(
                          'Gender',
                          profile.gender.isEmpty ? 'Not set' : profile.gender,
                        ),
                        if (profile.bloodGroup != null)
                          LabeledValue(
                            'Blood group',
                            '${profile.bloodGroup}${profile.bloodGroupConfirmed ? ' (confirmed)' : ''}',
                          ),
                        if (profile.lastDonationDate != null)
                          LabeledValue(
                            'Last donation',
                            Fmt.date(profile.lastDonationDate),
                          ),
                        if (profile.nextEligibleDonationDate != null)
                          LabeledValue(
                            'Next eligible',
                            Fmt.date(profile.nextEligibleDonationDate),
                          ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),
                  SectionCard(
                    title: profile.canEdit
                        ? 'Contact details and privacy'
                        : 'Contact details',
                    icon: Icons.contact_page_outlined,
                    child: Column(
                      children: [
                        _ContactRow(
                          label: 'Email',
                          value: profile.email,
                          isPublic: profile.isEmailPublic,
                          editable: profile.canEdit,
                          onChanged: (v) =>
                              _setPrivacy(context, ref, profile, email: v),
                        ),
                        const Divider(),
                        _ContactRow(
                          label: 'Phone',
                          value: profile.phoneNumber,
                          isPublic: profile.isPhonePublic,
                          editable: profile.canEdit,
                          onChanged: (v) =>
                              _setPrivacy(context, ref, profile, phone: v),
                        ),
                        const Divider(),
                        _ContactRow(
                          label: 'Address',
                          value: profile.address,
                          isPublic: profile.isAddressPublic,
                          editable: profile.canEdit,
                          onChanged: (v) =>
                              _setPrivacy(context, ref, profile, address: v),
                        ),
                      ],
                    ),
                  ),
                  if (profile.canEdit) ...[
                    const SizedBox(height: 12),
                    FilledButton.icon(
                      onPressed: () async {
                        final saved = await showModalBottomSheet<bool>(
                          context: context,
                          isScrollControlled: true,
                          useSafeArea: true,
                          builder: (_) =>
                              _EditUserProfileSheet(profile: profile),
                        );
                        if (saved == true) {
                          ref.invalidate(userProfileProvider(widget.userId));
                          try {
                            await ref
                                .read(authControllerProvider.notifier)
                                .refreshUser();
                          } catch (_) {
                            // The profile save succeeded; the next auth refresh will update the header.
                          }
                        }
                      },
                      icon: const Icon(Icons.edit_outlined),
                      label: const Text('Edit profile'),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _setPrivacy(
    BuildContext context,
    WidgetRef ref,
    UserProfile profile, {
    bool? email,
    bool? phone,
    bool? address,
  }) async {
    if (_privacyBusy) return;
    setState(() => _privacyBusy = true);
    try {
      await ref
          .read(profileRepositoryProvider)
          .updateUser(
            profile,
            isEmailPublic: email,
            isPhonePublic: phone,
            isAddressPublic: address,
          );
      ref.invalidate(userProfileProvider(widget.userId));
      if (context.mounted) {
        showSnack(
          context,
          'Privacy preference updated.',
          type: SnackType.success,
        );
      }
    } catch (e) {
      if (context.mounted) {
        showErrorSnack(context, e, title: 'Privacy not updated');
      }
    } finally {
      if (mounted) setState(() => _privacyBusy = false);
    }
  }
}

class _ContactRow extends StatelessWidget {
  const _ContactRow({
    required this.label,
    required this.value,
    required this.isPublic,
    required this.editable,
    required this.onChanged,
  });

  final String label;
  final String? value;
  final bool isPublic, editable;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) => ListTile(
    contentPadding: EdgeInsets.zero,
    leading: Icon(
      value == null ? Icons.lock_outline : Icons.contact_mail_outlined,
    ),
    title: Text(label),
    subtitle: Text(value?.isNotEmpty == true ? value! : 'Private'),
    trailing: editable
        ? Semantics(
            label: '$label visibility: ${isPublic ? 'Public' : 'Private'}',
            child: FilterChip(
              avatar: Icon(
                isPublic
                    ? Icons.visibility_outlined
                    : Icons.visibility_off_outlined,
                size: 18,
              ),
              label: Text(isPublic ? 'Public' : 'Private'),
              selected: isPublic,
              onSelected: onChanged,
            ),
          )
        : null,
  );
}

class _EditUserProfileSheet extends ConsumerStatefulWidget {
  const _EditUserProfileSheet({required this.profile});
  final UserProfile profile;

  @override
  ConsumerState<_EditUserProfileSheet> createState() =>
      _EditUserProfileSheetState();
}

class _EditUserProfileSheetState extends ConsumerState<_EditUserProfileSheet> {
  late final _first = TextEditingController(text: widget.profile.firstName);
  late final _last = TextEditingController(text: widget.profile.lastName);
  late final _phone = TextEditingController(
    text: widget.profile.phoneNumber ?? '',
  );
  late final _address = TextEditingController(
    text: widget.profile.address ?? '',
  );
  late String _gender = widget.profile.gender;
  late String? _bloodGroup = widget.profile.bloodGroup;
  late DateTime? _lastDonation = widget.profile.lastDonationDate;
  final _form = GlobalKey<FormState>();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _first.dispose();
    _last.dispose();
    _phone.dispose();
    _address.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref
          .read(profileRepositoryProvider)
          .updateUser(
            widget.profile,
            firstName: _first.text.trim(),
            lastName: _last.text.trim(),
            phoneNumber: _phone.text.trim(),
            gender: _gender,
            address: _address.text.trim(),
            bloodGroup: _bloodGroup,
            lastDonationDate: _lastDonation,
          );
      if (!mounted) return;
      showSnack(context, 'Your profile was saved.', type: SnackType.success);
      Navigator.pop(context, true);
    } catch (e) {
      if (mounted) setState(() => _error = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(
      20,
      8,
      20,
      MediaQuery.viewInsetsOf(context).bottom + 20,
    ),
    child: SingleChildScrollView(
      child: Form(
        key: _form,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Edit profile', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 16),
            TextFormField(
              controller: _first,
              textCapitalization: TextCapitalization.words,
              decoration: const InputDecoration(labelText: 'First name'),
              validator: (v) => Validators.name(v, 'First name'),
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _last,
              textCapitalization: TextCapitalization.words,
              decoration: const InputDecoration(labelText: 'Last name'),
              validator: (v) => Validators.name(v, 'Last name'),
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _phone,
              keyboardType: TextInputType.phone,
              inputFormatters: [
                FilteringTextInputFormatter.digitsOnly,
                LengthLimitingTextInputFormatter(10),
              ],
              decoration: const InputDecoration(labelText: 'Phone number'),
              validator: Validators.phone,
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              initialValue: {'Male', 'Female', 'Other'}.contains(_gender)
                  ? _gender
                  : 'Other',
              decoration: const InputDecoration(labelText: 'Gender'),
              items: [
                for (final g in ['Male', 'Female', 'Other'])
                  DropdownMenuItem(value: g, child: Text(g)),
              ],
              onChanged: (v) => setState(() => _gender = v ?? _gender),
            ),
            const SizedBox(height: 12),
            TextFormField(
              controller: _address,
              maxLines: 3,
              maxLength: 500,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Address'),
              validator: (v) => Validators.maxLength(v, 500, 'Address'),
            ),
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              initialValue: AppConstants.bloodGroups.contains(_bloodGroup)
                  ? _bloodGroup
                  : null,
              decoration: InputDecoration(
                labelText: 'Blood group',
                helperText: widget.profile.bloodGroupConfirmed
                    ? 'Confirmed by a recorded donation'
                    : null,
              ),
              items: [
                for (final g in AppConstants.bloodGroups)
                  DropdownMenuItem(value: g, child: Text(g)),
              ],
              onChanged: widget.profile.bloodGroupConfirmed
                  ? null
                  : (v) => setState(() => _bloodGroup = v),
            ),
            const SizedBox(height: 12),
            ListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Last donation date'),
              subtitle: Text(
                _lastDonation == null ? 'Not set' : Fmt.date(_lastDonation),
              ),
              trailing: const Icon(Icons.calendar_today_outlined),
              onTap: () async {
                final picked = await showDatePicker(
                  context: context,
                  initialDate: _lastDonation ?? Fmt.sriLankaToday(),
                  firstDate: DateTime(1950),
                  lastDate: Fmt.sriLankaToday(),
                );
                if (picked != null) setState(() => _lastDonation = picked);
              },
            ),
            if (_error != null) ...[
              const SizedBox(height: 8),
              FormErrorBox(_error!),
            ],
            const SizedBox(height: 16),
            BusyButton(
              label: 'Save profile',
              busy: _busy,
              onPressed: _save,
              icon: Icons.save_outlined,
            ),
          ],
        ),
      ),
    ),
  );
}
