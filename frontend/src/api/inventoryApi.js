import client from './client';

export const inventoryApi = {
  createInventory: async (dto) => {
    const response = await client.post('/Inventory', dto);
    return response.data;
  },

  getAllInventory: async () => {
    const response = await client.get('/Inventory');
    return response.data;
  },

  getLowStockInventory: async () => {
    const response = await client.get('/Inventory/low-stock');
    return response.data;
  },

  getSurplusInventory: async () => {
    const response = await client.get('/Inventory/surplus');
    return response.data;
  },

  getInventoryById: async (id) => {
    const response = await client.get(`/Inventory/${id}`);
    return response.data;
  },

  getInventoryTransactions: async (id) => {
    const response = await client.get(`/Inventory/${id}/transactions`);
    return response.data;
  },

  updateInventory: async (id, dto) => {
    const response = await client.put(`/Inventory/${id}`, dto);
    return response.data;
  },

  deleteInventory: async (id) => {
    const response = await client.delete(`/Inventory/${id}`);
    return response.data;
  },

  // Packets of the signed-in hospital; pass packetId to get one packet with its full history
  getPackets: async (params = {}) => {
    const response = await client.get('/Inventory/packets', { params });
    return response.data;
  },

  getHospitalInventory: async (hospitalId) => {
    const response = await client.get(`/Inventory/hospital/${hospitalId}`);
    return response.data;
  }
};

export default inventoryApi;
