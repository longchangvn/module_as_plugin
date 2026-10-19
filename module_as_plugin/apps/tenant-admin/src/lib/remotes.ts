import type { ComponentType } from 'react';
import {
  __federation_method_getRemote as getRemote,
  __federation_method_setRemote as setRemote,
  __federation_method_unwrapDefault as unwrapDefault,
} from 'virtual:__federation__';

// Runtime remote registry: scope -> remoteUrl (registered once per scope).
const registered = new Map<string, string>();

export async function loadRemoteComponent(scope: string, remoteUrl: string, exposes: string): Promise<ComponentType> {
  if (!registered.has(scope)) {
    setRemote(scope, { url: () => Promise.resolve(remoteUrl), format: 'esm', from: 'vite' });
    registered.set(scope, remoteUrl);
  }
  const mod = await getRemote(scope, exposes);
  return unwrapDefault(mod) as ComponentType;
}
