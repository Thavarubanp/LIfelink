import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../core/api/api_error.dart';
import '../../core/routing/routes.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/common.dart';
import 'inventory_repository.dart';
import 'packet_rules.dart';

/// Device feature: scan a packet's QR code (or barcode) with the camera, or type its tracking number, to open the
/// packet's details and history. The tracking number is matched against this hospital's packets (all statuses).
class ScanPacketScreen extends ConsumerStatefulWidget {
  const ScanPacketScreen({super.key});

  @override
  ConsumerState<ScanPacketScreen> createState() => _ScanPacketScreenState();
}

class _ScanPacketScreenState extends ConsumerState<ScanPacketScreen> {
  final MobileScannerController _camera = MobileScannerController(
    detectionSpeed: DetectionSpeed.noDuplicates,
    formats: const [BarcodeFormat.qrCode, BarcodeFormat.code128, BarcodeFormat.code39],
  );
  final _manual = TextEditingController();
  bool _busy = false;
  String? _message;

  @override
  void dispose() {
    _camera.dispose();
    _manual.dispose();
    super.dispose();
  }

  void _onDetect(BarcodeCapture capture) {
    if (_busy) return;
    final raw = capture.barcodes.map((b) => b.rawValue).firstWhere((v) => v != null && v.isNotEmpty, orElse: () => null);
    if (raw != null) _open(raw, fromCamera: true);
  }

  Future<void> _open(String raw, {bool fromCamera = false}) async {
    final tracking = normalizeTrackingNumber(raw);
    if (tracking == null) {
      setState(() => _message = '"${raw.length > 40 ? '${raw.substring(0, 40)}…' : raw}" is not a LifeLink packet code '
          '(expected a tracking number like PKT-00000012).');
      return;
    }
    setState(() {
      _busy = true;
      _message = null;
    });
    if (fromCamera) HapticFeedback.mediumImpact();
    try {
      // The API has no lookup by tracking number: match against this hospital's packets (all statuses)
      final packets = await ref.read(inventoryRepositoryProvider).packets();
      final match = packets.where((p) => p.trackingNumber.toUpperCase() == tracking).firstOrNull;
      if (!mounted) return;
      if (match == null) {
        setState(() => _message = '$tracking is not in your hospital\'s inventory. Packets sent to another hospital belong to it now.');
        return;
      }
      await _camera.stop();
      if (!mounted) return;
      await context.push(AppRoutes.hospitalPacket(match.packetId));
      if (mounted) await _camera.start();
    } catch (e) {
      if (mounted) setState(() => _message = ApiError.from(e).message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          title: const Text('Scan packet'),
          actions: [
            IconButton(tooltip: 'Torch', icon: const Icon(Icons.flashlight_on_outlined), onPressed: () => _camera.toggleTorch()),
          ],
        ),
        body: SafeArea(
          child: Column(
            children: [
              Expanded(
                child: Stack(
                  fit: StackFit.expand,
                  children: [
                    MobileScanner(
                      controller: _camera,
                      onDetect: _onDetect,
                      errorBuilder: (context, error) => _CameraError(error: error, onRetry: () => _camera.start()),
                    ),
                    IgnorePointer(
                      child: Center(
                        child: Container(
                          width: 240,
                          height: 240,
                          decoration: BoxDecoration(
                            border: Border.all(color: Colors.white, width: 3),
                            borderRadius: BorderRadius.circular(20),
                          ),
                        ),
                      ),
                    ),
                    if (_busy) const ColoredBox(color: Colors.black38, child: Center(child: CircularProgressIndicator())),
                  ],
                ),
              ),
              Padding(
                padding: const EdgeInsets.all(16),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Text('Point the camera at a packet\'s QR code, or type its tracking number.',
                        style: TextStyle(fontSize: 13)),
                    const SizedBox(height: 10),
                    if (_message != null) ...[
                      InfoBanner(_message!, color: AppColors.rose600, icon: Icons.qr_code_2),
                      const SizedBox(height: 10),
                    ],
                    Row(
                      children: [
                        Expanded(
                          child: TextField(
                            key: const Key('scan-manual'),
                            controller: _manual,
                            textCapitalization: TextCapitalization.characters,
                            decoration: const InputDecoration(hintText: 'PKT-00000012', isDense: true),
                            onSubmitted: (v) => _open(v),
                          ),
                        ),
                        const SizedBox(width: 8),
                        FilledButton(onPressed: _busy ? null : () => _open(_manual.text), child: const Text('Open')),
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      );
}

class _CameraError extends StatelessWidget {
  const _CameraError({required this.error, required this.onRetry});

  final MobileScannerException error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final denied = error.errorCode == MobileScannerErrorCode.permissionDenied;
    final unsupported = error.errorCode == MobileScannerErrorCode.unsupported;
    return ColoredBox(
      color: AppColors.slate900,
      child: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(denied ? Icons.no_photography_outlined : Icons.videocam_off_outlined, color: Colors.white, size: 48),
              const SizedBox(height: 12),
              Text(
                denied
                    ? 'Camera permission was denied. Allow the camera for LifeLink in your phone\'s Settings → Apps → LifeLink → Permissions, '
                        'or type the tracking number below.'
                    : unsupported
                        ? 'This device has no usable camera. Type the tracking number below.'
                        : 'The camera could not start. Type the tracking number below or try again.',
                textAlign: TextAlign.center,
                style: const TextStyle(color: Colors.white),
              ),
              if (!unsupported) ...[
                const SizedBox(height: 12),
                OutlinedButton(
                  style: OutlinedButton.styleFrom(foregroundColor: Colors.white, side: const BorderSide(color: Colors.white54)),
                  onPressed: onRetry,
                  child: const Text('Try again'),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
