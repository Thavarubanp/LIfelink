/**
 * LifeLink Backend Error Handling Utilities
 *
 * Faithfully extracts HTTP status codes and exact response messages/errors
 * returned by the ASP.NET Core backend and GlobalExceptionMiddleware.
 */

/**
 * Extracts a user-friendly main error message from an Axios error or standard Error.
 *
 * @param {any} error
 * @returns {string} User-facing error message
 */
export const getApiErrorMessage = (error) => {
  if (!error) return 'An unknown error occurred.';

  // 1. Error object without HTTP response (Custom thrown Error or Network failure)
  if (!error.response) {
    if (error.message && typeof error.message === 'string') {
      const msg = error.message.toLowerCase();
      if (msg.includes('network error') || msg.includes('econnrefused') || msg.includes('failed to fetch') || error.code === 'ERR_NETWORK') {
        return 'Unable to connect to server. Please ensure backend is running.';
      }
      if (error.code === 'ECONNABORTED') {
        return 'Server request timed out. Please try again.';
      }
      return error.message;
    }
    return 'Unable to connect to server. Please ensure backend is running.';
  }

  const { status, data } = error.response;

  // 2. Gateway Failure / Dev Server Proxy Error (502 / 504 or HTML response)
  if (status === 502 || status === 504 || (typeof data === 'string' && data.includes('<!DOCTYPE html>'))) {
    return 'Unable to connect to server. Please ensure backend is running.';
  }

  // 3. Status 400 Bad Request (ModelState / DTO Validation / Business Rule)
  if (status === 400) {
    if (typeof data === 'string' && data.trim()) return data;
    if (data?.message) return data.message;
    if (data?.title) return data.title;

    // Check if errors dictionary exists (ASP.NET Core ValidationProblemDetails)
    const errors = data?.errors || data?.Errors;
    if (errors && typeof errors === 'object') {
      const firstKey = Object.keys(errors)[0];
      if (firstKey) {
        const firstErr = Array.isArray(errors[firstKey]) ? errors[firstKey][0] : errors[firstKey];
        if (firstErr) return firstErr;
      }
    }
    return 'Validation failed. Please check your inputs.';
  }

  // 4. Status 401 Unauthorized (Invalid credentials / expired session)
  if (status === 401) {
    if (data?.message) return data.message;
    return 'Invalid email or password';
  }

  // 5. Status 403 Forbidden (Suspended account / insufficient permission)
  if (status === 403) {
    if (data?.message) return data.message;
    return 'Your account does not have permission';
  }

  // 6. Status 404 Not Found
  if (status === 404) {
    if (data?.message) return data.message;
    return 'User not found';
  }

  // 7. Status 409 Conflict (Duplicate email registration)
  if (status === 409) {
    if (data?.message) return data.message;
    return 'Email already registered';
  }

  // 8. Status 500 Internal Server Error
  if (status === 500) {
    if (data?.message) return data.message;
    return 'An unexpected server error occurred';
  }

  // Fallback for any other status code
  if (data?.message) return data.message;
  return `An unexpected server error occurred (HTTP ${status}).`;
};

/**
 * Extracts field-level validation errors from ASP.NET Core ModelState / DTO errors dictionary.
 *
 * @param {any} error
 * @returns {Record<string, string>} Map of field name -> error message string
 */
export const getApiFieldErrors = (error) => {
  if (!error?.response?.data) return {};

  const data = error.response.data;
  const fieldErrors = {};

  const rawErrors = data.errors || data.Errors;

  if (rawErrors && typeof rawErrors === 'object') {
    Object.keys(rawErrors).forEach((key) => {
      // Normalize C# Property Names (e.g. "FirstName" -> "firstName", "request.Email" -> "email")
      let cleanKey = key;
      if (cleanKey.includes('.')) {
        cleanKey = cleanKey.split('.').pop();
      }
      cleanKey = cleanKey.charAt(0).toLowerCase() + cleanKey.slice(1);

      const val = rawErrors[key];
      if (Array.isArray(val) && val.length > 0) {
        fieldErrors[cleanKey] = val[0]; // Take first validation message for field
      } else if (typeof val === 'string') {
        fieldErrors[cleanKey] = val;
      }
    });
  }

  return fieldErrors;
};
