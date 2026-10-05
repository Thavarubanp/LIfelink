import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../api/api_error.dart';
import '../theme/app_theme.dart';

/// Centered spinner with an optional message.
class LoadingView extends StatelessWidget {
  const LoadingView({super.key, this.message});

  final String? message;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const CircularProgressIndicator(color: AppColors.red600),
              if (message != null) ...[
                const SizedBox(height: 12),
                Text(message!, style: Theme.of(context).textTheme.bodySmall, textAlign: TextAlign.center),
              ],
            ],
          ),
        ),
      );
}

/// Empty list or no data, with an optional action.
class EmptyView extends StatelessWidget {
  const EmptyView({super.key, required this.message, this.icon = Icons.inbox_outlined, this.title, this.action});

  final String message;
  final String? title;
  final IconData icon;
  final Widget? action;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 48, color: AppColors.slate400),
              const SizedBox(height: 12),
              if (title != null)
                Padding(
                  padding: const EdgeInsets.only(bottom: 4),
                  child: Text(title!, style: Theme.of(context).textTheme.titleMedium, textAlign: TextAlign.center),
                ),
              Text(message, style: const TextStyle(color: AppColors.slate500), textAlign: TextAlign.center),
              if (action != null) ...[const SizedBox(height: 16), action!],
            ],
          ),
        ),
      );
}

/// An error with the API's message and a Retry button.
class ErrorView extends StatelessWidget {
  const ErrorView({super.key, required this.error, this.onRetry});

  final Object error;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final api = ApiError.from(error);
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(api.isNetwork ? Icons.wifi_off_rounded : Icons.error_outline_rounded, size: 48, color: AppColors.rose600),
            const SizedBox(height: 12),
            Text(api.message, textAlign: TextAlign.center),
            if (onRetry != null) ...[
              const SizedBox(height: 16),
              OutlinedButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh), label: const Text('Retry')),
            ],
          ],
        ),
      ),
    );
  }
}

/// Loading / error (with retry) / data for an [AsyncValue]; keeps showing old data while refreshing.
class AsyncView<T> extends StatelessWidget {
  const AsyncView({super.key, required this.value, required this.data, this.onRetry, this.loadingMessage});

  final AsyncValue<T> value;
  final Widget Function(T data) data;
  final VoidCallback? onRetry;
  final String? loadingMessage;

  @override
  Widget build(BuildContext context) => value.when(
        data: data,
        loading: () => LoadingView(message: loadingMessage),
        error: (e, _) => ErrorView(error: e, onRetry: onRetry),
      );
}

/// Pull-to-refresh that also works when the content is short or an empty/error view.
class RefreshableScroll extends StatelessWidget {
  const RefreshableScroll({super.key, required this.onRefresh, required this.child});

  final Future<void> Function() onRefresh;
  final Widget child;

  @override
  Widget build(BuildContext context) => RefreshIndicator(
        color: AppColors.red600,
        onRefresh: onRefresh,
        child: LayoutBuilder(
          builder: (context, constraints) => SingleChildScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            child: ConstrainedBox(constraints: BoxConstraints(minHeight: constraints.maxHeight), child: child),
          ),
        ),
      );
}
