import { useEffect, useState } from 'react';
import { Loader2, Package } from 'lucide-react';
import { inventoryApi } from '../../api';
import { Badge } from '../common/Badge';
import { getApiErrorMessage } from '../../utils/errorUtils';
import { formatDisplayDate } from '../../utils/dateUtils';

const fmtDate = (value) => formatDisplayDate(value, '-');

/**
 * Lets hospital staff choose the exact packets to issue, transfer or donate: the signed-in hospital's Available,
 * unexpired packets of one blood group (the server scopes the list to the hospital). `exact` requires that many
 * packets; `max` caps the selection. The server re-checks every rule.
 */
export const PacketPicker = ({ bloodGroup, selected, onChange, exact = null, max = null }) => {
  const [packets, setPackets] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const limit = exact ?? max;

  useEffect(() => {
    let active = true;
    const load = async () => {
      setLoading(true);
      setError('');
      try {
        const res = await inventoryApi.getPackets({ status: 'Available', bloodGroup });
        const list = (res?.data || []).filter((p) => new Date(p.expiryDate) > new Date());
        list.sort((a, b) => new Date(a.expiryDate) - new Date(b.expiryDate));
        if (active) setPackets(list);
      } catch (err) {
        if (active) setError(getApiErrorMessage(err));
      } finally {
        if (active) setLoading(false);
      }
    };
    load();
    return () => { active = false; };
  }, [bloodGroup]);

  const toggle = (id) => {
    if (selected.includes(id)) {
      onChange(selected.filter((x) => x !== id));
    } else if (!limit || selected.length < limit) {
      onChange([...selected, id]);
    }
  };

  const selectEarliest = () => onChange(packets.slice(0, limit || packets.length).map((p) => p.packetId));

  if (loading) {
    return <div className="p-4 flex items-center gap-2 text-xs text-slate-400"><Loader2 className="w-4 h-4 animate-spin text-red-500" /> Loading packets...</div>;
  }

  if (error) {
    return <p className="p-3 text-xs text-rose-600 dark:text-rose-400">{error}</p>;
  }

  if (packets.length === 0) {
    return (
      <div className="p-4 rounded-xl border border-dashed border-slate-200 dark:border-slate-700 text-xs text-slate-500 flex items-center gap-2">
        <Package className="w-4 h-4 text-slate-400" /> No available {bloodGroup} packets in your inventory.
      </div>
    );
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2 text-[11px]">
        <span className={`font-semibold ${exact && selected.length !== exact ? 'text-amber-600 dark:text-amber-400' : 'text-slate-600 dark:text-slate-300'}`}>
          {selected.length} selected{exact ? ` of ${exact} required` : max ? ` (up to ${max})` : ''} - {packets.length} available
        </span>
        <button type="button" onClick={selectEarliest} className="font-semibold text-red-600 hover:underline">
          Select earliest expiring
        </button>
      </div>
      <ul className="max-h-60 overflow-y-auto divide-y divide-slate-100 dark:divide-slate-800 rounded-xl border border-slate-200 dark:border-slate-700">
        {packets.map((p) => {
          const checked = selected.includes(p.packetId);
          const disabled = !checked && !!limit && selected.length >= limit;
          return (
            <li key={p.packetId}>
              <label className={`flex items-center gap-3 px-3 py-2 text-xs ${disabled ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer hover:bg-slate-50 dark:hover:bg-slate-800/60'}`}>
                <input type="checkbox" checked={checked} disabled={disabled} onChange={() => toggle(p.packetId)} className="accent-red-600" />
                <span className="font-mono font-semibold text-slate-800 dark:text-slate-100">{p.trackingNumber}</span>
                <span className="text-slate-500 hidden sm:inline">Collected {fmtDate(p.collectionDate)}</span>
                <span className="ml-auto flex items-center gap-1 text-slate-500">
                  Expires {fmtDate(p.expiryDate)} {p.isExpiringSoon && <Badge variant="warning" size="sm">Soon</Badge>}
                </span>
              </label>
            </li>
          );
        })}
      </ul>
    </div>
  );
};

export default PacketPicker;
