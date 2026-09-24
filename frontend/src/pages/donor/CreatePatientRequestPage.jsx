import React, { useState, useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';
import { bloodRequestApi, hospitalApi } from '../../api';
import { useAuth } from '../../context/AuthContext';
import { useNotification } from '../../context/NotificationContext';
import { getUserRoles } from '../../utils/roleUtils';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { MyRequestsList } from './MyRequestsPage';
import { AlertCircle, Loader2, Building2, X, Lock } from 'lucide-react';

const INITIAL_FORM = {
  hospitalId: '',
  bloodGroup: 'O+',
  unitsRequired: 2,
  reason: '',
  priority: 'High'
};

/**
 * Shared Create Blood Request page for Users (Donor/Patient), Admins and Hospital Staff.
 * Users/Admins must select a hospital; Hospital Staff are locked to their own hospital.
 * The creator's requests are listed below the form.
 */
export const CreatePatientRequestPage = () => {
  const { user } = useAuth();
  const isHospitalStaff = getUserRoles(user).includes('HospitalStaff');

  const [searchParams] = useSearchParams();
  const [formData, setFormData] = useState(() => ({
    ...INITIAL_FORM,
    priority: ['Normal', 'High', 'Critical'].includes(searchParams.get('priority')) ? searchParams.get('priority') : INITIAL_FORM.priority,
    bloodGroup: searchParams.get('bloodGroup') || INITIAL_FORM.bloodGroup
  }));
  const [refreshKey, setRefreshKey] = useState(0);

  // Hospital Autocomplete State
  const [hospitals, setHospitals] = useState([]);
  const [loadingHospitals, setLoadingHospitals] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedHospital, setSelectedHospital] = useState(null);
  const [isDropdownOpen, setIsDropdownOpen] = useState(false);
  const dropdownRef = useRef(null);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const { addToast } = useNotification();

  // Fetch active/approved hospitals from database
  useEffect(() => {
    const fetchHospitals = async () => {
      setLoadingHospitals(true);
      try {
        const res = await hospitalApi.getHospitals(true);
        const list = Array.isArray(res) ? res : (res?.data || []);
        // Display verified, non-suspended hospitals only
        const activeList = list.filter((h) => h.isVerified === true && !h.isSuspended);
        setHospitals(activeList);

        // Hospital staff always request for their own hospital (matched by account email, as the backend does)
        if (isHospitalStaff) {
          const normEmail = user?.email?.trim().toLowerCase();
          const own = activeList.find((h) => h.email?.toLowerCase() === normEmail);
          if (own) {
            setSelectedHospital(own);
            setSearchQuery(own.name);
            setFormData((prev) => ({ ...prev, hospitalId: own.hospitalId }));
          } else {
            setError('Your hospital could not be loaded. Please refresh the page.');
          }
        }
      } catch (err) {
        console.error('Failed to load active hospitals:', err);
      } finally {
        setLoadingHospitals(false);
      }
    };
    fetchHospitals();
  }, [isHospitalStaff, user?.email]);

  // Close dropdown when clicking outside
  useEffect(() => {
    const handleClickOutside = (event) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target)) {
        setIsDropdownOpen(false);
      }
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const getFilteredHospitals = () => {
    const query = searchQuery.trim().toLowerCase();
    if (!query) return hospitals;
    return hospitals.filter(
      (h) =>
        h.name?.toLowerCase().includes(query) ||
        h.licenseNumber?.toLowerCase().includes(query) ||
        h.address?.toLowerCase().includes(query)
    );
  };

  const handleSelectHospital = (h) => {
    setSelectedHospital(h);
    setSearchQuery(h.name);
    setFormData((prev) => ({ ...prev, hospitalId: h.hospitalId }));
    setIsDropdownOpen(false);
    if (error) setError('');
  };

  const handleClearHospital = () => {
    setSelectedHospital(null);
    setSearchQuery('');
    setFormData((prev) => ({ ...prev, hospitalId: '' }));
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    setError('');

    // Mandatory Hospital Selection Validation Guard
    if (!formData.hospitalId || formData.hospitalId === '00000000-0000-0000-0000-000000000000') {
      setError('Please select a mandatory target hospital facility from the database before submitting your request.');
      return;
    }

    setLoading(true);

    try {
      await bloodRequestApi.createRequest(formData);
      addToast({
        title: 'Blood Request Created!',
        message: `Your request for ${selectedHospital?.name || 'the hospital'} has been submitted for verification.`,
        type: 'success'
      });
      // Stay on the page: reset the form (hospital staff keep their locked hospital) and reload My Requests
      setFormData((prev) => ({ ...INITIAL_FORM, hospitalId: isHospitalStaff ? prev.hospitalId : '' }));
      if (!isHospitalStaff) handleClearHospital();
      setRefreshKey((k) => k + 1);
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="space-y-8">
    <div className="max-w-2xl mx-auto space-y-6">
      <div>
        <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Create Blood Request</h1>
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Submit a blood requirement request for hospital verification and donor matching.
        </p>
      </div>

      <div className="bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-sm">
        {error && (
          <div className="mb-4 p-3.5 bg-red-950/60 border border-red-900/50 rounded-xl flex items-start gap-2.5 text-xs text-red-300 animate-in fade-in">
            <AlertCircle className="w-4 h-4 text-red-500 shrink-0 mt-0.5" />
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4 text-xs">
          {/* Hospital staff: own hospital, locked */}
          {isHospitalStaff ? (
            <div>
              <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1 uppercase tracking-wider text-[11px]">
                HOSPITAL <span className="text-slate-400 font-medium normal-case tracking-normal">(your hospital — cannot be changed)</span>
              </label>
              <div className="relative">
                <Building2 className="w-4 h-4 text-slate-400 absolute left-3.5 top-3 pointer-events-none" />
                <input
                  type="text"
                  readOnly
                  value={loadingHospitals ? 'Loading your hospital...' : selectedHospital?.name || ''}
                  className="w-full pl-10 pr-9 py-2.5 bg-slate-100 dark:bg-slate-800/60 border border-emerald-500/80 rounded-xl text-slate-900 dark:text-slate-100 cursor-not-allowed focus:outline-none"
                />
                <Lock className="w-4 h-4 text-slate-400 absolute right-3 top-3" />
              </div>
            </div>
          ) : (
          /* Mandatory Searchable Hospital Dropdown */
          <div className="relative" ref={dropdownRef}>
            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1 uppercase tracking-wider text-[11px]">
              SELECT HOSPITAL * <span className="text-red-500 font-bold">(Mandatory)</span>
            </label>
            <div className="relative">
              <Building2 className="w-4 h-4 text-slate-400 absolute left-3.5 top-3 pointer-events-none" />
              <input
                type="text"
                required
                value={searchQuery}
                onFocus={() => setIsDropdownOpen(true)}
                onChange={(e) => {
                  setSearchQuery(e.target.value);
                  setIsDropdownOpen(true);
                  if (selectedHospital && e.target.value !== selectedHospital.name) {
                    setSelectedHospital(null);
                    setFormData((prev) => ({ ...prev, hospitalId: '' }));
                  }
                }}
                placeholder="Search active hospital by name or city..."
                className={`w-full pl-10 pr-9 py-2.5 bg-slate-50 dark:bg-slate-800 border rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none transition-colors ${
                  selectedHospital ? 'border-emerald-500/80' : 'border-slate-200 dark:border-slate-700 focus:border-red-500'
                }`}
              />
              {selectedHospital ? (
                <button
                  type="button"
                  onClick={handleClearHospital}
                  className="absolute right-3 top-3 text-slate-400 hover:text-red-400 transition-colors"
                  title="Clear selected hospital"
                >
                  <X className="w-4 h-4" />
                </button>
              ) : null}
            </div>

            {/* Selected Hospital Highlight Callout */}
            {selectedHospital && (
              <div className="mt-2 p-2.5 px-3.5 bg-emerald-500/10 border border-emerald-500/30 rounded-xl flex items-center justify-between text-xs text-emerald-300">
                <div className="flex items-center gap-2">
                  <Building2 className="w-4 h-4 text-emerald-400 shrink-0" />
                  <div>
                    <p className="font-bold text-slate-100">{selectedHospital.name}</p>
                    <p className="text-[10px] text-slate-400">
                      License: {selectedHospital.licenseNumber || 'Active'} • {selectedHospital.address || 'Sri Lanka'}
                    </p>
                  </div>
                </div>
                <span className="text-[10px] font-bold px-2 py-0.5 rounded bg-emerald-500/20 text-emerald-300 uppercase tracking-wider shrink-0">
                  Active Facility
                </span>
              </div>
            )}

            {/* Dropdown Options List */}
            {isDropdownOpen && (
              <div className="absolute z-30 top-full left-0 right-0 mt-1 max-h-56 overflow-y-auto bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-700 rounded-xl shadow-2xl divide-y divide-slate-100 dark:divide-slate-800 animate-in fade-in">
                {loadingHospitals ? (
                  <div className="p-3.5 text-center text-slate-400 flex items-center justify-center gap-2 text-xs">
                    <Loader2 className="w-4 h-4 animate-spin text-red-500" />
                    <span>Loading active hospitals from database...</span>
                  </div>
                ) : getFilteredHospitals().length > 0 ? (
                  getFilteredHospitals().map((h) => (
                    <button
                      key={h.hospitalId}
                      type="button"
                      onClick={() => handleSelectHospital(h)}
                      className="w-full p-2.5 px-3.5 text-left hover:bg-slate-100 dark:hover:bg-slate-800/80 flex items-center justify-between transition-colors group"
                    >
                      <div className="flex items-center gap-2.5 min-w-0">
                        <div className="w-7 h-7 rounded-lg flex items-center justify-center shrink-0 bg-cyan-500/10 text-cyan-400 border border-cyan-500/20">
                          <Building2 className="w-3.5 h-3.5" />
                        </div>
                        <div className="truncate">
                          <p className="font-semibold text-slate-900 dark:text-slate-100 group-hover:text-red-500 transition-colors truncate">
                            {h.name}
                          </p>
                          <p className="text-[10px] text-slate-500 dark:text-slate-400 truncate">
                            {h.licenseNumber ? `PHSRC: ${h.licenseNumber} • ` : ''}{h.address || 'Sri Lanka'}
                          </p>
                        </div>
                      </div>
                      <span className="text-[10px] font-bold px-2 py-0.5 rounded uppercase tracking-wider shrink-0 bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                        Active
                      </span>
                    </button>
                  ))
                ) : (
                  <div className="p-4 text-center text-slate-400 text-xs font-medium">
                    No active hospitals available
                  </div>
                )}
              </div>
            )}
          </div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Blood Group Required</label>
              <select
                value={formData.bloodGroup}
                onChange={(e) => setFormData({ ...formData, bloodGroup: e.target.value })}
                className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500"
              >
                {['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'].map((bg) => (
                  <option key={bg} value={bg}>{bg}</option>
                ))}
              </select>
            </div>

            <div>
              <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Units Required</label>
              <input
                type="number"
                min="1"
                required
                value={formData.unitsRequired}
                onChange={(e) => setFormData({ ...formData, unitsRequired: parseInt(e.target.value) || 1 })}
                className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500"
              />
            </div>
          </div>

          <div>
            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Urgency Priority</label>
            <select
              value={formData.priority}
              onChange={(e) => setFormData({ ...formData, priority: e.target.value })}
              className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500"
            >
              <option value="Normal">Normal (Scheduled Procedure)</option>
              <option value="High">Urgent (24-Hour Transfusion)</option>
              <option value="Critical">Critical Emergency (Immediately Needed)</option>
            </select>
          </div>

          <div>
            <label className="block text-slate-700 dark:text-slate-300 font-semibold mb-1">Clinical Reason / Diagnosis</label>
            <textarea
              required
              rows={4}
              value={formData.reason}
              onChange={(e) => setFormData({ ...formData, reason: e.target.value })}
              placeholder="Detail medical condition, attending physician notes, or surgery schedule..."
              className="w-full px-3.5 py-2.5 bg-slate-50 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 rounded-xl text-slate-900 dark:text-slate-100 focus:outline-none focus:border-red-500 resize-none"
            />
          </div>

          <button
            type="submit"
            disabled={loading || !formData.hospitalId}
            className="w-full py-3 bg-red-600 hover:bg-red-700 text-white font-semibold rounded-xl text-xs shadow-md shadow-red-600/20 transition-all flex items-center justify-center gap-2 mt-2 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {loading ? <Loader2 className="w-4 h-4 animate-spin" /> : 'Submit Blood Request'}
          </button>
        </form>
      </div>
    </div>

      <MyRequestsList refreshKey={refreshKey} />
    </div>
  );
};

export default CreatePatientRequestPage;
