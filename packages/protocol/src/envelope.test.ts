import { describe, it, expect } from 'vitest'
import { createEnvelope, parseEnvelope, serializeEnvelope, PROTOCOL_VERSION } from './envelope.js'

describe('createEnvelope', () => {
  it('creates an envelope with correct fields', () => {
    const envelope = createEnvelope('test_message', { foo: 'bar' })

    expect(envelope.version).toBe(PROTOCOL_VERSION)
    expect(envelope.type).toBe('test_message')
    expect(envelope.seq).toBeGreaterThan(0)
    expect(envelope.timestamp).toBeTruthy()
    expect(envelope.payload).toEqual({ foo: 'bar' })
  })

  it('increments sequence numbers', () => {
    const e1 = createEnvelope('msg1', {})
    const e2 = createEnvelope('msg2', {})

    expect(e2.seq).toBe(e1.seq + 1)
  })
})

describe('serializeEnvelope / parseEnvelope', () => {
  it('round-trips an envelope through JSON', () => {
    const original = createEnvelope('round_trip', { value: 42 })
    const serialized = serializeEnvelope(original)
    const parsed = parseEnvelope(serialized)

    expect(parsed.version).toBe(original.version)
    expect(parsed.type).toBe(original.type)
    expect(parsed.seq).toBe(original.seq)
    expect(parsed.payload).toEqual(original.payload)
  })

  it('rejects invalid JSON', () => {
    expect(() => parseEnvelope('not json')).toThrow()
  })

  it('rejects a message missing required fields', () => {
    expect(() => parseEnvelope('{"type":"test"}')).toThrow()
  })
})
