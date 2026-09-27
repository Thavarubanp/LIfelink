import { useEffect, useLayoutEffect, useRef } from 'react';
import { useTheme } from './useTheme';

const SYSTEM_THEME_QUERY = '(prefers-color-scheme: dark)';

const applyDocumentTheme = (isDark) => {
  document.documentElement.classList.toggle('dark', isDark);
  document.documentElement.style.colorScheme = isDark ? 'dark' : 'light';
  document.querySelector('meta[name=theme-color]')?.setAttribute(
    'content',
    isDark ? '#020617' : '#ffffff',
  );
};

export const useSystemThemePage = () => {
  const { theme } = useTheme();
  const appThemeRef = useRef(theme);

  useEffect(() => {
    appThemeRef.current = theme;
  }, [theme]);

  useLayoutEffect(() => {
    const mediaQuery = window.matchMedia(SYSTEM_THEME_QUERY);
    const applySystemTheme = (event) => applyDocumentTheme(event.matches);

    applyDocumentTheme(mediaQuery.matches);
    mediaQuery.addEventListener('change', applySystemTheme);

    return () => {
      mediaQuery.removeEventListener('change', applySystemTheme);
      applyDocumentTheme(appThemeRef.current === 'dark');
    };
  }, []);
};
