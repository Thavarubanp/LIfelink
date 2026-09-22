import React from 'react';
import WorkflowTracker from './WorkflowTracker';

export const SmartMatchingProgress = ({ status }) => {
  const steps = [
    { label: 'Request Created', description: 'Patient request logged' },
    { label: 'Hospital Verification', description: 'Hospital confirms urgency' },
    { label: 'Smart Donor Match', description: 'AI ranks nearby donors' },
    { label: 'Notifications Sent', description: 'Donors alerted via push/SMS' }
  ];

  const getStepIndex = () => {
    switch (status) {
      case 'PENDING': return 0;
      case 'VERIFIED': return 1;
      case 'MATCHING': return 2;
      case 'FULFILLED': return 4;
      default: return 1;
    }
  };

  return <WorkflowTracker steps={steps} currentStepIndex={getStepIndex()} title="Workflow A: Patient Request & Donor Dispatch" />;
};

export default SmartMatchingProgress;
