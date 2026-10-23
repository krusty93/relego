/**
 * Region selection, the one path that changes which notebook the extension reads.
 *
 * Both the first pairing and a later switch go through here, because both are the same act: ask the
 * user for the chosen region's notebook host, then tell the server which region the connection
 * reads. Nothing else is allowed to move the region — there is no free-text host anywhere in the
 * extension, and a host-shaped or unvalidated id is rejected before any permission is requested.
 *
 * The order matters and is what makes a refusal harmless: access is requested first, the server is
 * told second, and the previous region's host is released last. A denied prompt or a rejected call
 * therefore leaves the connection reading the region it already read.
 */

import { REGIONS, resolveRegion } from "./profile";
import type { KindleRegion } from "./profile";
import { notebookOrigin, serverOriginPattern, staleNotebookOrigins } from "./permissions";
import type { HostPermissions } from "./permissions";

/** One row of the region picker, in the words the popup shows. */
export interface RegionOption {
  /** Allowlisted region id, used as the option value. */
  readonly value: string;
  /** Label the user reads, naming the region and the host it reads. */
  readonly label: string;
  /** Notebook host the option reads. */
  readonly host: string;
}

/**
 * Rows of the region picker, in allowlist order.
 *
 * The list is the allowlist: the picker can only offer these ids, so no host the extension is not
 * built to read can be selected.
 */
export function regionOptions(): readonly RegionOption[] {
  return REGIONS.map((region) => ({
    value: region.id,
    label: `${region.displayName} — ${region.notebookHost}`,
    host: region.notebookHost,
  }));
}

/** What the user asked for when they saved the popup. */
export interface RegionChoiceRequest {
  /** Region id selected in the picker. */
  readonly regionId: string;
  /** Region the connection reads now, or `null` when the server has none yet. */
  readonly currentRegionId: string | null;
  /** Server address that also needs access, or `null` when the connection is already paired. */
  readonly serverBaseUrl: string | null;
}

/** The calls the region flow makes outside itself. */
export interface RegionChoicePorts {
  /** Host permissions. */
  readonly permissions: HostPermissions;
  /** Pairs the connection with the chosen region; resolves to the frequency to apply. */
  readonly pair: (regionId: string) => Promise<number>;
  /** Switches an already paired connection to the chosen region; resolves to the queued command. */
  readonly setRegion: (regionId: string) => Promise<string>;
}

/** What happened to the user's choice. */
export type RegionChoice =
  | {
      readonly kind: "region_selected";
      readonly region: KindleRegion;
      /** Frequency the server reported, when this choice paired the connection. */
      readonly intervalMinutes: number | null;
      /** Command the server queued, when this choice switched an existing connection. */
      readonly pendingCommand: string | null;
      /** Hosts released because the connection no longer reads them. */
      readonly released: readonly string[];
    }
  | { readonly kind: "already_selected"; readonly region: KindleRegion }
  | { readonly kind: "access_denied"; readonly region: KindleRegion; readonly origins: readonly string[] }
  | { readonly kind: "unsupported_region"; readonly regionId: string }
  | { readonly kind: "invalid_server_url"; readonly serverBaseUrl: string }
  | { readonly kind: "failed"; readonly region: KindleRegion; readonly status: number | null };

/**
 * Applies a region choice.
 *
 * @param request What the user selected and what the connection reads now.
 * @param ports Host permissions and the server calls for the chosen region.
 * @returns The outcome; never throws, so the popup always has something to say.
 */
export async function chooseRegion(
  request: RegionChoiceRequest,
  ports: RegionChoicePorts,
): Promise<RegionChoice> {
  const region = resolveRegion(request.regionId);
  if (region === null) return { kind: "unsupported_region", regionId: request.regionId };

  const serverPattern =
    request.serverBaseUrl === null ? null : serverOriginPattern(request.serverBaseUrl);
  if (request.serverBaseUrl !== null && serverPattern === null) {
    return { kind: "invalid_server_url", serverBaseUrl: request.serverBaseUrl };
  }

  if (serverPattern === null && request.currentRegionId === region.id) {
    return await reselect(region, ports.permissions);
  }

  const origins = serverPattern === null ? [notebookOrigin(region)] : [serverPattern, notebookOrigin(region)];
  if (!(await ports.permissions.request(origins))) {
    return { kind: "access_denied", region, origins };
  }

  try {
    const intervalMinutes = serverPattern === null ? null : await ports.pair(region.id);
    const pendingCommand = serverPattern === null ? await ports.setRegion(region.id) : null;

    return {
      kind: "region_selected",
      region,
      intervalMinutes,
      pendingCommand,
      released: await releaseOtherNotebookOrigins(region, ports.permissions),
    };
  } catch (error) {
    // Access was the only thing that changed, so hand it back: the connection still reads the
    // region it read before this attempt.
    await ports.permissions.remove([notebookOrigin(region)]);

    return { kind: "failed", region, status: statusOf(error) };
  }
}

/**
 * Handles re-selecting the region the connection already reads.
 *
 * Nothing is asked of the server: switching to the same region would interrupt a running job and
 * queue a full resync for a notebook that did not change. The host grant is still repaired, because
 * a reinstalled or newly granted extension can have the region without the access.
 */
async function reselect(region: KindleRegion, permissions: HostPermissions): Promise<RegionChoice> {
  const origins = [notebookOrigin(region)];

  if (await permissions.contains(origins)) return { kind: "already_selected", region };
  if (await permissions.request(origins)) return { kind: "already_selected", region };

  return { kind: "access_denied", region, origins };
}

/** Releases every granted notebook host the selected region does not read. */
export async function releaseOtherNotebookOrigins(
  region: KindleRegion,
  permissions: HostPermissions,
): Promise<readonly string[]> {
  const stale = staleNotebookOrigins(await permissions.granted(), region);
  if (stale.length > 0) await permissions.remove(stale);

  return stale;
}

/** Reads the HTTP status out of a rejected server call, when it has one. */
function statusOf(error: unknown): number | null {
  const status = (error as { readonly status?: unknown } | null)?.status;

  return typeof status === "number" ? status : null;
}
