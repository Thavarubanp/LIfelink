import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../notifications/unread_badge.dart';

const double _logoMarkSize = 32;

class LifeLinkAppBar extends StatelessWidget implements PreferredSizeWidget {
  const LifeLinkAppBar({super.key, required this.onBrandTap});

  final VoidCallback onBrandTap;

  @override
  Size get preferredSize => const Size.fromHeight(kToolbarHeight);

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final appBarTheme = theme.appBarTheme;

    return AppBar(
      automaticallyImplyLeading: false,
      backgroundColor: appBarTheme.backgroundColor,
      foregroundColor: appBarTheme.foregroundColor,
      titleSpacing: 16,
      title: Semantics(
        button: true,
        label: 'LifeLink, go to home',
        child: ExcludeSemantics(
          child: ConstrainedBox(
            constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
            child: InkWell(
              onTap: onBrandTap,
              borderRadius: BorderRadius.circular(12),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  ClipRRect(
                    borderRadius: BorderRadius.circular(8),
                    child: Image.asset(
                      'assets/brand/lifelink-logo.png',
                      width: _logoMarkSize,
                      height: _logoMarkSize,
                      fit: BoxFit.cover,
                    ),
                  ),
                  const SizedBox(width: 8),
                  RichText(
                    text: TextSpan(
                      style:
                          appBarTheme.titleTextStyle ??
                          theme.textTheme.titleLarge,
                      children: [
                        TextSpan(
                          text: 'Life',
                          style: TextStyle(color: theme.colorScheme.onSurface),
                        ),
                        TextSpan(
                          text: 'Link',
                          style: TextStyle(
                            color: theme.brightness == Brightness.dark
                                ? AppColors.red500
                                : AppColors.red600,
                          ),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
      actions: [
        IconButton(
          tooltip: 'Search LifeLink',
          onPressed: () => context.push(AppRoutes.search),
          icon: const Icon(Icons.search),
        ),
        IconButton(
          tooltip: 'Notifications',
          onPressed: () => context.push(AppRoutes.notifications),
          icon: const UnreadBadge(child: Icon(Icons.notifications_outlined)),
        ),
      ],
    );
  }
}
