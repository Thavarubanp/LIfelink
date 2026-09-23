import React, { useState } from 'react';
import { BrowserRouter, Routes, Route, Navigate, Outlet } from 'react-router-dom';
import { AuthProvider, useAuth } from './context/AuthContext';
import { NotificationProvider } from './context/NotificationContext';
import { getDashboardPath } from './utils/roleUtils';

import Navbar from './components/common/Navbar';
import Sidebar from './components/common/Sidebar';
import Toast from './components/common/Toast';
import NotificationCenterDrawer from './components/notifications/NotificationCenterDrawer';
import ProtectedRoute from './components/common/ProtectedRoute';

// Auth Pages
import LoginPage from './pages/auth/LoginPage';
import ForgotPasswordPage from './pages/auth/ForgotPasswordPage';
import RegisterPage from './pages/auth/RegisterPage';
import RegisterHospitalPage from './pages/auth/RegisterHospitalPage';
import WaitingForApprovalPage from './pages/auth/WaitingForApprovalPage';

// Donor Pages
import DonorDashboard from './pages/donor/DonorDashboard';
import AvailableRequestsPage from './pages/donor/AvailableRequestsPage';
import RequestDetailPage from './pages/donor/RequestDetailPage';
import CreatePatientRequestPage from './pages/donor/CreatePatientRequestPage';
import MyAcceptancesPage from './pages/donor/MyAcceptancesPage';
import MyProfilePage from './pages/profiles/MyProfilePage';
import DonorComplaintsPage from './pages/donor/DonorComplaintsPage';

// Doctor Pages
import DoctorDashboard from './pages/doctor/DoctorDashboard';
import ScreeningReportsPage from './pages/doctor/ScreeningReportsPage';
import DoctorChangePasswordPage from './pages/doctor/DoctorChangePasswordPage';

// Hospital Staff Pages
import HospitalDashboard from './pages/hospital/HospitalDashboard';
import InventoryManagementPage from './pages/hospital/InventoryManagementPage';
import EmergencyHubPage from './pages/hospital/EmergencyHubPage';
import DoctorManagementPage from './pages/hospital/DoctorManagementPage';
import VerifyBloodRequestsPage from './pages/hospital/VerifyBloodRequestsPage';

// Admin Pages
import AdminDashboard from './pages/admin/AdminDashboard';
import HospitalManagementPage from './pages/admin/HospitalManagementPage';
import AdminComplaintsPage from './pages/admin/AdminComplaintsPage';
import AdminAppealsPage from './pages/admin/AdminAppealsPage';

// Governance Page
import SuspendedGovernancePage from './pages/governance/SuspendedGovernancePage';

// Profile Pages
import HospitalProfilePage from './pages/profiles/HospitalProfilePage';
import UserProfilePage from './pages/profiles/UserProfilePage';
import DoctorProfilePage from './pages/profiles/DoctorProfilePage';

// Authenticated Shell Layout with Navbar & Sidebar
const DashboardLayout = ({ onOpenNotifications }) => {
  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-100 flex flex-col font-sans transition-colors">
      <Navbar onOpenNotifications={onOpenNotifications} />
      <div className="flex-1 flex overflow-hidden">
        <Sidebar />
        <main className="flex-1 p-4 md:p-8 overflow-y-auto max-w-7xl mx-auto w-full">
          <Outlet />
        </main>
      </div>
    </div>
  );
};

// Sends "/" (and the catch-all fallback) to the signed-in user's own dashboard
const RoleHomeRedirect = () => {
  const { user } = useAuth();
  return <Navigate to={getDashboardPath(user)} replace />;
};

export function App() {
  const [notificationsOpen, setNotificationsOpen] = useState(false);

  return (
    <AuthProvider>
      <NotificationProvider>
        <BrowserRouter>
          <Routes>
            {/* Public Auth Routes */}
            <Route path="/login" element={<LoginPage />} />
            <Route path="/admin/login" element={<LoginPage initialRole="admin" />} />
            <Route path="/forgot-password" element={<ForgotPasswordPage />} />
            <Route path="/register" element={<RegisterPage />} />
            <Route path="/register-hospital" element={<RegisterHospitalPage />} />
            <Route path="/hospital/waiting-approval" element={<WaitingForApprovalPage />} />

            {/* Suspended Access Route */}
            <Route
              path="/governance/status"
              element={
                <ProtectedRoute allowSuspended={true}>
                  <SuspendedGovernancePage />
                </ProtectedRoute>
              }
            />

            {/* Doctor First-Login Change Password — standalone full-screen, outside DashboardLayout */}
            <Route
              path="/doctor/change-password"
              element={
                <ProtectedRoute allowedRoles={['Doctor']}>
                  <DoctorChangePasswordPage />
                </ProtectedRoute>
              }
            />

            {/* Authenticated Dashboard Routes */}
            <Route
              element={
                <ProtectedRoute>
                  <DashboardLayout onOpenNotifications={() => setNotificationsOpen(true)} />
                </ProtectedRoute>
              }
            >
              {/* Default Root Redirect (role-aware) */}
              <Route path="/" element={<RoleHomeRedirect />} />


              {/* Donor Module */}
              <Route
                path="/donor/dashboard"
                element={
                  <ProtectedRoute allowedRoles={['User']}>
                    <DonorDashboard />
                  </ProtectedRoute>
                }
              />
              <Route path="/donor/requests" element={<AvailableRequestsPage />} />
              {/* Shared Create Blood Request page (with My Requests below the form) */}
              <Route
                path="/donor/requests/create"
                element={
                  <ProtectedRoute allowedRoles={['User', 'HospitalStaff', 'Admin']}>
                    <CreatePatientRequestPage />
                  </ProtectedRoute>
                }
              />
              <Route path="/donor/requests/:id" element={<RequestDetailPage />} />
              <Route path="/donor/my-requests" element={<Navigate to="/donor/requests/create" replace />} />
              <Route path="/donor/acceptances" element={<MyAcceptancesPage />} />
              <Route path="/donor/profile" element={<Navigate to="/my-profile" replace />} />
              <Route path="/donor/complaints" element={<DonorComplaintsPage />} />
              <Route path="/complaints" element={<DonorComplaintsPage />} />

              {/* Doctor Module */}
              <Route
                path="/doctor/dashboard"
                element={
                  <ProtectedRoute allowedRoles={['Doctor', 'Admin']}>
                    <DoctorDashboard />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/doctor/screenings"
                element={
                  <ProtectedRoute allowedRoles={['Doctor', 'Admin']}>
                    <ScreeningReportsPage />
                  </ProtectedRoute>
                }
              />

              {/* Hospital Staff Module */}
              <Route
                path="/hospital/dashboard"
                element={
                  <ProtectedRoute allowedRoles={['HospitalStaff', 'Admin']}>
                    <HospitalDashboard />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/hospital/inventory"
                element={
                  <ProtectedRoute allowedRoles={['HospitalStaff', 'Admin']}>
                    <InventoryManagementPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/hospital/emergency"
                element={
                  <ProtectedRoute allowedRoles={['HospitalStaff', 'Admin']}>
                    <EmergencyHubPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/hospital/requests/verify"
                element={
                  <ProtectedRoute allowedRoles={['HospitalStaff']}>
                    <VerifyBloodRequestsPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/hospital/doctors"
                element={
                  <ProtectedRoute allowedRoles={['HospitalStaff']}>
                    <DoctorManagementPage />
                  </ProtectedRoute>
                }
              />

              {/* System Admin Module */}
              <Route
                path="/admin/dashboard"
                element={
                  <ProtectedRoute allowedRoles={['Admin']}>
                    <AdminDashboard />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/hospitals/pending"
                element={
                  <ProtectedRoute allowedRoles={['Admin']}>
                    <HospitalManagementPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/complaints"
                element={
                  <ProtectedRoute allowedRoles={['Admin']}>
                    <AdminComplaintsPage />
                  </ProtectedRoute>
                }
              />
              <Route
                path="/admin/appeals"
                element={
                  <ProtectedRoute allowedRoles={['Admin']}>
                    <AdminAppealsPage />
                  </ProtectedRoute>
                }
              />

              {/* Profiles Module (Accessible to all authenticated users) */}
              <Route path="/my-profile" element={<MyProfilePage />} />
              <Route path="/profiles/hospital/:id" element={<HospitalProfilePage />} />
              <Route path="/profiles/user/:id" element={<UserProfilePage />} />
              <Route path="/profiles/doctor/:id" element={<DoctorProfilePage />} />
            </Route>

            {/* Fallback Catch-all Route */}
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>

          {/* Global Toast Alerts */}
          <Toast />

          {/* Notification Slide-Over Drawer */}
          <NotificationCenterDrawer
            isOpen={notificationsOpen}
            onClose={() => setNotificationsOpen(false)}
          />
        </BrowserRouter>
      </NotificationProvider>
    </AuthProvider>
  );
}

export default App;
