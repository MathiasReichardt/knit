import { describe, expect, it } from 'vitest'
import { layerPath, type ConfigLayer } from './config'

const layers: ConfigLayer[] = [
  { label: 'global', path: '/home/a/.config/knit/config.json' },
  { label: 'config.json', path: '/repo/.knit/config.json' },
  { label: 'config.local.json', path: '/repo/.knit/config.local.json' },
]

describe('layerPath', () => {
  it('names the file a value came from', () => {
    expect(layerPath(layers, 'global')).toBe('/home/a/.config/knit/config.json')
    expect(layerPath(layers, 'config.local.json')).toBe('/repo/.knit/config.local.json')
  })

  it('has no file for a default, so the badge carries no tooltip', () => {
    expect(layerPath(layers, 'default')).toBeNull()
  })

  it('survives a layer the server did not describe', () => {
    expect(layerPath([], 'config.json')).toBeNull()
  })
})
