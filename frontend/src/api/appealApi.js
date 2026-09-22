import client from './client';

export const appealApi = {
  submitAppeal: async (dto) => {
    const response = await client.post('/Appeals', dto);
    return response.data;
  },

  getMyAppeals: async () => {
    const response = await client.get('/Appeals/my');
    return response.data;
  }
};

export default appealApi;
