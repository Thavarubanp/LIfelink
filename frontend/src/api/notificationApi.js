import client from './client';

export const notificationApi = {
  getAllNotifications: async () => {
    const response = await client.get('/notifications');
    return response.data;
  },

  getUserNotifications: async (userId) => {
    const response = await client.get(`/notifications/user/${userId}`);
    return response.data;
  },

  createRecommendationNotification: async (dto) => {
    const response = await client.post('/notifications/recommendations', dto);
    return response.data;
  }
};

export default notificationApi;
