import React from 'react';
import WorkflowTracker from './WorkflowTracker';

export const EmergencyResponseTimeline = ({ status }) => {
  const steps = [
    { label: 'Emergency Triggered', description: 'Critical broadcast active' },
    { label: 'Inventory Analysis', description: 'Scanning regional stock' },
    { label: 'Transfer Suggested', description: 'Inter-hospital transfer route' },
    { label: 'Emergency Match', description: 'Donors dispatched' }
  ];

  const getStepIndex = () => {
    switch (status) {
      case 'CRITICAL': return 1;
      case 'APPROVED': return 2;
      case 'COMPLETED': return 4;
      default: return 1;
    }
  };

  return <WorkflowTracker steps={steps} currentStepIndex={getStepIndex()} title="Workflow C: Emergency Blood & Transfer Coordination" />;
};

export default EmergencyResponseTimeline;
