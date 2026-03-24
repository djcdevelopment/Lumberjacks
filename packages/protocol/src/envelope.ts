import { z } from 'zod'

export const PROTOCOL_VERSION = 1

export const EnvelopeSchema = z.object({
  version: z.number().int(),
  type: z.string(),
  seq: z.number().int(),
  timestamp: z.string().datetime(),
  payload: z.unknown(),
})

export type Envelope = z.infer<typeof EnvelopeSchema>

let seqCounter = 0

export function createEnvelope(type: string, payload: unknown): Envelope {
  return {
    version: PROTOCOL_VERSION,
    type,
    seq: ++seqCounter,
    timestamp: new Date().toISOString(),
    payload,
  }
}

export function parseEnvelope(raw: string): Envelope {
  const data = JSON.parse(raw)
  return EnvelopeSchema.parse(data)
}

export function serializeEnvelope(envelope: Envelope): string {
  return JSON.stringify(envelope)
}
