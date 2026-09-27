import { useEffect, useMemo, useState } from 'react';
import ThemeContext from './themeContext';

const DARK_MODE_QUERY = '(prefers-color-scheme: dark)';
const getSystemTheme = () => {
  if (typeof window === 'undefined' || !window.matchMedia) return 'light';
  return window.matchMedia(DARK_MODE_QUERY).matches ? 'dark' : 'light';
};

const applyTheme = (theme) => {
  const isDark = theme === 'dark';
  document.documentElement.classList.toggle('dark', isDark);
  document.documentElement.style.colorScheme = theme;
  document.querySelector('meta[name=theme-color]')?.setAttribute(
    'content',
    isDark ? '#020617' : '#ffffff',
  );
};

export const ThemeProvider = ({ children }) => {
  const [systemTheme, setSystemTheme] = useState(getSystemTheme);
  const [manualTheme, setManualTheme] = useState(null);
  const theme = manualTheme ?? systemTheme;

  useEffect(() => {
    const mediaQuery = window.matchMedia(DARK_MODE_QUERY);
    const handleSystemThemeChange = (event) => {
      setSystemTheme(event.matches ? 'dark' : 'light');
    };

    mediaQuery.addEventListener('change', handleSystemThemeChange);
    return () => mediaQuery.removeEventListener('change', handleSystemThemeChange);
  }, []);

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  const value = useMemo(() => ({
    theme,
    toggleTheme: () => {
      setManualTheme((currentTheme) => {
        const activeTheme = currentTheme ?? systemTheme;
        return activeTheme === 'dark' ? 'light' : 'dark';
      });
    },
  }), [systemTheme, theme]);

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
};
