declare module 'virtual:__federation__' {
  export function __federation_method_setRemote(
    name: string,
    cfg: { url: string | (() => Promise<string>); format: 'esm' | 'var'; from: 'vite' | 'webpack' },
  ): void;
  export function __federation_method_getRemote(name: string, exposed: string): Promise<unknown>;
  export function __federation_method_unwrapDefault(mod: unknown): unknown;
}
