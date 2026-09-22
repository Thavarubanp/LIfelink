import React, { createContext, useContext, useState, useEffect } from 'react';
import { authApi } from '../api';

const AuthContext = createContext(null);

export const AuthProvider = ({ children }) => {
  const [user, setUser] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const initAuth = async () => {
      const token = localStorage.getItem('lifelink_token');
      if (token) {
        try {
          const res = await authApi.getCurrentUser();
          if (res.isSuccess && res.data) {
            setUser(res.data);
          } else {
            localStorage.removeItem('lifelink_token');
          }
        } catch (err) {
          console.error('Failed to fetch user context:', err);
          localStorage.removeItem('lifelink_token');
        }
      }
      setLoading(false);
    };

    initAuth();
  }, []);

  const login = async (credentials) => {
    const res = await authApi.login(credentials);
    const isSuccess = res && (res.success === true || res.isSuccess === true);
    const token = res?.data?.accessToken || res?.data?.token;

    if (isSuccess && token) {
      localStorage.setItem('lifelink_token', token);
      
      if (res.data.user) {
        setUser(res.data.user);
      } else {
        try {
          const meRes = await authApi.getCurrentUser();
          if (meRes?.data) setUser(meRes.data);
        } catch (e) {
          setUser({ email: credentials.email, roles: ['User'] });
        }
      }
      return res;
    }

    throw new Error(res?.message || 'Invalid email or password.');
  };

  const logout = async () => {
    try {
      await authApi.logout();
    } catch (e) {
      // Ignore network errors on logout
    } finally {
      localStorage.removeItem('lifelink_token');
      setUser(null);
      window.location.href = '/login';
    }
  };

  const hasRole = (roleName) => {
    if (!user || !user.roles) return false;
    if (Array.isArray(user.roles)) {
      return user.roles.includes(roleName);
    }
    return user.roles === roleName;
  };

  return (
    <AuthContext.Provider value={{ user, setUser, loading, login, logout, hasRole }}>
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = () => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
