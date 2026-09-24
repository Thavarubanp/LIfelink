import client from './client';

export const notificationApi = {
  getAllNotifications: async () => {
    const response = await client.get('/notifications');
    return response.data;
  },

  getMyNotifications: async () => {
    const response = await client.get('/notifications/my');
    return response.data;
  },

  getUserNotifications: async (userId) => {
    const response = await client.get(`/notifications/user/${userId}`);
    return response.data;
  },

  getUnreadCount: async () => {
    const response = await client.get('/notifications/unread-count');
    return response.data;
  },

  markRead: async (notificationId) => {
    const response = await client.patch(`/notifications/${notificationId}/read`);
    return response.data;
  },

  /** Dismiss (permanently delete) one of the caller's notifications */
  dismiss: async (notificationId) => {
    const response = await client.delete(`/notifications/${notificationId}`);
    return response.data;
  },

  markAllRead: async () => {
    const response = await client.patch('/notifications/read-all');
    return response.data;
  },

  createRecommendationNotification: async (dto) => {
    const response = await client.post('/notifications/recommendations', dto);
    return response.data;
  }
};

export default notificationApi;
