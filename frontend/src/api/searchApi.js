import client from './client';

export const searchApi = {
  globalSearch: async (query) => {
    const response = await client.get('/search', {
      params: { q: query }
    });
    return response.data;
  }
};

export default searchApi;
