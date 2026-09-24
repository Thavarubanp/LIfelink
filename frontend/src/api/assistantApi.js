import client from './client';

export const assistantApi = {
  /**
   * Universal assistant (Supervisor agent). With acceptanceId it is a turn of the donor's own screening
   * interview; an empty message resumes the interview. History stays in the browser.
   */
  chat: async ({ message = '', history = [], acceptanceId = null }) => {
    const response = await client.post('/assistant/chat', { message, history, acceptanceId }, { timeout: 70000 });
    return response.data;
  }
};

export default assistantApi;
