import logoArtwork from '../../assets/brand/lifelink-logo.png';

const sizes = {
  sm: { image: 'h-9 w-9 rounded-xl', name: 'text-lg', tagline: 'text-[9px]' },
  md: { image: 'h-12 w-12 rounded-2xl', name: 'text-2xl', tagline: 'text-[10px]' },
  lg: { image: 'h-16 w-16 rounded-2xl', name: 'text-3xl', tagline: 'text-xs' },
};

export const BrandLogo = ({ size = 'md', compact = false, inverse = false, tagline = 'Blood donation & emergency care', className = '' }) => {
  const styles = sizes[size] || sizes.md;
  return (
    <span className={`inline-flex items-center gap-3 ${className}`} aria-label="LifeLink">
      <img src={logoArtwork} alt="" className={`${styles.image} shrink-0 object-cover shadow-sm ring-1 ring-black/5 dark:ring-white/10`} />
      {!compact && (
        <span className="flex min-w-0 flex-col text-left">
          <span className={`${styles.name} font-extrabold leading-none tracking-tight ${inverse ? 'text-white' : 'text-slate-950 dark:text-white'}`}>Life<span className="text-red-600 dark:text-red-500">Link</span></span>
          {tagline && <span className={`${styles.tagline} mt-1 truncate font-semibold uppercase tracking-[0.14em] ${inverse ? 'text-slate-300' : 'text-slate-500 dark:text-slate-400'}`}>{tagline}</span>}
        </span>
      )}
    </span>
  );
};

export default BrandLogo;
