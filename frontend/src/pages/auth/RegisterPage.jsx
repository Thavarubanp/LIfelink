import React, { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { authApi } from '../../api';
import { getApiErrorMessage, getApiFieldErrors } from '../../utils/errorUtils';
import { AlertCircle, CheckCircle2, Loader2 } from 'lucide-react';

export const RegisterPage = () => {
  const [formData, setFormData] = useState({
    firstName: '',
    lastName: '',
    email: '',
    password: '',
    phoneNumber: '',
    gender: 'Other',
    address: ''
  });

  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState({});
  const [success, setSuccess] = useState(false);
  const [loading, setLoading] = useState(false);

  const navigate = useNavigate();

  // Real-time Validation Computations
  const isFirstNameValid = formData.firstName.trim().length > 0;
  const isLastNameValid = formData.lastName.trim().length > 0;
  const isEmailValid = /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(formData.email.trim());

  // Password validation: min 8 chars, at least 1 uppercase letter, at least 1 special char
  const hasMinLen = formData.password.length >= 8;
  const hasUpper = /[A-Z]/.test(formData.password);
  const hasSpecial = /[^a-zA-Z0-9]/.test(formData.password);
  const isPasswordValid = hasMinLen && hasUpper && hasSpecial;

  // Phone number validation: exactly 10 digits
  const isPhoneValid = /^\d{10}$/.test(formData.phoneNumber);

  // Address validation
  const isAddressValid = formData.address.trim().length > 0;

  // Overall form validity state
  const isFormValid =
    isFirstNameValid &&
    isLastNameValid &&
    isEmailValid &&
    isPasswordValid &&
    isPhoneValid &&
    isAddressValid;

  const getFieldBorderClass = (fieldName, isValid) => {
    const value = formData[fieldName];
    const hasServerError = fieldErrors[fieldName];

    if (hasServerError) {
      return 'border-red-500 focus:border-red-500';
    }

    if (!value) {
      return 'border-slate-700 focus:border-red-500';
    }

    if (isValid) {
      return 'border-emerald-500 focus:border-emerald-500';
    }

    return 'border-red-500 focus:border-red-500';
  };

  const handleChange = (e) => {
    const { name, value } = e.target;

    if (name === 'phoneNumber') {
      // Allow numbers only, exactly max 10 digits
      const digitsOnly = value.replace(/\D/g, '').slice(0, 10);
      setFormData((prev) => ({ ...prev, phoneNumber: digitsOnly }));
    } else {
      setFormData((prev) => ({ ...prev, [name]: value }));
    }

    // Clear field-specific error when user modifies field
    if (fieldErrors[name]) {
      setFieldErrors((prev) => ({ ...prev, [name]: null }));
    }
    if (error) setError('');
  };

  const handleSubmit = async (e) => {
    e.preventDefault();
    if (!isFormValid) return;

    setError('');
    setFieldErrors({});
    setLoading(true);

    try {
      const res = await authApi.register(formData);
      if (res.isSuccess) {
        setSuccess(true);
        setTimeout(() => navigate('/login'), 2500);
      } else {
        setError(res.message || 'Registration failed.');
      }
    } catch (err) {
      const mainMsg = getApiErrorMessage(err);
      const fields = getApiFieldErrors(err);
      setError(mainMsg);
      setFieldErrors(fields);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col justify-center items-center px-4 py-8 relative overflow-hidden">
      <div className="max-w-lg w-full bg-slate-900 border border-slate-800 rounded-2xl p-8 shadow-2xl relative z-10">
        <div className="text-center mb-6">
          <h1 className="text-2xl font-bold text-white tracking-tight">
            Create Life<span className="text-red-500">Link</span> Account
          </h1>
          <p className="text-xs text-slate-400 mt-1">Register to donate blood or request emergency blood support</p>
        </div>

        {error && (
          <div className="mb-4 p-3.5 bg-red-950/80 border border-red-900 rounded-xl flex items-start gap-2.5 text-xs text-red-200 shadow-md animate-in fade-in">
            <AlertCircle className="w-4 h-4 text-red-400 shrink-0 mt-0.5" />
            <span className="leading-relaxed font-medium">{error}</span>
          </div>
        )}

        {success && (
          <div className="mb-4 p-3.5 bg-emerald-950/80 border border-emerald-900 rounded-xl flex items-start gap-2.5 text-xs text-emerald-200 shadow-md animate-in fade-in">
            <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0 mt-0.5" />
            <span>Registration successful! Redirecting to login page...</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-4 text-xs">
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-slate-300 font-semibold mb-1">First Name</label>
              <input
                type="text"
                name="firstName"
                required
                value={formData.firstName}
                onChange={handleChange}
                placeholder="John"
                className={`w-full px-3.5 py-2.5 bg-slate-800/80 border rounded-xl text-white focus:outline-none transition-colors ${getFieldBorderClass(
                  'firstName',
                  isFirstNameValid
                )}`}
              />
              {fieldErrors.firstName && (
                <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.firstName}</span>
              )}
            </div>
            <div>
              <label className="block text-slate-300 font-semibold mb-1">Last Name</label>
              <input
                type="text"
                name="lastName"
                required
                value={formData.lastName}
                onChange={handleChange}
                placeholder="Doe"
                className={`w-full px-3.5 py-2.5 bg-slate-800/80 border rounded-xl text-white focus:outline-none transition-colors ${getFieldBorderClass(
                  'lastName',
                  isLastNameValid
                )}`}
              />
              {fieldErrors.lastName && (
                <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.lastName}</span>
              )}
            </div>
          </div>

          <div>
            <label className="block text-slate-300 font-semibold mb-1">Email Address</label>
            <input
              type="email"
              name="email"
              required
              value={formData.email}
              onChange={handleChange}
              placeholder="john.doe@example.com"
              className={`w-full px-3.5 py-2.5 bg-slate-800/80 border rounded-xl text-white focus:outline-none transition-colors ${getFieldBorderClass(
                'email',
                isEmailValid
              )}`}
            />
            {formData.email && !isEmailValid && !fieldErrors.email && (
              <span className="text-[11px] text-red-400 mt-1 block font-medium">Enter a valid email address.</span>
            )}
            {fieldErrors.email && (
              <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.email}</span>
            )}
          </div>

          <div>
            <label className="block text-slate-300 font-semibold mb-1">Password (Min 8 Chars, Uppercase, Special)</label>
            <input
              type="password"
              name="password"
              required
              value={formData.password}
              onChange={handleChange}
              placeholder="••••••••"
              className={`w-full px-3.5 py-2.5 bg-slate-800/80 border rounded-xl text-white focus:outline-none transition-colors ${getFieldBorderClass(
                'password',
                isPasswordValid
              )}`}
            />
            {formData.password && !isPasswordValid && !fieldErrors.password && (
              <span className="text-[11px] text-red-400 mt-1 block font-medium">
                Password must contain min 8 characters, at least 1 uppercase letter & 1 special character.
              </span>
            )}
            {fieldErrors.password && (
              <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.password}</span>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-slate-300 font-semibold mb-1">Phone Number</label>
              <input
                type="tel"
                name="phoneNumber"
                value={formData.phoneNumber}
                onChange={handleChange}
                placeholder="0771234567"
                className={`w-full px-3.5 py-2.5 bg-slate-800/80 border rounded-xl text-white focus:outline-none transition-colors ${getFieldBorderClass(
                  'phoneNumber',
                  isPhoneValid
                )}`}
              />
              <p
                className={`text-[11px] mt-1 ${
                  formData.phoneNumber && !isPhoneValid
                    ? 'text-red-400 font-medium'
                    : formData.phoneNumber && isPhoneValid
                    ? 'text-emerald-400 font-medium'
                    : 'text-slate-400'
                }`}
              >
                Phone number must contain exactly 10 digits.
              </p>
              {fieldErrors.phoneNumber && (
                <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.phoneNumber}</span>
              )}
            </div>
            <div>
              <label className="block text-slate-300 font-semibold mb-1">Gender</label>
              <select
                name="gender"
                value={formData.gender}
                onChange={handleChange}
                className="w-full px-3 py-2.5 bg-slate-800/80 border border-slate-700 rounded-xl text-white focus:outline-none focus:border-red-500"
              >
                <option value="Male">Male</option>
                <option value="Female">Female</option>
                <option value="Other">Other</option>
              </select>
            </div>
          </div>

          <div>
            <label className="block text-slate-300 font-semibold mb-1">Address</label>
            <input
              type="text"
              name="address"
              value={formData.address}
              onChange={handleChange}
              placeholder="123 Health Ave, Suite 400"
              className={`w-full px-3.5 py-2.5 bg-slate-800/80 border rounded-xl text-white focus:outline-none transition-colors ${getFieldBorderClass(
                'address',
                isAddressValid
              )}`}
            />
            {fieldErrors.address && (
              <span className="text-[11px] text-red-400 mt-1 block font-medium">{fieldErrors.address}</span>
            )}
          </div>

          <button
            type="submit"
            disabled={loading || !isFormValid}
            className="w-full py-3 bg-red-600 hover:bg-red-700 text-white font-semibold rounded-xl shadow-lg shadow-red-600/20 transition-all flex items-center justify-center gap-2 mt-4 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {loading ? <Loader2 className="w-4 h-4 animate-spin" /> : 'Create Account'}
          </button>
        </form>

        <div className="mt-6 text-center text-xs text-slate-400">
          Already have an account?{' '}
          <Link to="/login" className="text-red-400 font-semibold hover:underline">
            Sign In
          </Link>
        </div>
      </div>
    </div>
  );
};

export default RegisterPage;

