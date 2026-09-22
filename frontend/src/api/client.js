import axios from 'axios';

// Base Axios instance matching LifeLink ASP.NET Core backend API
const client = axios.create({
  baseURL: '/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

// Request interceptor to automatically attach JWT Bearer token
client.interceptors.request.use(
  (config) => {
    const token = localStorage.getItem('lifelink_token');
    if (token) {
      config.headers.Authorization = `Bearer ${token}`;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Response interceptor for centralized error handling and 401 session expiry
client.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response ? error.response.status : null;
    
    if (status === 401) {
      // Clear expired credentials
      localStorage.removeItem('lifelink_token');
      localStorage.removeItem('lifelink_user');
      
      // Redirect to login if not already on auth page
      if (!window.location.pathname.startsWith('/login') && !window.location.pathname.startsWith('/register')) {
        window.location.href = '/login?expired=1';
      }
    }
    
    return Promise.reject(error);
  }
);

export default client;
