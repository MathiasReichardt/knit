import { beforeEach, describe, expect, it } from 'vitest'
import { useConnection } from './connection'

function reset() {
  useConnection.setState({ status: 'open', checking: false, errorsSinceOpen: 0 })
}

describe('connection store', () => {
  beforeEach(reset)

  it('suppresses the first error (a drop fires once before the retry)', () => {
    useConnection.getState().reportError()
    expect(useConnection.getState().status).toBe('open')
    expect(useConnection.getState().errorsSinceOpen).toBe(1)
  })

  it('surfaces disconnected when the retry also fails', () => {
    useConnection.getState().reportError()
    useConnection.getState().reportError()
    expect(useConnection.getState().status).toBe('disconnected')
  })

  it('reportOpen clears the banner and resets the counter', () => {
    useConnection.getState().reportError()
    useConnection.getState().reportError()
    useConnection.getState().reportOpen()
    expect(useConnection.getState().status).toBe('open')
    expect(useConnection.getState().errorsSinceOpen).toBe(0)
  })

  it('stays disconnected on further errors after the threshold', () => {
    useConnection.getState().reportError()
    useConnection.getState().reportError()
    useConnection.getState().reportError()
    expect(useConnection.getState().status).toBe('disconnected')
    expect(useConnection.getState().errorsSinceOpen).toBe(3)
  })

  it('reportError clears the in-flight checking flag', () => {
    useConnection.getState().setChecking(true)
    useConnection.getState().reportError()
    expect(useConnection.getState().checking).toBe(false)
  })
})
