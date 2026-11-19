import type { MermaidTheme, MermaidThemeVariables } from '../mermaid/MermaidBlock'

// Tailwind v4's zinc / blue scale as hex: mermaid derives shades with khroma,
// which can't parse the oklch() values Tailwind's own tokens use.
const PALETTE: Record<MermaidTheme, MermaidThemeVariables> = {
  light: {
    darkMode: false,
    background: '#ffffff',
    primaryColor: '#f4f4f5',
    primaryBorderColor: '#a1a1aa',
    primaryTextColor: '#18181b',
    secondaryColor: '#eff6ff',
    secondaryBorderColor: '#93c5fd',
    tertiaryColor: '#fafafa',
    tertiaryBorderColor: '#d4d4d8',
    textColor: '#18181b',
    lineColor: '#71717a',
    edgeLabelBackground: '#ffffff',
    clusterBkg: '#fafafa',
    clusterBorder: '#d4d4d8',
    noteBkgColor: '#eff6ff',
    noteBorderColor: '#93c5fd',
    noteTextColor: '#18181b',
    // Mermaid's default is a dark blue; a linked task is marked by its badge.
    taskTextClickableColor: '#18181b',
    // Architecture has its own: the base theme draws groups black and edges #777.
    archEdgeColor: '#71717a',
    archEdgeArrowColor: '#71717a',
    archGroupBorderColor: '#a1a1aa',
  },
  dark: {
    darkMode: true,
    background: '#09090b',
    primaryColor: '#27272a',
    primaryBorderColor: '#52525b',
    primaryTextColor: '#f4f4f5',
    secondaryColor: '#172554',
    secondaryBorderColor: '#1e40af',
    tertiaryColor: '#18181b',
    tertiaryBorderColor: '#3f3f46',
    textColor: '#f4f4f5',
    lineColor: '#a1a1aa',
    edgeLabelBackground: '#09090b',
    clusterBkg: '#18181b',
    clusterBorder: '#3f3f46',
    noteBkgColor: '#172554',
    noteBorderColor: '#1e40af',
    noteTextColor: '#f4f4f5',
    // Mermaid's default is a dark blue; a linked task is marked by its badge.
    taskTextClickableColor: '#f4f4f5',
    // Architecture has its own: the base theme draws groups black and edges #777.
    archEdgeColor: '#a1a1aa',
    archEdgeArrowColor: '#a1a1aa',
    archGroupBorderColor: '#71717a',
  },
}

/** The SPA's palette plus its body font, for mermaid's `base` theme. */
export function mermaidThemeVariables(theme: MermaidTheme, fontFamily: string): MermaidThemeVariables {
  // Normal edges (mermaid's default is 1px, faint next to the nodes); `==>` stays 3.5px.
  return { ...PALETTE[theme], strokeWidth: '1.5', fontFamily }
}
