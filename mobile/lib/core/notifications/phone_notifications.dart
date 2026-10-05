import 'dart:async';
import 'dart:ui';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_local_notifications/flutter_local_notifications.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:workmanager/workmanager.dart';

import '../api/json.dart';
import '../config/env.dart';
import 'notification_rules.dart';

/// Device feature: local phone notifications for new LifeLink notifications (admin messages, alerts, decisions) and,
/// for the admin, new items that need attention. No push service: the app polls the existing API
/// - while it is open, every 30 s (with the unread count), and
/// - when it is closed, with an Android WorkManager periodic task (about every 15 minutes, the Android minimum).
/// Every poll is a BACKGROUND request (no X-LifeLink-Activity header), so it never extends the session.
class PhoneNotifications {
  PhoneNotifications._();

  static final instance = PhoneNotifications._();

  static const taskName = 'lifelink-notification-poll';
  static const _uniqueName = 'lifelink-notification-poll';
  static const _channelId = 'lifelink_updates';
  static const _channelName = 'LifeLink updates';
  static const _markerIdKey = 'notif_last_seen_id';
  static const _markerAtKey = 'notif_last_seen_at';
  static const _attentionKey = 'notif_attention';
  static const _isAdminKey = 'notif_is_admin';
  static const askedKey = 'notif_permission_asked';
  static const _tokenKey = 'lifelink_token';

  final _plugin = FlutterLocalNotificationsPlugin();
  bool _ready = false;

  /// Called with the screen a tapped notification should open.
  void Function(String route)? onOpen;

  /// The route of the notification that launched the app (opened once the router is ready).
  String? launchRoute;

  Future<void> init() async {
    if (_ready || kIsWeb || defaultTargetPlatform != TargetPlatform.android) return;
    await _plugin.initialize(
      settings: const InitializationSettings(android: AndroidInitializationSettings('@mipmap/ic_launcher')),
      onDidReceiveNotificationResponse: (r) {
        final route = r.payload;
        if (route != null && route.isNotEmpty) onOpen?.call(route);
      },
    );
    final launch = await _plugin.getNotificationAppLaunchDetails();
    if (launch?.didNotificationLaunchApp == true) launchRoute = launch?.notificationResponse?.payload;
    _ready = true;
  }

  AndroidFlutterLocalNotificationsPlugin? get _android =>
      _plugin.resolvePlatformSpecificImplementation<AndroidFlutterLocalNotificationsPlugin>();

  Future<bool> areEnabled() async => await _android?.areNotificationsEnabled() ?? false;

  /// Android 13+ asks the user; older versions allow notifications by default. Returns whether they are allowed.
  Future<bool> requestPermission() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(askedKey, true);
    return await _android?.requestNotificationsPermission() ?? false;
  }

  /// Starts the background poll for the signed-in account (kept if it already runs).
  Future<void> start({required bool isAdmin}) async {
    if (kIsWeb || defaultTargetPlatform != TargetPlatform.android) return;
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_isAdminKey, isAdmin);
    await Workmanager().initialize(notificationTaskDispatcher);
    await Workmanager().registerPeriodicTask(
      _uniqueName,
      taskName,
      frequency: const Duration(minutes: 15),
      constraints: Constraints(networkType: NetworkType.connected),
      existingWorkPolicy: ExistingPeriodicWorkPolicy.keep,
    );
  }

  /// Signed out: stop the background poll and forget what was seen (the next account starts fresh).
  Future<void> stop() async {
    if (kIsWeb || defaultTargetPlatform != TargetPlatform.android) return;
    try {
      await Workmanager().cancelByUniqueName(_uniqueName);
    } catch (_) {
      // Not registered
    }
    final prefs = await SharedPreferences.getInstance();
    for (final k in [_markerIdKey, _markerAtKey, _attentionKey, _isAdminKey]) {
      await prefs.remove(k);
    }
  }

  /// One poll: reads the token, fetches the caller's notifications (and the admin's attention counts) as background
  /// requests, shows what is new, and saves the marker. Returns false when the session has ended (401).
  Future<bool> poll({String? token, Dio? dio}) async {
    final storedToken = token ?? await const FlutterSecureStorage().read(key: _tokenKey);
    if (storedToken == null || storedToken.isEmpty) return false;
    final client = dio ??
        Dio(BaseOptions(
          baseUrl: Env.apiBaseUrl,
          connectTimeout: const Duration(seconds: 15),
          receiveTimeout: const Duration(seconds: 30),
          // Background request: no X-LifeLink-Activity header, so the idle timeout is not extended
          headers: {'Authorization': 'Bearer $storedToken', 'Accept': 'application/json'},
        ));
    final prefs = await SharedPreferences.getInstance();
    try {
      final body = (await client.get<Object?>('/notifications/my')).data;
      final all = unwrapList(body)
          .map((j) => PolledNotification(
                id: str(j['notificationId']),
                title: str(j['title']),
                message: str(j['message']),
                type: str(j['notificationType']),
                createdAt: parseDate(j['createdAt']),
              ))
          .toList();
      final marker = PollMarker(
        lastSeenId: prefs.getString(_markerIdKey),
        lastSeenAt: DateTime.tryParse(prefs.getString(_markerAtKey) ?? ''),
      );
      final result = newNotifications(all, marker);
      for (final n in result.toShow) {
        await show(n.id.hashCode, n.title, n.message, routeForNotificationType(n.type));
      }
      if (result.marker.lastSeenId != null) await prefs.setString(_markerIdKey, result.marker.lastSeenId!);
      if (result.marker.lastSeenAt != null) await prefs.setString(_markerAtKey, result.marker.lastSeenAt!.toIso8601String());

      if (prefs.getBool(_isAdminKey) == true) {
        final a = unwrapMap((await client.get<Object?>('/Admin/attention-counts')).data);
        final now = AttentionSnapshot(
          registrations: intOf(a['pendingRegistrations']),
          appeals: intOf(a['pendingAppeals']),
          complaints: intOf(a['pendingComplaints']),
          newActivity: intOf(a['newBloodRequests']) + intOf(a['newTransfers']),
        );
        final saved = prefs.getStringList(_attentionKey);
        final before = saved != null && saved.length == 4 ? AttentionSnapshot.fromList(saved.map(int.parse).toList()) : null;
        final summary = attentionIncrease(before, now);
        if (summary != null) await show(9001, 'Needs your attention', summary, routeForNotificationType('AdminAttention'));
        await prefs.setStringList(_attentionKey, now.toList().map((e) => '$e').toList());
      }
      return true;
    } on DioException catch (e) {
      // Session ended (idle timeout, sign-out elsewhere or expiry): stop quietly until the next sign-in
      if (e.response?.statusCode == 401) return false;
      return true;
    }
  }

  Future<void> show(int id, String title, String body, String route) async {
    if (!_ready) await init();
    if (!_ready) return;
    await _plugin.show(
      id: id,
      title: title,
      body: body,
      payload: route,
      notificationDetails: const NotificationDetails(
        android: AndroidNotificationDetails(
          _channelId,
          _channelName,
          channelDescription: 'Messages from the administrator, alerts and decisions',
          importance: Importance.high,
          priority: Priority.high,
          styleInformation: BigTextStyleInformation(''),
        ),
      ),
    );
  }

  Future<void> cancelBackgroundTask() async {
    try {
      await Workmanager().cancelByUniqueName(_uniqueName);
    } catch (_) {
      // Not registered
    }
  }
}

/// WorkManager entry point (runs in a background isolate, also when the app is closed).
@pragma('vm:entry-point')
void notificationTaskDispatcher() {
  Workmanager().executeTask((task, _) async {
    WidgetsFlutterBinding.ensureInitialized();
    DartPluginRegistrant.ensureInitialized();
    final notifier = PhoneNotifications.instance;
    await notifier.init();
    final stillSignedIn = await notifier.poll();
    if (!stillSignedIn) await notifier.cancelBackgroundTask();
    return true;
  });
}
