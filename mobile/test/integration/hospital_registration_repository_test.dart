import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:http_mock_adapter/http_mock_adapter.dart';
import 'package:lifelink_mobile/core/attachments/attachment.dart';
import 'package:lifelink_mobile/features/auth/hospital_registration_repository.dart';

import '../helpers.dart';

void main() {
  test('hospital registration create is anonymous and omits UI-only institution type', () async {
    final api = MockedApi();
    api.adapter.onPost(
      '/Hospitals',
      (server) => server.reply(201, hospitalRegistrationJson()),
      data: Matchers.any,
    );
    final repository = HospitalRegistrationRepository(api.client);
    final license = Attachment.fromBytes(
      'license.pdf',
      Uint8List.fromList([1, 2, 3]),
    );

    final result = await repository.create(
      HospitalRegistrationDraft(
        name: ' Central Hospital ',
        registrationNumber: ' PHSRC/PH/123 ',
        email: ' hospital@example.test ',
        password: 'Hospital#1',
        contactNumber: '0112345678',
        address: ' 1 Main Street ',
        city: '',
        contactPersonName: ' Nimal Perera ',
        contactPersonPhone: '0771234567',
        licenseDocument: license,
        accreditationDocument: null,
      ),
    );

    expect(result.name, 'Central Hospital');
    final request = api.sent.single;
    expect(request.headers.containsKey('Authorization'), isFalse);
    final body = Map<String, dynamic>.from(request.data as Map);
    expect(body['licenseNumber'], 'PHSRC/PH/123');
    expect(body['registrationNumber'], 'PHSRC/PH/123');
    expect(body['licenseDocumentName'], 'license.pdf');
    expect(body.containsKey('institutionType'), isFalse);
    expect(body.containsKey('contactPersonEmail'), isFalse);
    expect(body.containsKey('city'), isFalse);
  });

  test(
    '/Hospitals/me parses full details, documents and conversation',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter.onGet(
        '/Hospitals/me',
        (server) => server.reply(200, {
          ...hospitalRegistrationJson(
            status: 'Rejected',
            history: [
              {
                'id': 'entry-1',
                'type': 'Rejected',
                'fromAdmin': true,
                'timestamp': '2026-10-15T05:00:00Z',
                'message': 'Replace the licence.',
                'attachmentName': 'report.pdf',
                'attachmentUrl': 'data:application/pdf;base64,AQID',
              },
            ],
          ),
          'licenseDocumentName': 'license.pdf',
          'licenseDocumentUrl': 'data:application/pdf;base64,AQID',
          'accreditationDocumentName': 'certificate.png',
          'accreditationDocumentUrl': 'data:image/png;base64,AQID',
          'rejectionReason': 'Licence expired.',
          'rejectionReportName': 'report.pdf',
          'rejectionReportUrl': 'data:application/pdf;base64,AQID',
        }),
      );

      final result = await HospitalRegistrationRepository(api.client).mine();

      expect(result.isRejected, isTrue);
      expect(result.licenseDocument?.name, 'license.pdf');
      expect(result.accreditationDocument?.name, 'certificate.png');
      expect(result.rejectionReport?.name, 'report.pdf');
      expect(result.history.single.title, 'Registration rejected');
      expect(result.history.single.attachment?.name, 'report.pdf');
    },
  );

  test(
    'hospital reply sends corrections and all three attachment roles',
    () async {
      final api = MockedApi(token: 'jwt');
      api.adapter.onPost(
        '/Hospitals/h1/replies',
        (server) => server.reply(
          200,
          hospitalRegistrationJson(status: 'AwaitingAdminReview'),
        ),
        data: Matchers.any,
      );
      const general = Attachment(
        name: 'note.pdf',
        url: 'data:application/pdf;base64,AQID',
      );
      const license = Attachment(
        name: 'new-license.pdf',
        url: 'data:application/pdf;base64,AQID',
      );
      const accreditation = Attachment(
        name: 'new-certificate.png',
        url: 'data:image/png;base64,AQID',
      );

      final result = await HospitalRegistrationRepository(api.client).reply(
        'h1',
        const HospitalRegistrationReply(
          message: ' Corrected documents attached. ',
          attachment: general,
          corrections: {
            'name': 'Central Teaching Hospital',
            'contactPersonEmail': 'contact@example.test',
            'city': '',
          },
          licenseDocument: license,
          accreditationDocument: accreditation,
        ),
      );

      expect(result.isAwaitingAdminReview, isTrue);
      final body = Map<String, dynamic>.from(api.sent.single.data as Map);
      expect(body['message'], 'Corrected documents attached.');
      expect(body['name'], 'Central Teaching Hospital');
      expect(body['contactPersonEmail'], 'contact@example.test');
      expect(body.containsKey('city'), isFalse);
      expect(body['attachmentName'], 'note.pdf');
      expect(body['licenseDocumentName'], 'new-license.pdf');
      expect(body['accreditationDocumentName'], 'new-certificate.png');
    },
  );
}
