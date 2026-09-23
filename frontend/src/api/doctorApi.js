import client from './client';

export const doctorApi = {
  /**
   * Create a new Doctor account (Hospital Staff only).
   * @param {Object} data - { hospitalId, firstName, lastName, email, password, licenseNumber, phoneNumber, specialization }
   */
  createDoctor: async (data) => {
    const response = await client.post('/Doctors', data);
    return response.data;
  },

  /**
   * Get doctors for the authenticated hospital (or all doctors for Admins).
   */
  getDoctors: async () => {
    const response = await client.get('/Doctors');
    return response.data;
  },

  /**
   * Get a specific doctor by their ID.
   * @param {string} id - Doctor GUID
   */
  getDoctorById: async (id) => {
    const response = await client.get(`/Doctors/${id}`);
    return response.data;
  },

  /**
   * Delete a doctor created by the authenticated hospital (removes login; keeps request history).
   * @param {string} id - Doctor GUID
   */
  deleteDoctor: async (id) => {
    const response = await client.delete(`/Doctors/${id}`);
    return response.data;
  }
};

export default doctorApi;
