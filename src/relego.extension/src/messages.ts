/**
 * Messages the extension's own pages exchange with the service worker.
 *
 * The popup owns region choice, because only a user gesture can request host access, while the
 * worker owns the schedule. This is the one thing the popup has to tell it.
 */

/** Sent by the popup after it has paired the connection or changed the region. */
export const CONFIGURED_MESSAGE = "relego:configured";

/** Payload of the popup's configuration message. */
export interface ConfiguredMessage {
  /** Discriminator the popup sends. */
  readonly type: typeof CONFIGURED_MESSAGE;
  /** Pairing starts an initial sync; a region change picks up the queued full resync. */
  readonly reason: "paired" | "region_changed";
  /** Frequency the server reported, when the popup learned one. */
  readonly intervalMinutes?: number;
}

/**
 * Reports whether a runtime message is the popup's configuration message.
 *
 * @param message Anything received on the runtime channel.
 */
export function isConfiguredMessage(message: unknown): message is ConfiguredMessage {
  if (typeof message !== "object" || message === null) return false;
  return "type" in message && message.type === CONFIGURED_MESSAGE
    && "reason" in message && (message.reason === "paired" || message.reason === "region_changed");
}
