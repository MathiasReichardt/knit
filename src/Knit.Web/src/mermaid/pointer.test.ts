import { describe, expect, it } from 'vitest'
import { edgeEndpoints, formatPointer, labelText, resolveHit } from './pointer'

// Fragments mirror mermaid 11.17's rendered output, trimmed to the attributes
// the resolver reads.
function mount(inner: string, id = 'm1'): SVGSVGElement {
  document.body.innerHTML = `<svg id="${id}" xmlns="http://www.w3.org/2000/svg">${inner}</svg>`
  return document.querySelector('svg')!
}

const at = (svg: Element, sel: string) => svg.querySelector(sel)!
const resolvePick = (target: Element, svg: Element, type: string) => resolveHit(target, svg, type)?.pick

const htmlLabel = (cls: string, html: string) =>
  `<g class="label"><foreignObject><div xmlns="http://www.w3.org/1999/xhtml"><span class="${cls}"><p>${html}</p></span></div></foreignObject></g>`

const FLOWCHART = `
  <g class="clusters"><g class="cluster" id="m1-body"><rect/><g class="cluster-label">${htmlLabel('nodeLabel', 'Body stations')}</g></g></g>
  <g class="edgePaths">
    <path id="m1-L_my_node_S_0" data-et="edge" data-id="L_my_node_S_0"/>
    <path id="m1-L_S_body_0" data-et="edge" data-id="L_S_body_0"/>
  </g>
  <g class="edgeLabels">
    <g class="edgeLabel"><g class="label" data-id="L_my_node_S_0"><foreignObject><div xmlns="http://www.w3.org/1999/xhtml"><span class="edgeLabel"><p>cured</p></span></div></foreignObject></g></g>
  </g>
  <g class="nodes">
    <g class="node default" id="m1-flowchart-my_node-0"><rect class="basic"/>${htmlLabel('nodeLabel', 'Start<br>here')}</g>
    <g class="node default" id="m1-flowchart-S-1"><rect class="basic"/>${htmlLabel('nodeLabel', 'Skin')}</g>
  </g>`

describe('labelText', () => {
  it('joins <br> and tspan parts with a space', () => {
    const svg = mount(`${htmlLabel('nodeLabel', 'Start<br>here')}<text id="t"><tspan>two</tspan><tspan>lines</tspan></text>`)
    expect(labelText(at(svg, '.label'))).toBe('Start here')
    expect(labelText(at(svg, '#t'))).toBe('two lines')
  })
})

describe('resolvePick — exact tier', () => {
  it('maps a flowchart node, cluster and edge to their DSL ids', () => {
    const svg = mount(FLOWCHART)
    expect(resolvePick(at(svg, '#m1-flowchart-my_node-0 rect'), svg, 'flowchart-v2')).toEqual({
      kind: 'node',
      dslId: 'my_node',
      label: 'Start here',
    })
    expect(resolvePick(at(svg, '#m1-body rect'), svg, 'flowchart-v2')).toEqual({
      kind: 'cluster',
      dslId: 'body',
      label: 'Body stations',
    })
    expect(resolvePick(at(svg, '#m1-L_my_node_S_0'), svg, 'flowchart-v2')).toEqual({
      kind: 'edge',
      from: 'my_node',
      to: 'S',
      label: 'cured',
    })
  })

  it('maps a click on an edge label to its edge', () => {
    const svg = mount(FLOWCHART)
    expect(resolvePick(at(svg, '.edgeLabel p'), svg, 'flowchart-v2')).toMatchObject({
      kind: 'edge',
      from: 'my_node',
      to: 'S',
    })
  })

  it('resolves an edge into a subgraph', () => {
    const svg = mount(FLOWCHART)
    expect(resolvePick(at(svg, '#m1-L_S_body_0'), svg, 'flowchart-v2')).toEqual({
      kind: 'edge',
      from: 'S',
      to: 'body',
    })
  })

  it('splits the edge id schemes of class, ER, requirement and block', () => {
    const svg = mount(`
      <g class="node" id="m1-classId-Robot-0"></g><g class="node" id="m1-classId-Bobbin-1"></g>
      <g class="node" id="m1-entity-ROBOT-2"></g><g class="node" id="m1-entity-PART-3"></g>
      <g class="node" id="m1-test_entity"></g><g class="node" id="m1-test_req"></g>
      <g class="node" id="m1-a"></g><g class="node" id="m1-b"></g>`)
    expect(edgeEndpoints('id_Robot_Bobbin_1', svg, 'm1')).toEqual({ from: 'Robot', to: 'Bobbin' })
    expect(edgeEndpoints('id_entity-ROBOT-2_entity-PART-3_0', svg, 'm1')).toEqual({ from: 'ROBOT', to: 'PART' })
    expect(edgeEndpoints('test_entity-test_req-0', svg, 'm1')).toEqual({ from: 'test_entity', to: 'test_req' })
    expect(edgeEndpoints('m1-1-a-b', svg, 'm1')).toEqual({ from: 'a', to: 'b' })
    expect(edgeEndpoints('L_x_y_0', svg, 'm1')).toBeUndefined()
  })

  it('names state start / end pseudo-states [*] and takes a composite state id from data-id', () => {
    const svg = mount(`
      <g class="node default" id="m1-state-root_start-0"><circle/></g>
      <g class="statediagram-state statediagram-cluster" id="m1-state-Walking-4" data-id="Walking"><rect class="outer"/><g class="cluster-label"><text>Walking</text></g></g>`)
    expect(resolvePick(at(svg, 'circle'), svg, 'stateDiagram')).toEqual({ kind: 'node', dslId: '[*]' })
    expect(resolvePick(at(svg, 'rect'), svg, 'stateDiagram')).toEqual({
      kind: 'cluster',
      dslId: 'Walking',
      label: 'Walking',
    })
  })

  it('prefers a requirement name over its <<stereotype>> line', () => {
    const svg = mount(`<g class="node default" id="m1-test_req"><path/>
      <g class="label"><text>&lt;&lt;Requirement&gt;&gt;</text></g><g class="label"><text>test_req</text></g></g>`)
    expect(resolvePick(at(svg, 'path'), svg, 'requirement')).toEqual({ kind: 'node', dslId: 'test_req', label: 'test_req' })
  })

  const ARCHITECTURE = `
    <g class="architecture-edges">
      <g><path class="edge" id="m1-L_db_server_0"/><polygon class="arrow"/></g>
      <g><path class="edge" id="m1-L_server_hub_0"/></g>
    </g>
    <g class="architecture-services">
      <g id="m1-service-db" class="architecture-service"><g><rect class="background"/><text>Database</text></g></g>
      <g id="m1-service-server" class="architecture-service"><text>Server</text></g>
    </g>
    <g class="architecture-junction"><g><rect id="m1-node-hub" fill-opacity="0"/></g></g>
    <g class="architecture-groups">
      <rect id="m1-group-plant" class="node-bkg"/>
      <g><g><svg><rect class="icon"/></svg></g><g><g><text>Plant</text></g></g></g>
      <rect id="m1-group-line" class="node-bkg"/>
      <g><g><svg><rect/></svg></g><g><g><text>Line</text></g></g></g>
    </g>`

  it('maps architecture services, junctions and edges', () => {
    const svg = mount(ARCHITECTURE)
    const dbServer = { kind: 'edge', from: 'db', to: 'server' }
    expect(resolvePick(at(svg, '.background'), svg, 'architecture')).toEqual({
      kind: 'service',
      dslId: 'db',
      label: 'Database',
    })
    expect(resolvePick(at(svg, '#m1-node-hub'), svg, 'architecture')).toEqual({ kind: 'junction', dslId: 'hub' })
    expect(resolvePick(at(svg, '#m1-L_db_server_0'), svg, 'architecture')).toEqual(dbServer)
    expect(resolvePick(at(svg, 'polygon.arrow'), svg, 'architecture')).toEqual(dbServer)
    expect(resolvePick(at(svg, '#m1-L_server_hub_0'), svg, 'architecture')).toEqual({ kind: 'edge', from: 'server', to: 'hub' })
  })

  it('maps an architecture group from its border, icon and title', () => {
    const svg = mount(ARCHITECTURE)
    const line = { kind: 'group', dslId: 'line', label: 'Line' }
    expect(resolvePick(at(svg, '#m1-group-line'), svg, 'architecture')).toEqual(line)
    expect(resolvePick(svg.querySelectorAll('.architecture-groups text')[1]!, svg, 'architecture')).toEqual(line)
    const hit = resolveHit(at(svg, 'rect.icon'), svg, 'architecture')
    expect(hit?.pick).toEqual({ kind: 'group', dslId: 'plant', label: 'Plant' })
    expect(hit?.el).toBe(at(svg, '#m1-group-plant'))
  })

  it('maps block nodes, composites, block arrows and edges', () => {
    const svg = mount(`
      <g class="block">
        <g class="node default flowchart-label" id="m1-svc"><rect class="basic cluster composite label-container"/>${htmlLabel('nodeLabel', '')}</g>
        <g class="node default flowchart-label" id="m1-orders"><rect class="basic label-container"/>${htmlLabel('nodeLabel', 'Orders')}</g>
        <g class="node default flowchart-label" id="m1-billing"><rect class="basic label-container"/>${htmlLabel('nodeLabel', 'Billing')}</g>
        <g class="node default flowchart-label" id="m1-down"><polygon class="label-container"/>${htmlLabel('nodeLabel', '&nbsp;&nbsp;')}</g>
        <path id="m1-m1-1-orders-billing" data-et="edge" data-id="m1-1-orders-billing"/>
        <g class="edgeLabel"><g class="label" data-id="1-orders-billing"><text>invoice</text></g></g>
      </g>`)
    expect(resolvePick(at(svg, '#m1-svc rect'), svg, 'block')).toEqual({ kind: 'cluster', dslId: 'svc' })
    expect(resolvePick(at(svg, '#m1-orders rect'), svg, 'block')).toEqual({ kind: 'node', dslId: 'orders', label: 'Orders' })
    expect(resolvePick(at(svg, '#m1-down polygon'), svg, 'block')).toEqual({ kind: 'node', dslId: 'down' })
    expect(resolvePick(at(svg, '.edgeLabel text'), svg, 'block')).toEqual({
      kind: 'edge',
      from: 'orders',
      to: 'billing',
      label: 'invoice',
    })
  })

  it('maps gantt bars and their text, dropping generated task ids', () => {
    const svg = mount(`
      <rect id="m1-a1" class="task task0"/><rect id="m1-task1" class="task task0"/>
      <text id="m1-a1-text" class="taskText">Frame</text><text id="m1-task1-text" class="taskText">Skin</text>`)
    expect(resolvePick(at(svg, '#m1-a1-text'), svg, 'gantt')).toEqual({ kind: 'task', dslId: 'a1', label: 'Frame' })
    expect(resolvePick(at(svg, '#m1-task1'), svg, 'gantt')).toEqual({ kind: 'task', label: 'Skin' })
  })

  it('maps venn areas by their set names', () => {
    const svg = mount(`<g class="venn-area venn-intersection" data-venn-sets="A_B"><path/><text class="label">Bobbin</text></g>`)
    expect(resolvePick(at(svg, 'path'), svg, 'venn')).toEqual({ kind: 'set', dslId: 'A_B', label: 'Bobbin' })
  })

  it('keeps the label but no id where element ids are internal counters', () => {
    const svg = mount(`<g class="node mindmap-node" id="m1-node_1"><path class="node-bkg"/><g class="label"><text>Body</text></g></g>`)
    expect(resolvePick(at(svg, 'path'), svg, 'mindmap')).toEqual({ kind: 'node', label: 'Body' })
  })
})

describe('resolvePick — sequence', () => {
  const SEQUENCE = `
    <g><rect class="actor actor-bottom"/><text class="actor actor-box">Bobbin</text></g>
    <g><line class="actor-line" data-et="life-line" data-id="B"/>
      <g data-et="participant" data-type="participant" data-id="B"><rect class="actor actor-top"/><text class="actor actor-box">Bobbin</text></g></g>
    <g data-et="note" data-id="i3"><rect class="note"/><text class="noteText">never fired</text></g>
    <g data-et="control-structure" data-id="i6"><line class="loopLine"/><text class="labelText">loop</text><text class="loopText">[retry]</text></g>
    <text class="messageText">pat</text>
    <line class="messageLine0" data-et="message" data-id="i0" data-from="V" data-to="B"/>`

  it('maps participants from the top box, the life-line and the bottom box', () => {
    const svg = mount(SEQUENCE)
    const bobbin = { kind: 'participant', dslId: 'B', label: 'Bobbin' }
    expect(resolvePick(at(svg, '.actor-top'), svg, 'sequence')).toEqual(bobbin)
    expect(resolvePick(at(svg, '.actor-line'), svg, 'sequence')).toEqual(bobbin)
    expect(resolvePick(at(svg, '.actor-bottom'), svg, 'sequence')).toEqual(bobbin)
    expect(resolvePick(at(svg, '.actor-box'), svg, 'sequence')).toEqual(bobbin)
  })

  it('maps a message line and its preceding text', () => {
    const svg = mount(SEQUENCE)
    const pat = { kind: 'message', from: 'V', to: 'B', label: 'pat' }
    expect(resolvePick(at(svg, '[data-et=message]'), svg, 'sequence')).toEqual(pat)
    expect(resolvePick(at(svg, '.messageText'), svg, 'sequence')).toEqual(pat)
  })

  it('maps notes and control blocks', () => {
    const svg = mount(SEQUENCE)
    expect(resolvePick(at(svg, '.note'), svg, 'sequence')).toEqual({ kind: 'note', label: 'never fired' })
    expect(resolvePick(at(svg, '.loopLine'), svg, 'sequence')).toEqual({ kind: 'loop', label: '[retry]' })
  })
})

describe('resolvePick — label tier', () => {
  it('takes the clicked text and its class as the kind', () => {
    const svg = mount(`<g class="commit-labels"><g><rect class="commit-label-bkg"/><text class="commit-label">init</text></g></g>`)
    expect(resolvePick(at(svg, 'text'), svg, 'gitGraph')).toEqual({ kind: 'commit-label', label: 'init' })
    expect(resolvePick(at(svg, 'rect'), svg, 'gitGraph')).toEqual({ kind: 'commit-label', label: 'init' })
  })

  it('names a pie slice from the legend entry at the same index', () => {
    const svg = mount(`<g><path class="pieCircle"/><path class="pieCircle"/></g>
      <g class="legend"><text>Arms</text></g><g class="legend"><text>Legs</text></g>`)
    expect(resolvePick(svg.querySelectorAll('path')[1]!, svg, 'pie')).toEqual({ kind: 'slice', label: 'Legs' })
  })

  it('returns undefined on the background', () => {
    const svg = mount(`<rect class="background"/><g><text>a</text><text>b</text><line/></g>`)
    expect(resolvePick(at(svg, 'rect'), svg, 'xychart')).toBeUndefined()
    expect(resolvePick(at(svg, 'line'), svg, 'xychart')).toBeUndefined()
  })
})

describe('resolveHit', () => {
  it('returns the element that drew the pick, for hover and flash', () => {
    const svg = mount(FLOWCHART)
    expect(resolveHit(at(svg, '#m1-flowchart-S-1 p'), svg, 'flowchart-v2')?.el).toBe(at(svg, '#m1-flowchart-S-1'))
    expect(resolveHit(at(svg, '#m1-L_S_body_0'), svg, 'flowchart-v2')?.el).toBe(at(svg, '#m1-L_S_body_0'))
    const git = mount(`<g><rect/><text class="commit-label">init</text></g>`)
    expect(resolveHit(at(git, 'rect'), git, 'gitGraph')?.el).toBe(at(git, 'text'))
  })
})

describe('formatPointer', () => {
  it('formats element, edge and block pointers', () => {
    expect(formatPointer('design:auth', 2, 'flowchart-v2', { kind: 'node', dslId: 'db', label: 'Database' })).toBe(
      'design:auth#mermaid-2/node/db["Database"]',
    )
    expect(formatPointer('design:auth', 2, 'flowchart-v2', { kind: 'edge', from: 'authSvc', to: 'db', label: 'reads' })).toBe(
      'design:auth#mermaid-2/edge/authSvc->db["reads"]',
    )
    expect(formatPointer('item:abc', 1, 'classDiagram', { kind: 'node', dslId: 'Robot', label: 'Robot' })).toBe(
      'item:abc#mermaid-1/node/Robot',
    )
    expect(formatPointer('design:auth', 2, 'flowchart-v2')).toBe('design:auth#mermaid-2(flowchart)')
  })

  it('quotes ids outside mermaid id syntax and escapes labels', () => {
    expect(formatPointer('item:abc', 1, 'stateDiagram', { kind: 'node', dslId: '[*]' })).toBe('item:abc#mermaid-1/node/"[*]"')
    expect(formatPointer('item:abc', 1, 'pie', { kind: 'slice', label: 'say "hi" \\o/' })).toBe(
      'item:abc#mermaid-1/slice["say \\"hi\\" \\\\o/"]',
    )
  })
})
