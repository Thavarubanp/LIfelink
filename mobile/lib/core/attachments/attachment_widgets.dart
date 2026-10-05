import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:image_picker/image_picker.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

import '../theme/app_theme.dart';
import '../widgets/common.dart';
import 'attachment.dart';

/// Picks a document (PDF, Word or image) with the system file picker. Returns null when cancelled.
/// Throws [AttachmentException] when the file is not allowed.
Future<Attachment?> pickDocument() async {
  final file = await FilePicker.pickFile(type: FileType.custom, allowedExtensions: AttachmentRules.allowedExtensions);
  if (file == null) return null;
  final length = await file.length() ?? 0;
  final problem = AttachmentRules.check(file.name, length);
  if (problem != null) throw AttachmentException(problem);
  return Attachment.fromBytes(file.name, await file.readAsBytes());
}

/// Takes a photo with the camera (compressed so it fits the 2 MB limit). Returns null when cancelled.
Future<Attachment?> takePhoto() async {
  try {
    final photo = await ImagePicker().pickImage(source: ImageSource.camera, maxWidth: 2000, maxHeight: 2000, imageQuality: 80);
    if (photo == null) return null;
    final name = photo.name.toLowerCase().endsWith('.png') ? photo.name : 'photo_${DateTime.now().millisecondsSinceEpoch}.jpg';
    return Attachment.fromBytes(name, await photo.readAsBytes());
  } on PlatformException catch (e) {
    if (e.code.contains('denied')) {
      throw const AttachmentException(
          'Camera permission was denied. Allow the camera for LifeLink in Settings → Apps → LifeLink → Permissions, or attach a file instead.');
    }
    throw AttachmentException(e.message ?? 'The camera could not be opened.');
  }
}

/// Chooses a photo from the device gallery and applies the same 2 MB attachment rules.
Future<Attachment?> pickGalleryImage() async {
  try {
    final photo = await ImagePicker().pickImage(source: ImageSource.gallery, maxWidth: 2000, maxHeight: 2000, imageQuality: 80);
    if (photo == null) return null;
    final lower = photo.name.toLowerCase();
    final name = lower.endsWith('.png') || lower.endsWith('.jpg') || lower.endsWith('.jpeg')
        ? photo.name
        : 'gallery_${DateTime.now().millisecondsSinceEpoch}.jpg';
    return Attachment.fromBytes(name, await photo.readAsBytes());
  } on PlatformException catch (e) {
    if (e.code.contains('denied')) {
      throw const AttachmentException('Photo access was denied. Allow photos for LifeLink in Settings, or attach a file instead.');
    }
    throw AttachmentException(e.message ?? 'The photo gallery could not be opened.');
  }
}

/// "Attach file" / "Take photo" with the chosen attachment shown below (removable). Errors are shown under it.
class AttachmentPickerField extends StatefulWidget {
  const AttachmentPickerField({super.key, required this.value, required this.onChanged, this.enabled = true, this.label = 'Attachment (optional)'});

  final Attachment? value;
  final ValueChanged<Attachment?> onChanged;
  final bool enabled;
  final String label;

  @override
  State<AttachmentPickerField> createState() => _AttachmentPickerFieldState();
}

class _AttachmentPickerFieldState extends State<AttachmentPickerField> {
  String? _error;
  bool _busy = false;

  Future<void> _pick(Future<Attachment?> Function() picker) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final a = await picker();
      if (a != null) widget.onChanged(a);
    } on AttachmentException catch (e) {
      setState(() => _error = e.message);
    } catch (_) {
      setState(() => _error = 'The file could not be read.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(widget.label, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          if (widget.value != null)
            Chip(
              avatar: Icon(widget.value!.isImage ? Icons.image_outlined : Icons.description_outlined, size: 18),
              label: Text('${widget.value!.name} · ${widget.value!.sizeLabel}', overflow: TextOverflow.ellipsis),
              onDeleted: widget.enabled ? () => widget.onChanged(null) : null,
            )
          else
            Wrap(
              spacing: 8,
              children: [
                OutlinedButton.icon(
                  style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40)),
                  onPressed: !widget.enabled || _busy ? null : () => _pick(pickDocument),
                  icon: const Icon(Icons.attach_file, size: 18),
                  label: const Text('Attach file'),
                ),
                OutlinedButton.icon(
                  style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40)),
                  onPressed: !widget.enabled || _busy ? null : () => _pick(takePhoto),
                  icon: const Icon(Icons.photo_camera_outlined, size: 18),
                  label: const Text('Take photo'),
                ),
                OutlinedButton.icon(
                  style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40)),
                  onPressed: !widget.enabled || _busy ? null : () => _pick(pickGalleryImage),
                  icon: const Icon(Icons.photo_library_outlined, size: 18),
                  label: const Text('Gallery'),
                ),
              ],
            ),
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(
              _error ?? 'PDF, Word or image, up to 2 MB.',
              style: TextStyle(fontSize: 11, color: _error != null ? AppColors.rose600 : AppColors.slate500),
            ),
          ),
        ],
      );
}

/// Shows an attachment: images inline (tap for full screen), other files open in the phone's viewer.
class AttachmentView extends StatelessWidget {
  const AttachmentView({super.key, required this.attachment, this.label});

  final Attachment attachment;
  final String? label;

  Future<void> _open(BuildContext context) async {
    final bytes = attachment.bytes;
    if (bytes == null) {
      showSnack(context, 'This attachment cannot be opened on the phone. Open it on the LifeLink web app.', type: SnackType.warning);
      return;
    }
    try {
      final dir = await getTemporaryDirectory();
      final safe = attachment.name.replaceAll(RegExp(r'[^\w.\- ]'), '_');
      final file = File('${dir.path}/$safe');
      await file.writeAsBytes(bytes, flush: true);
      final result = await OpenFilex.open(file.path, type: attachment.mimeType);
      if (result.type != ResultType.done && context.mounted) {
        showSnack(
          context,
          result.type == ResultType.noAppToOpen
              ? 'No app on this phone can open ${attachment.name}. Install a PDF or document viewer.'
              : result.message,
          type: SnackType.warning,
        );
      }
    } catch (_) {
      if (context.mounted) showSnack(context, 'The attachment could not be opened.', type: SnackType.error);
    }
  }

  @override
  Widget build(BuildContext context) {
    final bytes = attachment.isImage ? attachment.bytes : null;
    if (bytes != null) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (label != null) Text(label!, style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600)),
          const SizedBox(height: 4),
          GestureDetector(
            onTap: () => Navigator.of(context).push(MaterialPageRoute<void>(
              builder: (_) => Scaffold(
                appBar: AppBar(title: Text(attachment.name)),
                backgroundColor: Colors.black,
                body: Center(child: InteractiveViewer(child: Image.memory(bytes))),
              ),
            )),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(10),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxHeight: 180),
                child: Image.memory(bytes, fit: BoxFit.cover, errorBuilder: (_, _, _) => Text(attachment.name)),
              ),
            ),
          ),
        ],
      );
    }
    return OutlinedButton.icon(
      style: OutlinedButton.styleFrom(minimumSize: const Size(0, 40), alignment: Alignment.centerLeft),
      onPressed: () => _open(context),
      icon: const Icon(Icons.description_outlined, size: 18),
      label: Text(label == null ? attachment.name : '$label: ${attachment.name}', overflow: TextOverflow.ellipsis),
    );
  }
}
