import { useEffect, useState } from 'react';
import { endpointApi } from '../api/endpointApi';

/** The subdomains the platform registers endpoints under, read once; nothing while unread or refused. */
export function useEndpoints(): string[] {
  const [subdomains, setSubdomains] = useState<string[]>([]);
  useEffect(() => {
    let current = true;
    endpointApi
      .getAll()
      .then((endpoints) => { if (current) setSubdomains(endpoints.map((endpoint) => endpoint.Subdomain).filter(Boolean).sort()); })
      .catch(() => { if (current) setSubdomains([]); });
    return () => { current = false; };
  }, []);
  return subdomains;
}
