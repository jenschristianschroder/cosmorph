import { useEffect, useRef } from 'react'
import type { SpectatorSnapshot } from '../api/dto'
import { buildTextureData } from '../render/texture'
import type { OverlayMode } from '../render/palette'
import { GlobeScene } from './GlobeScene'

export interface GlobeProps {
  readonly snapshot: SpectatorSnapshot | null
  readonly previousSnapshot: SpectatorSnapshot | null
  readonly overlay: OverlayMode
  readonly colorBlindMode: boolean
  readonly reducedMotion: boolean
  readonly autoRotate: boolean
  readonly focus: { readonly latitude: number; readonly longitude: number } | null
}

/** Thin React wrapper. All per-frame work happens inside GlobeScene, never in React state. */
export function Globe(props: GlobeProps): React.ReactElement {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const sceneRef = useRef<GlobeScene | null>(null)

  useEffect(() => {
    const container = containerRef.current
    if (!container) {
      return
    }

    let scene: GlobeScene | null = null
    try {
      scene = new GlobeScene(container, { reducedMotion: props.reducedMotion })
    } catch {
      // WebGL may be unavailable. The accessible summary remains the source of truth.
      return
    }

    sceneRef.current = scene
    const onResize = (): void => scene?.resize()
    globalThis.addEventListener('resize', onResize)

    return () => {
      globalThis.removeEventListener('resize', onResize)
      scene?.dispose()
      sceneRef.current = null
    }
    // The scene is created once; later prop changes are pushed through imperative calls below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    sceneRef.current?.setReducedMotion(props.reducedMotion)
  }, [props.reducedMotion])

  useEffect(() => {
    sceneRef.current?.setAutoRotate(props.autoRotate)
  }, [props.autoRotate])

  useEffect(() => {
    const scene = sceneRef.current
    const snapshot = props.snapshot
    if (!scene || !snapshot) {
      return
    }

    scene.updateState(
      buildTextureData(snapshot, props.previousSnapshot, props.overlay, props.colorBlindMode),
      snapshot.gridWidth,
      snapshot.gridHeight,
    )
  }, [props.snapshot, props.previousSnapshot, props.overlay, props.colorBlindMode])

  useEffect(() => {
    if (props.focus) {
      sceneRef.current?.lookAtLocation(props.focus.latitude, props.focus.longitude)
    }
  }, [props.focus])

  return <div className="globe" ref={containerRef} aria-hidden="true" />
}
