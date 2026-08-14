import { useEffect, useRef } from 'react'
import type { SpectatorSnapshot, WorldLife } from '../api/dto'
import { buildTextureData } from '../render/texture'
import {
  buildDetailTextureData,
  createDetailBuffers,
  detailCapacity,
  fillDetailInstances,
  type DetailBuffers,
} from '../render/detail'
import type { OverlayMode } from '../render/palette'
import { GlobeScene, type CellLabel } from './GlobeScene'

export interface GlobeProps {
  readonly snapshot: SpectatorSnapshot | null
  readonly previousSnapshot: SpectatorSnapshot | null
  readonly overlay: OverlayMode
  readonly colorBlindMode: boolean
  readonly reducedMotion: boolean
  readonly autoRotate: boolean
  readonly focus: { readonly latitude: number; readonly longitude: number } | null
  readonly onSelectCell?: ((cellIndex: number) => void) | undefined
  readonly highlight?: ReadonlySet<number> | undefined
  /** Outlined on the planet, so the globe shows which place the panel is describing. */
  readonly selectedCell?: number | null | undefined
  /** Live text pinned over cells, shown only once the camera is close enough to read it. */
  readonly labels?: readonly CellLabel[] | undefined
  /**
   * Told when the camera crosses into, or back out of, the range where the planet draws its life.
   * Fired only on a change, so it costs one render per crossing and nothing per frame.
   */
  readonly onCloseUpChange?: ((closeUp: boolean) => void) | undefined
  /**
   * What is alive on every cell. Null while the camera is too far out for it to be visible, which is
   * also when it is not being fetched.
   */
  readonly life?: WorldLife | null | undefined
}

/** Thin React wrapper. All per-frame work happens inside GlobeScene, never in React state. */
export function Globe(props: GlobeProps): React.ReactElement {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const sceneRef = useRef<GlobeScene | null>(null)

  // Held in a ref so changing the handler never tears down and rebuilds the WebGL context.
  const selectRef = useRef(props.onSelectCell)
  selectRef.current = props.onSelectCell

  const closeUpRef = useRef(props.onCloseUpChange)
  closeUpRef.current = props.onCloseUpChange

  // What is standing on the planet, held here rather than rebuilt per poll. Dropped along with the
  // scene's own copy whenever the close-up lets go.
  const buffersRef = useRef<DetailBuffers | null>(null)

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
    scene.setPickHandler((cellIndex) => selectRef.current?.(cellIndex))
    scene.setCloseUpHandler((closeUp) => closeUpRef.current?.(closeUp))
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
      buildTextureData(
        snapshot,
        props.previousSnapshot,
        props.overlay,
        props.colorBlindMode,
        props.highlight,
      ),
      snapshot.gridWidth,
      snapshot.gridHeight,
    )
  }, [
    props.snapshot,
    props.previousSnapshot,
    props.overlay,
    props.colorBlindMode,
    props.highlight,
  ])

  useEffect(() => {
    const scene = sceneRef.current
    const life = props.life
    if (!scene) {
      return
    }

    if (!life) {
      scene.clearDetail()
      buffersRef.current = null
      return
    }

    scene.updateDetail(
      buildDetailTextureData(life, props.snapshot),
      life.gridWidth,
      life.gridHeight,
      life.seaLevel,
    )

    // Allocated once and refilled in place: a tick refreshes what is standing on the planet without
    // handing the garbage collector a megabyte of arrays every five seconds.
    const capacity = detailCapacity(life.gridWidth * life.gridHeight)
    let buffers = buffersRef.current
    if (!buffers || buffers.capacity !== capacity) {
      buffers = createDetailBuffers(capacity)
      buffersRef.current = buffers
    }

    fillDetailInstances(buffers, life, props.snapshot, props.colorBlindMode)
    scene.updateDetailInstances(buffers)
  }, [props.life, props.snapshot, props.colorBlindMode])

  useEffect(() => {
    // Declared after the state upload so the scene already knows the grid size it is bounded by.
    sceneRef.current?.setSelectedCell(props.selectedCell ?? null)
  }, [props.selectedCell, props.snapshot])

  useEffect(() => {
    sceneRef.current?.setLabels(props.labels ?? [])
  }, [props.labels])

  useEffect(() => {
    if (props.focus) {
      sceneRef.current?.lookAtLocation(props.focus.latitude, props.focus.longitude, true)
    }
  }, [props.focus])

  return (
    <div className="globe-shell">
      <div className="globe" ref={containerRef} aria-hidden="true" />
      <div className="globe-zoom">
        <button type="button" onClick={() => sceneRef.current?.setZoom(-0.4)}>
          <span aria-hidden="true">+</span>
          <span className="visually-hidden">Zoom in</span>
        </button>
        <button type="button" onClick={() => sceneRef.current?.setZoom(0.4)}>
          <span aria-hidden="true">−</span>
          <span className="visually-hidden">Zoom out</span>
        </button>
      </div>
    </div>
  )
}
