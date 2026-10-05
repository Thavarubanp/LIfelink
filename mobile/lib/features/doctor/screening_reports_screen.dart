import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/utils/format.dart';
import '../../core/widgets/badges.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/reply_sheet.dart';
import '../../core/widgets/state_views.dart';
import '../verification/request_donors_screen.dart' show RecordDonationSheet;
import '../verification/verification_repository.dart';
import 'screening_models.dart';

/// Donor screening queue (web ScreeningReportsPage): AI-prepared reports for the hospital's requests. Reports assigned
/// to the doctor come first; any doctor of the hospital can act as a fallback. The doctor makes every decision.
class ScreeningReportsScreen extends ConsumerStatefulWidget {
  const ScreeningReportsScreen({super.key});

  @override
  ConsumerState<ScreeningReportsScreen> createState() => _ScreeningReportsScreenState();
}

class _ScreeningReportsScreenState extends ConsumerState<ScreeningReportsScreen> {
  ReportTab _tab = ReportTab.review;
  String _search = '';

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(screeningReportsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Screening reports')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(screeningReportsProvider);
          await ref.read(screeningReportsProvider.future).then((_) {}, onError: (_) {});
        },
        child: ContentWidth(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: AsyncView(
              value: value,
              onRetry: () => ref.invalidate(screeningReportsProvider),
              loadingMessage: 'Loading screening reports...',
              data: (all) {
                final tabs = groupReports(all);
                final q = _search.trim().toLowerCase();
                final list = tabs[_tab]!
                    .where((g) => q.isEmpty || [g.latest.donorName, g.latest.requestBloodGroup, g.latest.bloodRequestId].any((s) => s.toLowerCase().contains(q)))
                    .toList();
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text(
                      'AI-prepared screening reports. The AI only flags answers and suggests checks; you decide every donor.',
                      style: TextStyle(fontSize: 12, color: AppColors.slate500),
                    ),
                    const SizedBox(height: 10),
                    SearchField(hint: 'Search donor or request', onChanged: (v) => setState(() => _search = v)),
                    const SizedBox(height: 10),
                    FilterChips<ReportTab>(
                      options: [
                        (ReportTab.review, 'Waiting for review (${tabs[ReportTab.review]!.length})'),
                        (ReportTab.awaiting, 'Approved – awaiting donation (${tabs[ReportTab.awaiting]!.length})'),
                        (ReportTab.history, 'History (${tabs[ReportTab.history]!.length})'),
                      ],
                      selected: _tab,
                      onSelected: (t) => setState(() => _tab = t),
                    ),
                    const SizedBox(height: 12),
                    if (list.isEmpty)
                      const EmptyView(icon: Icons.fact_check_outlined, message: 'Nothing here right now.')
                    else
                      for (final g in list)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 10),
                          child: _ReportRow(group: g, tab: _tab),
                        ),
                  ],
                );
              },
            ),
          ),
        ),
      ),
    );
  }
}

class _ReportRow extends StatelessWidget {
  const _ReportRow({required this.group, required this.tab});

  final ReportGroup group;
  final ReportTab tab;

  @override
  Widget build(BuildContext context) {
    final r = group.latest;
    return Card(
      child: InkWell(
        key: Key('report-${r.acceptanceId}'),
        onTap: () => context.push(AppRoutes.doctorReport(r.acceptanceId)),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(children: [
                Expanded(child: Text(r.donorName, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15))),
                const Icon(Icons.chevron_right),
              ]),
              const SizedBox(height: 6),
              Wrap(spacing: 6, runSpacing: 4, children: [
                if (r.donorBloodGroup != null) BloodGroupBadge(r.donorBloodGroup!),
                if (r.riskLevel != null) StatusBadge('Risk ${r.riskLevel}', variant: riskVariant(r.riskLevel)),
                if (r.isAssignedToMe) const StatusBadge('Assigned to you', variant: BadgeVariant.primary),
                if (r.donorAccountStatus != null && r.donorAccountStatus != 'Active')
                  StatusBadge('Donor ${r.donorAccountStatus}', variant: BadgeVariant.warning),
                if (group.versions.length > 1) StatusBadge('v${r.version}'),
                if (r.requestStatus == 'Deleted') const StatusBadge('Request deleted'),
              ]),
              const SizedBox(height: 6),
              Text(
                'Request #${r.bloodRequestId.length >= 8 ? r.bloodRequestId.substring(0, 8) : r.bloodRequestId} (${r.requestBloodGroup}, ${r.requestStatus}) · '
                '${r.fulfilledUnits}/${r.unitsRequired} donated, ${r.reservedUnits} reserved'
                '${r.recommendation != null ? ' · AI: ${r.recommendation}' : ''}',
                style: const TextStyle(fontSize: 12, color: AppColors.slate500),
              ),
              if (tab == ReportTab.history)
                Text(
                  '${r.status}${r.decidedByName != null ? ' by ${r.decidedByName}' : ''}${r.verifiedAt != null ? ' on ${Fmt.sriLankaDateTime(r.verifiedAt)}' : ''} · donor status ${r.acceptanceStatus}',
                  style: const TextStyle(fontSize: 12),
                ),
              if (tab == ReportTab.review && !r.hasFreeSlot)
                const Text('No free slot: every remaining unit is reserved. The donor stays on standby.',
                    style: TextStyle(fontSize: 12, color: AppColors.amber600)),
            ],
          ),
        ),
      ),
    );
  }
}

/// One donor's report versions: the agent's risk, recommendation, summary and flags, every answer (flagged sections
/// highlighted), and the doctor's decision. Superseded and decided versions are read-only.
class ScreeningReportDetailScreen extends ConsumerStatefulWidget {
  const ScreeningReportDetailScreen({super.key, required this.acceptanceId});

  final String acceptanceId;

  @override
  ConsumerState<ScreeningReportDetailScreen> createState() => _ScreeningReportDetailScreenState();
}

class _ScreeningReportDetailScreenState extends ConsumerState<ScreeningReportDetailScreen> {
  int? _selectedVersion;

  void _reload() {
    ref.invalidate(screeningReportsProvider);
    ref.invalidate(assignedRequestsProvider);
  }

  Future<void> _approve(ScreeningReport r) async {
    final sent = await showReplySheet(
      context,
      title: 'Approve donor · ${r.donorName}',
      subtitle: 'Approval reserves one donation slot. It counts as donated only when the donation is recorded.',
      label: 'Notes for the donor (optional, visible to the donor)',
      submitLabel: 'Approve donor',
      minLength: 0,
      maxLength: 500,
      allowAttachment: false,
      onSubmit: (text, _) => ref.read(screeningRepositoryProvider).approve(r.id, text),
    );
    _reload();
    if (sent && mounted) showSnack(context, 'A donation slot is reserved for this donor.', type: SnackType.success, title: 'Donor approved');
  }

  Future<void> _reject(ScreeningReport r) async {
    final sent = await showReplySheet(
      context,
      title: 'Reject donor · ${r.donorName}',
      subtitle: 'The donor will see your reason. The request stays open to other donors.',
      label: 'Reason (required, shown to the donor)',
      submitLabel: 'Reject donor',
      maxLength: 500,
      requiredMessage: 'A reason is required.',
      allowAttachment: false,
      destructive: true,
      onSubmit: (text, _) => ref.read(screeningRepositoryProvider).reject(r.id, text),
    );
    _reload();
    if (sent && mounted) showSnack(context, 'The donor will see your reason. The request stays open.', title: 'Donor not approved');
  }

  Future<void> _record(ScreeningReport r) async {
    final done = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => RecordDonationSheet(requestId: r.bloodRequestId, acceptanceId: r.acceptanceId, donorName: r.donorName, initialGroup: r.donorBloodGroup),
    );
    _reload();
    if (done == true && mounted) {
      showSnack(context, 'The donated unit now counts towards the request.', type: SnackType.success, title: 'Donation recorded');
    }
  }

  Future<void> _release(ScreeningReport r) async {
    final sent = await showReplySheet(
      context,
      title: 'Release reservation · ${r.donorName}',
      subtitle: 'The slot becomes available to other donors.',
      label: 'Reason (shown to the donor)',
      submitLabel: 'Release slot',
      maxLength: 500,
      requiredMessage: 'A reason is required.',
      allowAttachment: false,
      destructive: true,
      onSubmit: (text, _) => ref.read(verificationRepositoryProvider).release(r.acceptanceId, text),
    );
    _reload();
    if (sent && mounted) showSnack(context, 'The slot is available to other donors again.', title: 'Reservation released');
  }

  @override
  Widget build(BuildContext context) {
    final value = ref.watch(screeningReportsProvider);
    return Scaffold(
      appBar: AppBar(title: const Text('Screening report')),
      body: RefreshableScroll(
        onRefresh: () async {
          ref.invalidate(screeningReportsProvider);
          await ref.read(screeningReportsProvider.future).then((_) {}, onError: (_) {});
        },
        child: AsyncView(
          value: value,
          onRetry: () => ref.invalidate(screeningReportsProvider),
          data: (all) {
            final versions = all.where((r) => r.acceptanceId == widget.acceptanceId).toList()..sort((a, b) => a.version.compareTo(b.version));
            if (versions.isEmpty) return const EmptyView(icon: Icons.search_off, message: 'This screening report was not found.');
            final selected = versions.firstWhere((v) => v.version == _selectedVersion, orElse: () => versions.last);
            final latest = versions.last;
            return ContentWidth(maxWidth: 760, child: _body(versions, selected, latest));
          },
        ),
      ),
    );
  }

  Widget _body(List<ScreeningReport> versions, ScreeningReport selected, ScreeningReport latest) {
    final content = selected.content;
    final isLatest = selected.version == latest.version;
    final canDecide = isLatest && selected.canDecide;
    return Padding(
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(selected.donorName, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
          Text(
            'Request #${selected.bloodRequestId.length >= 8 ? selected.bloodRequestId.substring(0, 8) : selected.bloodRequestId} '
            '(${selected.requestBloodGroup})${selected.requestStatus == 'Deleted' ? ' – request deleted by its creator' : ''} · '
            'version ${selected.version}, submitted ${Fmt.sriLankaDateTime(selected.createdAt)}',
            style: const TextStyle(fontSize: 12, color: AppColors.slate500),
          ),
          if (versions.length > 1) ...[
            const SizedBox(height: 10),
            FilterChips<int>(
              options: [for (final v in versions) (v.version, 'v${v.version} · ${v.status}')],
              selected: selected.version,
              onSelected: (v) => setState(() => _selectedVersion = v),
            ),
          ],
          const SizedBox(height: 12),
          if (!isLatest || selected.status != 'Pending')
            InfoBanner(
              selected.status == 'Superseded'
                  ? 'Superseded: the donor updated their answers. This version is read-only.'
                  : selected.status == 'Pending'
                      ? 'An older version (read-only).'
                      : 'Decided: ${selected.status}${selected.decidedByName != null ? ' by ${selected.decidedByName}' : ''}'
                          '${selected.verifiedAt != null ? ' on ${Fmt.sriLankaDateTime(selected.verifiedAt)}' : ''}. Read-only.'
                          '${selected.notes?.isNotEmpty == true ? '\n${selected.notes}' : ''}',
              color: AppColors.blue600,
              icon: Icons.lock_outline,
            ),
          const SizedBox(height: 12),
          if (content == null)
            const InfoBanner('This is a legacy record without an AI screening report. Ask the donor to complete screening again if needed.')
          else ...[
            _AgentSummary(content: content),
            const SizedBox(height: 12),
            for (final s in content.sections) ...[_SectionCard(section: s, content: content), const SizedBox(height: 10)],
          ],
          const SizedBox(height: 8),
          if (canDecide)
            SectionCard(
              title: 'Your decision',
              icon: Icons.gavel_outlined,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (!selected.hasFreeSlot)
                    const Padding(
                      padding: EdgeInsets.only(bottom: 8),
                      child: Text('No free slot: every remaining unit is reserved. The donor stays on standby.',
                          style: TextStyle(fontSize: 12, color: AppColors.amber600)),
                    ),
                  Wrap(spacing: 8, runSpacing: 8, children: [
                    FilledButton.icon(
                      key: const Key('report-approve'),
                      style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600),
                      onPressed: selected.hasFreeSlot ? () => _approve(selected) : null,
                      icon: const Icon(Icons.check),
                      label: const Text('Approve'),
                    ),
                    FilledButton.icon(
                      key: const Key('report-reject'),
                      style: FilledButton.styleFrom(backgroundColor: AppColors.rose600),
                      onPressed: () => _reject(selected),
                      icon: const Icon(Icons.close),
                      label: const Text('Reject'),
                    ),
                  ]),
                ],
              ),
            ),
          if (isLatest && latest.acceptanceStatus == 'Verified')
            SectionCard(
              title: 'Approved – awaiting donation',
              icon: Icons.assignment_turned_in_outlined,
              child: Wrap(spacing: 8, runSpacing: 8, children: [
                FilledButton.icon(
                  key: const Key('report-record'),
                  style: FilledButton.styleFrom(backgroundColor: AppColors.emerald600),
                  onPressed: () => _record(selected),
                  icon: const Icon(Icons.assignment_turned_in_outlined),
                  label: const Text('Record donation'),
                ),
                OutlinedButton.icon(
                  key: const Key('report-release'),
                  onPressed: () => _release(selected),
                  icon: const Icon(Icons.undo),
                  label: const Text('Release slot'),
                ),
              ]),
            ),
        ],
      ),
    );
  }
}

class _AgentSummary extends StatelessWidget {
  const _AgentSummary({required this.content});

  final ScreeningReportContent content;

  @override
  Widget build(BuildContext context) => SectionCard(
        title: 'AI screening summary',
        icon: Icons.smart_toy_outlined,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Wrap(spacing: 6, runSpacing: 4, children: [
              StatusBadge('Risk ${content.riskLevel}', variant: riskVariant(content.riskLevel)),
              StatusBadge('AI recommendation: ${content.recommendation}', variant: BadgeVariant.info),
            ]),
            const SizedBox(height: 8),
            Text(content.summary),
            if (content.doctorNotes?.isNotEmpty == true) ...[
              const SizedBox(height: 6),
              Text('Suggested checks: ${content.doctorNotes}', style: const TextStyle(fontSize: 13, color: AppColors.slate500)),
            ],
            if (content.flags.isNotEmpty) ...[
              const SizedBox(height: 10),
              const Text('Flags for review', style: TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(height: 4),
              for (final f in content.flags)
                Padding(
                  key: Key('flag-${f.code}'),
                  padding: const EdgeInsets.only(bottom: 6),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      StatusBadge(f.label, variant: f.variant, icon: f.isDeferral ? Icons.block : Icons.visibility_outlined),
                      const SizedBox(height: 2),
                      Text('Q${f.section}: ${f.message}', style: const TextStyle(fontSize: 13)),
                    ],
                  ),
                ),
            ],
            const SizedBox(height: 6),
            Text(content.governance ?? 'The AI never decides: the doctor approves or rejects every donor.',
                style: const TextStyle(fontSize: 11, color: AppColors.slate500)),
          ],
        ),
      );
}

class _SectionCard extends StatelessWidget {
  const _SectionCard({required this.section, required this.content});

  final ScreeningSection section;
  final ScreeningReportContent content;

  @override
  Widget build(BuildContext context) {
    final highlighted = content.isHighlighted(section);
    final flags = content.flagsFor(section);
    final color = flags.any((f) => f.isDeferral) ? AppColors.rose600 : AppColors.amber600;
    return Container(
      key: Key('section-${section.index}'),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: highlighted ? color : Theme.of(context).colorScheme.outline, width: highlighted ? 2 : 1),
        color: highlighted ? color.withValues(alpha: 0.06) : null,
      ),
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(children: [
            if (section.confidential) ...[const Icon(Icons.lock_outline, size: 16), const SizedBox(width: 6)],
            Expanded(child: Text('Section ${section.index}: ${section.title}', style: const TextStyle(fontWeight: FontWeight.w700))),
            if (highlighted) StatusBadge(flags.any((f) => f.isDeferral) ? 'Flagged' : 'Review', variant: flags.first.variant),
          ]),
          const SizedBox(height: 8),
          if (section.items.isEmpty)
            const Text('Not applicable.', style: TextStyle(color: AppColors.slate500))
          else
            for (final item in section.items)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(item.question, style: const TextStyle(fontSize: 12, color: AppColors.slate500)),
                    Text(item.answer.isEmpty ? '-' : item.answer,
                        style: TextStyle(fontWeight: FontWeight.w600, color: highlighted ? color : null)),
                  ],
                ),
              ),
        ],
      ),
    );
  }
}
