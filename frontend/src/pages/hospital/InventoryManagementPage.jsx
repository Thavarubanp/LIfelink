import React, { useState, useEffect } from 'react';
import { inventoryApi } from '../../api';
import { DataTable } from '../../components/common/DataTable';
import { Badge } from '../../components/common/Badge';
import { useNotification } from '../../context/NotificationContext';
import { Plus, Edit, Trash2, AlertTriangle, Loader2 } from 'lucide-react';

export const InventoryManagementPage = () => {
  const [inventory, setInventory] = useState([]);
  const [loading, setLoading] = useState(true);
  const [modalOpen, setModalOpen] = useState(false);
  const [formData, setFormData] = useState({
    hospitalId: '00000000-0000-0000-0000-000000000000',
    bloodGroup: 'O+',
    unitsAvailable: 25,
    minimumThreshold: 5,
    maximumCapacity: 100
  });

  const { addToast } = useNotification();

  const fetchInventory = async () => {
    setLoading(true);
    try {
      const res = await inventoryApi.getAllInventory();
      setInventory(res.data || (Array.isArray(res) ? res : []));
    } catch (err) {
      console.error('Failed to load inventory:', err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchInventory();
  }, []);

  const handleCreate = async (e) => {
    e.preventDefault();
    try {
      await inventoryApi.createInventory(formData);
      addToast({ title: 'Stock Updated', message: 'Blood category added successfully.', type: 'success' });
      setModalOpen(false);
      fetchInventory();
    } catch (err) {
      addToast({ title: 'Operation Failed', message: err.response?.data?.message || 'Could not update inventory.', type: 'error' });
    }
  };

  const columns = [
    {
      header: 'Blood Category',
      accessor: 'bloodGroup',
      cell: (row) => <Badge variant="blood">{row.bloodGroup}</Badge>
    },
    {
      header: 'Units Available',
      accessor: 'unitsAvailable',
      cell: (row) => <span className="font-bold text-slate-900 dark:text-slate-100">{row.unitsAvailable} Units</span>
    },
    {
      header: 'Min Threshold',
      accessor: 'minimumThreshold',
      cell: (row) => <span className="text-slate-500">{row.minimumThreshold} Units</span>
    },
    {
      header: 'Max Capacity',
      accessor: 'maximumCapacity',
      cell: (row) => <span className="text-slate-500">{row.maximumCapacity || 100} Units</span>
    },
    {
      header: 'Stock Health',
      accessor: 'unitsAvailable',
      cell: (row) => {
        const isLow = row.unitsAvailable <= row.minimumThreshold;
        const isSurplus = row.unitsAvailable >= (row.maximumCapacity || 100) * 0.8;
        if (isLow) return <Badge variant="warning">Low Stock</Badge>;
        if (isSurplus) return <Badge variant="info">Surplus Reserve</Badge>;
        return <Badge variant="success">Optimal Stock</Badge>;
      }
    }
  ];

  return (
    <div className="space-y-6">
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h1 className="text-xl font-bold text-slate-900 dark:text-slate-100">Blood Bank Inventory Management</h1>
          <p className="text-xs text-slate-500 dark:text-slate-400">
            Monitor and manage blood unit reserves, low-stock triggers, and surplus capacities.
          </p>
        </div>
        <button
          onClick={() => setModalOpen(true)}
          className="inline-flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 text-white font-semibold text-xs rounded-xl shadow-md transition-all self-start"
        >
          <Plus className="w-4 h-4" />
          <span>Add / Modify Category</span>
        </button>
      </div>

      <DataTable
        columns={columns}
        data={inventory}
        searchPlaceholder="Search blood group..."
        emptyMessage="No inventory records found."
      />

      {/* Add Inventory Modal */}
      {modalOpen && (
        <div className="fixed inset-0 z-50 bg-slate-950/50 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="max-w-md w-full bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 rounded-2xl p-6 shadow-2xl">
            <h3 className="text-base font-bold text-slate-900 dark:text-slate-100 mb-4">Add Blood Inventory Record</h3>
            <form onSubmit={handleCreate} className="space-y-3 text-xs">
              <div>
                <label className="block font-semibold mb-1">Blood Group</label>
                <select
                  value={formData.bloodGroup}
                  onChange={(e) => setFormData({ ...formData, bloodGroup: e.target.value })}
                  className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl"
                >
                  {['A+', 'A-', 'B+', 'B-', 'AB+', 'AB-', 'O+', 'O-'].map((bg) => (
                    <option key={bg} value={bg}>{bg}</option>
                  ))}
                </select>
              </div>

              <div>
                <label className="block font-semibold mb-1">Units Available (Min 0)</label>
                <input
                  type="number"
                  min="0"
                  required
                  value={formData.unitsAvailable}
                  onChange={(e) => setFormData({ ...formData, unitsAvailable: parseInt(e.target.value) || 0 })}
                  className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl"
                />
              </div>

              <div>
                <label className="block font-semibold mb-1">Minimum Alert Threshold</label>
                <input
                  type="number"
                  min="0"
                  required
                  value={formData.minimumThreshold}
                  onChange={(e) => setFormData({ ...formData, minimumThreshold: parseInt(e.target.value) || 0 })}
                  className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl"
                />
              </div>

              <div>
                <label className="block font-semibold mb-1">Maximum Storage Capacity</label>
                <input
                  type="number"
                  min="1"
                  required
                  value={formData.maximumCapacity}
                  onChange={(e) => setFormData({ ...formData, maximumCapacity: parseInt(e.target.value) || 100 })}
                  className="w-full px-3 py-2 bg-slate-50 dark:bg-slate-800 border rounded-xl"
                />
              </div>

              <div className="flex gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setModalOpen(false)}
                  className="w-1/2 py-2.5 border rounded-xl font-semibold"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  className="w-1/2 py-2.5 bg-red-600 text-white rounded-xl font-semibold"
                >
                  Save Record
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};

export default InventoryManagementPage;
