import React from 'react';
import AgentStatusCard from './AgentStatusCard';

export const InventoryAnalysisStatus = ({ lowStockCount = 0, surplusCount = 0 }) => {
  return (
    <AgentStatusCard
      type="inventory"
      title="Inventory Insights Engine"
      description="Automated regional blood bank monitoring active. Real-time scanning for low-stock thresholds and surplus allocation routes."
      metrics={[
        { label: 'Low Stock Alerts', value: `${lowStockCount} Categories` },
        { label: 'Surplus Reserves', value: `${surplusCount} Categories` },
        { label: 'Optimization Rate', value: '98.4%' }
      ]}
      status="active"
    />
  );
};

export default InventoryAnalysisStatus;
