import React, { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  Building2,
  MapPin,
  Phone,
  Mail,
  ShieldCheck,
  Stethoscope,
  Calendar,
  Lock,
  ArrowLeft,
  Droplet,
  AlertCircle,
  Loader2,
  Pencil,
  MessageSquare
} from 'lucide-react';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import profileApi from '../../api/profileApi';
import hospitalApi from '../../api/hospitalApi';
import EditProfileModal from '../../components/common/EditProfileModal';
import AttachmentLink from '../../components/common/AttachmentLink';
import RegistrationThread from '../../components/hospital/RegistrationThread';
import { DocumentPreviewModal } from '../../components/common/DocumentPreviewModal';

// Email, license number and registration number are not editable
const HOSPITAL_EDIT_FIELDS = [
  { name: 'name', label: 'Hospital Name', fullWidth: true },
  { name: 'address', label: 'Address', fullWidth: true },
  { name: 'city', label: 'City' },
  { name: 'contactNumber', label: 'Hospital Contact Number', type: 'tel', placeholder: '10 digits', required: true, pattern: '\\d{10}', patternTitle: 'Exactly 10 digits' },
  { name: 'contactPersonName', label: 'Authorized Person Name', required: true },
  { name: 'contactPersonPhone', label: 'Authorized Person Phone Number', type: 'tel', placeholder: '10 digits', required: true, pattern: '\\d{10}', patternTitle: 'Exactly 10 digits' },
  { name: 'contactPersonEmail', label: 'Authorized Person Email', type: 'email', fullWidth: true },
  { name: 'packetShelfLifeDays', label: 'Blood packet shelf life (21-35 days)', type: 'number' },
  { name: 'expiryAlertDays', label: 'Expiry alert window (days)', type: 'number' }
];

export const HospitalProfilePage = () => {
  const { id } = useParams();
  const navigate = useNavigate();
  const { user: currentUser } = useAuth();
  const { addToast } = useNotification();
  const [profile, setProfile] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [editing, setEditing] = useState(false);
  const [registration, setRegistration] = useState(null); // documents + registration conversation (own hospital or admin)
  const [previewDoc, setPreviewDoc] = useState(null);

  const currentRoles = Array.isArray(currentUser?.roles) ? currentUser.roles : [currentUser?.roles];
  const isAdmin = currentRoles.includes('Admin');

  const handleSave = async (values) => {
    const updated = await profileApi.updateHospitalProfile(profile.hospitalId, {
      ...values,
      packetShelfLifeDays: values.packetShelfLifeDays === '' ? null : Number(values.packetShelfLifeDays),
      expiryAlertDays: values.expiryAlertDays === '' ? null : Number(values.expiryAlertDays)
    });
    setProfile(updated);
    setEditing(false);
    addToast({ title: 'Profile Updated', message: 'Your hospital profile changes were saved.', type: 'success' });
  };

  useEffect(() => {
    const fetchProfile = async () => {
      try {
        setLoading(true);
        setError('');
        const data = await profileApi.getHospitalProfile(id);
        setProfile(data);
      } catch (err) {
        setError(err.response?.data?.message || 'Failed to load hospital profile.');
      } finally {
        setLoading(false);
      }
    };

    if (id) {
      fetchProfile();
    }
  }, [id]);

  // The registration record (documents and conversation) is visible to the hospital's own staff and to admins
  const canEdit = !!profile?.canEdit;
  const canSeeRegistration = !!profile && (canEdit || isAdmin);
  useEffect(() => {
    if (!canSeeRegistration) return;
    const request = canEdit ? hospitalApi.getMyHospital() : hospitalApi.getHospitalById(profile.hospitalId);
    request.then(setRegistration).catch(() => setRegistration(null));
  }, [profile, canEdit, canSeeRegistration]);
  const shownRegistration = canSeeRegistration ? registration : null;

  if (loading) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] gap-3">
        <Loader2 className="w-8 h-8 text-cyan-600 animate-spin" />
        <p className="text-xs text-slate-500">Loading hospital profile...</p>
      </div>
    );
  }

  if (error || !profile) {
    return (
      <div className="max-w-2xl mx-auto p-6 bg-red-50 dark:bg-red-950/30 border border-red-200 dark:border-red-900/50 rounded-2xl text-center space-y-4">
        <AlertCircle className="w-10 h-10 text-red-600 mx-auto" />
        <h2 className="text-base font-bold text-red-700 dark:text-red-400">Unable to load hospital profile</h2>
        <p className="text-xs text-slate-600 dark:text-slate-300">{error || 'Hospital record not found.'}</p>
        <button
          onClick={() => navigate(-1)}
          className="inline-flex items-center gap-2 px-4 py-2 bg-slate-900 text-white rounded-xl text-xs font-semibold hover:bg-slate-800"
        >
          <ArrowLeft className="w-4 h-4" /> Go Back
        </button>
      </div>
    );
  }

  return (
    <div className="max-w-4xl mx-auto space-y-6">
      {/* Top Navigation */}
      <div>
        <button
          onClick={() => navigate(-1)}
          className="inline-flex items-center gap-1.5 text-xs font-semibold text-slate-500 hover:text-slate-800 dark:hover:text-slate-200 transition-colors mb-2"
        >
          <ArrowLeft className="w-4 h-4" /> Back
        </button>
        <div className="flex items-center justify-between flex-wrap gap-4">
          <div>
            <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2">
              <Building2 className="w-6 h-6 text-cyan-600" />
              {profile.name}
            </h1>
            <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
              Verified Healthcare Facility Profile & Operational Directory
            </p>
          </div>
          <div className="flex items-center gap-2">
            {profile.isVerified ? (
              <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-semibold bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300 border border-emerald-200 dark:border-emerald-800">
                <ShieldCheck className="w-3.5 h-3.5" /> Verified Facility
              </span>
            ) : (
              <span className="inline-flex items-center gap-1 px-3 py-1 rounded-full text-xs font-semibold bg-amber-100 text-amber-800 dark:bg-amber-950/60 dark:text-amber-300 border border-amber-200 dark:border-amber-800">
                Pending Verification
              </span>
            )}
            {profile.canEdit && !currentUser?.isSuspended && (
              <button
                onClick={() => setEditing(true)}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-900 text-white hover:bg-slate-800 dark:bg-slate-100 dark:text-slate-900 dark:hover:bg-white transition-colors"
              >
                <Pencil className="w-3.5 h-3.5" /> Edit Profile
              </button>
            )}
          </div>
        </div>
      </div>

      {/* Main Info Card */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm space-y-6">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-slate-400 text-[10px] uppercase font-semibold block">City & Region</span>
            <div className="font-bold text-slate-900 dark:text-slate-100 mt-1 flex items-center gap-1.5 text-xs">
              <MapPin className="w-4 h-4 text-cyan-600 shrink-0" />
              <span>{profile.city || 'Not Specified'}</span>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-slate-400 text-[10px] uppercase font-semibold block">Hospital Contact Number</span>
            <div className="font-bold text-slate-900 dark:text-slate-100 mt-1 flex items-center gap-1.5 text-xs">
              <Phone className="w-4 h-4 text-emerald-600 shrink-0" />
              <span>{profile.contactNumber || 'N/A'}</span>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-slate-400 text-[10px] uppercase font-semibold block">Official Email</span>
            <div className="font-bold text-slate-900 dark:text-slate-100 mt-1 flex items-center gap-1.5 text-xs truncate">
              <Mail className="w-4 h-4 text-blue-600 shrink-0" />
              <span className="truncate">{profile.email}</span>
            </div>
          </div>

          <div className="p-4 bg-slate-50 dark:bg-slate-800/60 rounded-xl border border-slate-100 dark:border-slate-800">
            <span className="text-slate-400 text-[10px] uppercase font-semibold block">Active Doctors</span>
            <div className="font-bold text-slate-900 dark:text-slate-100 mt-1 flex items-center gap-1.5 text-xs">
              <Stethoscope className="w-4 h-4 text-purple-600 shrink-0" />
              <span>{profile.doctorCount} Registered</span>
            </div>
          </div>
        </div>

        {/* Detailed Facility Information */}
        <div className="border-t border-slate-100 dark:border-slate-800 pt-6">
          <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100 mb-4">Facility Credentials</h3>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 text-xs">
            <div className="space-y-3">
              <div>
                <span className="text-slate-400 text-[11px] block">Full Physical Address</span>
                <span className="font-medium text-slate-800 dark:text-slate-200">{profile.address || 'Address not listed'}</span>
              </div>
              <div>
                <span className="text-slate-400 text-[11px] block">Operating License Number</span>
                <span className="font-mono font-medium text-slate-800 dark:text-slate-200">{profile.licenseNumber}</span>
              </div>
              {profile.registrationNumber && (
                <div>
                  <span className="text-slate-400 text-[11px] block">National Registration ID</span>
                  <span className="font-mono font-medium text-slate-800 dark:text-slate-200">{profile.registrationNumber}</span>
                </div>
              )}
            </div>

            <div className="space-y-3">
              <div>
                <span className="text-slate-400 text-[11px] block">Authorized Person</span>
                <span className="font-medium text-slate-800 dark:text-slate-200">
                  {profile.contactPersonName || 'Not provided'}
                </span>
              </div>
              {profile.contactPersonPhone && (
                <div>
                  <span className="text-slate-400 text-[11px] block">Authorized Person Phone</span>
                  <span className="font-medium text-slate-800 dark:text-slate-200">{profile.contactPersonPhone}</span>
                </div>
              )}
              <div>
                <span className="text-slate-400 text-[11px] block">Partner Since</span>
                <span className="font-medium text-slate-800 dark:text-slate-200 flex items-center gap-1.5 mt-0.5">
                  <Calendar className="w-3.5 h-3.5 text-slate-400" />
                  {profile.createdAt ? new Date(profile.createdAt).toLocaleDateString([], { month: 'long', year: 'numeric', day: 'numeric' }) : 'N/A'}
                </span>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Blood Inventory Section with Strict Role-Based Visibility */}
      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-2">
            <Droplet className="w-5 h-5 text-red-600" />
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Blood Bank Inventory</h3>
          </div>
          {profile.canViewInventory && (
            <span className="text-[10px] font-semibold text-emerald-600 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-950/40 px-2.5 py-0.5 rounded-full border border-emerald-200 dark:border-emerald-800/50">
              Live Verified Telemetry
            </span>
          )}
        </div>

        {profile.canViewInventory ? (
          <div>
            {profile.inventory && profile.inventory.length > 0 ? (
              <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                {profile.inventory.map((item) => {
                  const isCritical = item.unitsAvailable <= item.minimumThreshold;
                  return (
                    <div
                      key={item.inventoryId || item.bloodGroup}
                      className={`p-4 rounded-xl border text-center transition-all ${
                        isCritical
                          ? 'bg-red-50/60 dark:bg-red-950/30 border-red-300 dark:border-red-900/60'
                          : 'bg-slate-50 dark:bg-slate-800/60 border-slate-200 dark:border-slate-700/60'
                      }`}
                    >
                      <span className="text-sm font-black text-slate-900 dark:text-slate-100 block">
                        {item.bloodGroup}
                      </span>
                      <div className="text-2xl font-bold text-red-600 my-1">{item.unitsAvailable}</div>
                      <span className="text-[10px] text-slate-400 block">Units Available</span>
                      <div className="mt-2 text-[10px] text-slate-500 font-medium">
                        Min: {item.minimumThreshold} / Max: {item.maximumCapacity}
                      </div>
                    </div>
                  );
                })}
              </div>
            ) : (
              <p className="text-xs text-slate-500 text-center py-6">
                No active inventory batches currently logged for this facility.
              </p>
            )}
          </div>
        ) : (
          <div className="p-6 rounded-xl bg-slate-50 dark:bg-slate-800/40 border border-slate-200/80 dark:border-slate-800 text-center space-y-2">
            <div className="w-10 h-10 rounded-full bg-slate-100 dark:bg-slate-800 text-slate-400 flex items-center justify-center mx-auto">
              <Lock className="w-5 h-5 text-slate-500" />
            </div>
            <h4 className="text-xs font-bold text-slate-800 dark:text-slate-200">
              Live Blood Unit Inventory Restricted
            </h4>
            <p className="text-[11px] text-slate-500 dark:text-slate-400 max-w-md mx-auto leading-relaxed">
              In accordance with platform clinical protocols, detailed real-time blood stock tallies are visible to
              authorized hospital personnel, medical doctors, and system administrators.
            </p>
          </div>
        )}
      </div>

      {shownRegistration && (
        <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm space-y-4">
          <div className="flex items-center gap-2">
            <MessageSquare className="w-5 h-5 text-cyan-600" />
            <h3 className="text-sm font-bold text-slate-900 dark:text-slate-100">Registration & Approval History</h3>
          </div>
          <div className="flex flex-wrap gap-2">
            <AttachmentLink label="License" url={shownRegistration.licenseDocumentUrl} name={shownRegistration.licenseDocumentName} onPreview={setPreviewDoc} />
            <AttachmentLink label="Accreditation" url={shownRegistration.accreditationDocumentUrl} name={shownRegistration.accreditationDocumentName} onPreview={setPreviewDoc} />
          </div>
          <RegistrationThread entries={shownRegistration.approvalHistory} onPreview={setPreviewDoc} />
        </div>
      )}

      {editing && (
        <EditProfileModal
          title="Edit Hospital Profile"
          fields={HOSPITAL_EDIT_FIELDS}
          initialValues={profile}
          onSave={handleSave}
          onClose={() => setEditing(false)}
        />
      )}

      {previewDoc && (
        <DocumentPreviewModal
          isOpen={!!previewDoc}
          onClose={() => setPreviewDoc(null)}
          title={previewDoc.title || 'Document'}
          documentUrl={previewDoc.url}
          documentName={previewDoc.name}
        />
      )}
    </div>
  );
};

export default HospitalProfilePage;
