import client from './client';

export const appealApi = {
  submitAppeal: async (dto) => {
    const response = await client.post('/Appeals', dto);
    return response.data;
  },

  /** Appellant reply { notes, attachmentUrl?, attachmentName? } — after an admin message */
  replyToAppeal: async (id, dto) => {
    const response = await client.post(`/Appeals/${id}/reply`, dto);
    return response.data;
  },

  getMyAppeals: async () => {
    const response = await client.get('/Appeals/my');
    return response.data;
  }
};

export default appealApi;
