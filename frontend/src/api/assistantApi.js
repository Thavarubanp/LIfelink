import client from './client';

export const assistantApi = {
  /**
   * Universal assistant (Supervisor agent). With acceptanceId it is a turn of the donor's own screening
   * interview; an empty message resumes the interview. History stays in the browser.
   */
  // structured: screening answers from the inputs inside the question bubble ({ fieldId: value })
  chat: async ({ message = '', history = [], acceptanceId = null, structured = null }) => {
    const response = await client.post('/assistant/chat', { message, history, acceptanceId, structured }, { timeout: 70000 });
    return response.data;
  }
};

export default assistantApi;
