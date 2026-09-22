import React from 'react';
import WorkflowTracker from './WorkflowTracker';

export const ScreeningProgressTimeline = ({ status }) => {
  const steps = [
    { label: 'Acceptance Registered', description: 'Donor opted to donate' },
    { label: 'Health Screening', description: 'Interactive vitals check' },
    { label: 'AI Risk Evaluation', description: 'Intelligent health assessment' },
    { label: 'Clinical Approval', description: 'Doctor verification sign-off' }
  ];

  const getStepIndex = () => {
    switch (status) {
      case 'Accepted': return 1;
      case 'ScreeningPending': return 1;
      case 'ScreeningCompleted': return 2;
      case 'Verified': return 4;
      default: return 0;
    }
  };

  return <WorkflowTracker steps={steps} currentStepIndex={getStepIndex()} title="Workflow B: Donor Health & Clinical Evaluation" />;
};

export default ScreeningProgressTimeline;
