import client from './client';

export const governanceApi = {
  getStatus: async () => {
    const response = await client.get('/governance/status');
    return response.data;
  }
};

export default governanceApi;
