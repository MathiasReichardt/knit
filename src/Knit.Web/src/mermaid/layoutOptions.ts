import { readConfig, writeConfig, type Scalar } from './frontmatter'
import { DEFAULT_DIRECTION, DIRECTIONS, readDirection, writeDirection, type DirectionSyntax } from './direction'

interface Base {
  label: string
  hint: string
  /** Under the fence's `config:`, or the option's name for a direction. */
  path: string[]
  /** Set in the diagram text, in this syntax, instead of the frontmatter. */
  direction?: DirectionSyntax
}

export type LayoutOption =
  | (Base & { kind: 'choice'; choices: string[]; labels?: Record<string, string>; default: string })
  | (Base & { kind: 'int' | 'float'; min: number; max: number; default: number })
  | (Base & { kind: 'bool'; default: boolean })

const curve = (path: string[]): LayoutOption => ({
  kind: 'choice',
  label: 'Line style',
  hint: 'How edges are drawn between nodes.',
  path,
  choices: ['basis', 'linear', 'rounded', 'stepBefore', 'stepAfter'],
  default: 'basis',
})

const number =
  (kind: 'int' | 'float') =>
  (label: string, hint: string, path: string[], d: number, min: number, max: number): LayoutOption => ({
    kind,
    label,
    hint,
    path,
    default: d,
    min,
    max,
  })

const int = number('int')
const float = number('float')

const bool = (label: string, hint: string, path: string[], d: boolean): LayoutOption => ({
  kind: 'bool',
  label,
  hint,
  path,
  default: d,
})

const DIRECTION_LABELS: Record<string, string> = {
  TB: 'Top to bottom',
  TD: 'Top to bottom',
  BT: 'Bottom to top',
  LR: 'Left to right',
  RL: 'Right to left',
}

const direction = (syntax: DirectionSyntax): LayoutOption => ({
  kind: 'choice',
  label: 'Direction',
  hint: 'Which way the diagram flows.',
  path: ['direction'],
  direction: syntax,
  choices: DIRECTIONS[syntax],
  labels: DIRECTION_LABELS,
  default: DEFAULT_DIRECTION[syntax],
})

const NODE_SPACING = 'Space between nodes on the same level.'
const RANK_SPACING = 'Space between levels.'

// Keyed by render()'s diagramType. Each path is the one mermaid 11.17's renderer
// for that type actually reads, which isn't always the type's own config block:
// state and class edges follow `flowchart.curve`, and the class renderer reads
// the top-level spacing (its `class.*Spacing` keys are ignored).
const OPTIONS: Record<string, LayoutOption[]> = {
  'flowchart-v2': [
    direction('flowchart'),
    curve(['flowchart', 'curve']),
    int('Node spacing', NODE_SPACING, ['flowchart', 'nodeSpacing'], 50, 10, 300),
    int('Rank spacing', RANK_SPACING, ['flowchart', 'rankSpacing'], 50, 10, 300),
    int('Wrapping width', 'Label width at which text wraps.', ['flowchart', 'wrappingWidth'], 200, 50, 1000),
    int('Node padding', 'Space between a label and its shape.', ['flowchart', 'padding'], 15, 0, 100),
  ],
  stateDiagram: [
    direction('statement'),
    curve(['flowchart', 'curve']),
    int('Node spacing', NODE_SPACING, ['state', 'nodeSpacing'], 50, 10, 300),
    int('Rank spacing', RANK_SPACING, ['state', 'rankSpacing'], 50, 10, 300),
    int('Node padding', 'Space between a label and its shape.', ['flowchart', 'padding'], 15, 0, 100),
  ],
  classDiagram: [
    direction('statement'),
    curve(['flowchart', 'curve']),
    int('Node spacing', NODE_SPACING, ['nodeSpacing'], 50, 10, 300),
    int('Rank spacing', RANK_SPACING, ['rankSpacing'], 50, 10, 300),
    bool('Hide empty members', 'Leave out the member box of a class without members.', ['class', 'hideEmptyMembersBox'], false),
  ],
  sequence: [
    int('Actor margin', 'Space between actors.', ['sequence', 'actorMargin'], 50, 0, 500),
    int('Actor width', 'Width of the actor boxes.', ['sequence', 'width'], 150, 50, 500),
    int('Note margin', 'Space around notes.', ['sequence', 'noteMargin'], 10, 0, 100),
    int('Box margin', 'Space around loop, alt and opt boxes.', ['sequence', 'boxMargin'], 10, 0, 100),
    bool('Mirror actors', 'Repeat the actors below the diagram.', ['sequence', 'mirrorActors'], true),
    bool('Right-angle self messages', 'Draw messages to the same actor with right angles.', ['sequence', 'rightAngles'], false),
    bool('Wrap messages', 'Wrap long message and note text.', ['sequence', 'wrap'], false),
  ],
  mindmap: [
    int('Node padding', 'Space between a label and its shape.', ['mindmap', 'padding'], 10, 0, 100),
    int('Max node width', 'Label width at which text wraps.', ['mindmap', 'maxNodeWidth'], 200, 50, 1000),
  ],
  block: [int('Block gap', 'Space between blocks.', ['block', 'padding'], 8, 0, 100)],
  architecture: [
    int('Node separation', 'Least space between services.', ['architecture', 'nodeSeparation'], 75, 10, 300),
    int('Padding', 'Space inside groups and around the diagram.', ['architecture', 'padding'], 40, 0, 200),
    int('Icon size', 'Size of a service; edges scale with it.', ['architecture', 'iconSize'], 80, 20, 200),
    int('Font size', 'Size of the labels.', ['architecture', 'fontSize'], 16, 8, 40),
    float(
      'Edge length',
      'Length of an edge inside a group, times the icon size.',
      ['architecture', 'idealEdgeLengthMultiplier'],
      1.5,
      0.5,
      5,
    ),
    float(
      'Edge elasticity',
      'How strongly an edge inside a group pulls its ends together.',
      ['architecture', 'edgeElasticity'],
      0.45,
      0.05,
      1,
    ),
  ],
  timeline: [
    direction('timeline'),
    int('Padding', 'Space around the diagram.', ['timeline', 'padding'], 50, 0, 200),
    int('Left margin', 'Space left of the first period.', ['timeline', 'leftMargin'], 150, 0, 500),
  ],
}

/** The layout options for a rendered diagram type; empty when it has none. */
export const layoutOptions = (diagramType: string): LayoutOption[] => OPTIONS[diagramType] ?? []

export const optionKey = (o: LayoutOption) => o.path.join('.')

export type NumberOption = Extract<LayoutOption, { kind: 'int' | 'float' }>

/** A typed number, or why it can't be applied. */
export function parseNumberOption(o: NumberOption, raw: string): { value: number } | { error: string } {
  const t = raw.trim()
  if (o.kind === 'int' && !/^-?\d+$/.test(t)) return { error: 'Whole number' }
  if (o.kind === 'float' && !/^-?\d+(\.\d+)?$/.test(t)) return { error: 'Number' }
  const n = Number(t)
  if (n < o.min || n > o.max) return { error: `${o.min}–${o.max}` }
  return { value: n }
}

/** The option's value as written in the fence: undefined when at its default, null when not editable. */
export const readOption = (code: string, o: LayoutOption): Scalar | undefined | null =>
  o.direction ? readDirection(code, o.direction) : readConfig(code, o.path)

/**
 * The fence with the option set, or restored to its default when undefined or
 * the default itself, so the fence doesn't spell out defaults. Null when not editable.
 */
export function writeOption(code: string, o: LayoutOption, value: Scalar | undefined): string | null {
  const v = value === o.default ? undefined : value
  return o.direction
    ? writeDirection(code, o.direction, v === undefined ? undefined : String(v))
    : writeConfig(code, o.path, v)
}
