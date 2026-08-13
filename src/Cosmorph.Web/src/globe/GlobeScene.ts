import * as THREE from 'three'
import { cellToPoint, rotationForLongitude, tiltForLatitude, uvToCell } from '../render/projection'

/**
 * Owns every Three.js resource for the planet. It is deliberately outside React so the animation
 * loop never triggers a re-render, and every GPU resource is disposed explicitly.
 */
export interface GlobeOptions {
  readonly reducedMotion: boolean
}

/** A few lines of live text pinned over a cell. Anchored by cell, not by screen position. */
export interface CellLabel {
  readonly cellIndex: number
  readonly lines: readonly string[]
}

const VERTEX_SHADER = `
varying vec2 vUv;
varying vec3 vNormal;
void main() {
  vUv = uv;
  vNormal = normalize(normalMatrix * normal);
  gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
}
`

// Hatching is derived from the alpha channel, giving stress a redundant, non-colour encoding.
const FRAGMENT_SHADER = `
precision mediump float;
uniform sampler2D uState;
uniform vec2 uGrid;
uniform float uTime;
uniform float uCellSnap;
uniform vec2 uSelected;
varying vec2 vUv;
varying vec3 vNormal;

float hatch(vec2 uv, float pattern) {
  vec2 p = uv * uGrid * 3.0;
  if (pattern < 0.5) { return 0.0; }
  if (pattern < 1.5) { return step(0.5, fract(p.x + p.y)); }
  if (pattern < 2.5) { return step(0.75, fract(p.x * 1.7)) ; }
  if (pattern < 3.5) { return step(0.75, fract(p.x - p.y)); }
  return step(0.8, fract(p.y * 1.3));
}

void main() {
  // Row 0 of the uploaded data is the north pole, which lands at v = 0; the sphere puts the north
  // pole at uv.y = 1. Flipping here rather than on the texture is deliberate: WebGL ignores
  // UNPACK_FLIP_Y_WEBGL for ArrayBufferView uploads, so texture.flipY would have no effect.
  vec2 grid = vec2(vUv.x, 1.0 - vUv.y);

  // Up close the sample is pulled to the centre of its cell, so the linear filter stops smearing
  // neighbours together and each place becomes a tile you can actually read. Far away uCellSnap is
  // zero and the planet keeps the soft blend it has always had.
  vec2 cellId = floor(grid * uGrid);
  vec2 sampleUv = mix(grid, (cellId + 0.5) / uGrid, uCellSnap);
  vec4 state = texture2D(uState, sampleUv);
  float pattern = floor(state.a * 255.0 / 60.0 + 0.5);
  vec3 color = state.rgb;

  float shade = 0.55 + 0.45 * clamp(dot(vNormal, normalize(vec3(0.6, 0.5, 0.8))), 0.0, 1.0);
  color *= shade;

  float marks = hatch(grid, pattern);
  color = mix(color, vec3(0.08, 0.06, 0.05), marks * 0.45);

  // Chunky cartoon clouds drift slowly and never hide the underlying state.
  float clouds = smoothstep(0.72, 0.95, sin(vUv.x * 22.0 + uTime * 0.05) * cos(vUv.y * 14.0 - uTime * 0.03));
  color = mix(color, vec3(1.0), clouds * 0.18);

  // Distance to the nearest cell border, in cell widths: zero on the border, a half at the centre.
  vec2 within = fract(grid * uGrid);
  float border = min(min(within.x, 1.0 - within.x), min(within.y, 1.0 - within.y));

  float edge = 1.0 - smoothstep(0.008, 0.030, border);
  color = mix(color, color * 0.55, edge * uCellSnap * 0.85);

  // The selected cell is outlined so the planet says which place the panel is describing. Columns
  // wrap, so the seam is measured the short way round.
  if (uSelected.x >= 0.0) {
    float dx = abs(cellId.x - uSelected.x);
    dx = min(dx, uGrid.x - dx);
    if (dx < 0.5 && abs(cellId.y - uSelected.y) < 0.5) {
      float ring = 1.0 - smoothstep(0.020, 0.075, border);
      color = mix(color, vec3(1.0, 0.94, 0.62), ring * 0.9);
    }
  }

  float rim = pow(1.0 - abs(vNormal.z), 3.0);
  color += vec3(0.30, 0.45, 0.65) * rim * 0.35;

  gl_FragColor = vec4(color, 1.0);
}
`

export class GlobeScene {
  /** A pointer that moved further than this, or was held longer, was a drag rather than a pick. */
  static readonly PICK_MOVE_LIMIT_PX = 5
  static readonly PICK_HOLD_LIMIT_MS = 400
  static readonly MIN_DISTANCE = 1.35
  static readonly MAX_DISTANCE = 6
  /** How close the camera moves when a place is selected. */
  static readonly FOCUS_DISTANCE = 1.8
  /** Cells sharpen into tiles between these distances, and labels appear at the closer one. */
  static readonly DETAIL_FAR = 2.6
  static readonly DETAIL_NEAR = GlobeScene.FOCUS_DISTANCE
  static readonly LABEL_DISTANCE = 2.4

  private readonly renderer: THREE.WebGLRenderer
  private readonly scene: THREE.Scene
  private readonly camera: THREE.PerspectiveCamera
  private readonly geometry: THREE.SphereGeometry
  private readonly material: THREE.ShaderMaterial
  private readonly mesh: THREE.Mesh
  private texture: THREE.DataTexture | null = null
  private frame = 0
  private disposed = false
  private targetRotation = 0
  private rotation = 0
  private targetTilt = 0
  private targetDistance = 3.2
  private autoRotate = true
  private reducedMotion: boolean
  private pointerDown = false
  private pointerX = 0
  private pointerStartX = 0
  private pointerStartY = 0
  private pointerDownAt = 0
  private gridWidth = 64
  private gridHeight = 32
  private pickHandler: ((cellIndex: number) => void) | null = null
  private readonly raycaster = new THREE.Raycaster()
  private readonly container: HTMLElement
  private readonly clock = new THREE.Clock()
  private readonly labelLayer: HTMLDivElement
  private labels: readonly CellLabel[] = []
  private labelNodes: HTMLDivElement[] = []
  // Scratch vectors, reused every frame so the label layer allocates nothing per animation frame.
  private readonly scratchPoint = new THREE.Vector3()
  private readonly scratchView = new THREE.Vector3()

  constructor(container: HTMLElement, options: GlobeOptions) {
    this.container = container
    this.reducedMotion = options.reducedMotion

    this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false })
    this.renderer.setPixelRatio(Math.min(2, globalThis.devicePixelRatio || 1))
    this.renderer.setSize(container.clientWidth, container.clientHeight, false)
    container.appendChild(this.renderer.domElement)

    this.scene = new THREE.Scene()
    this.scene.background = new THREE.Color(0x05070f)

    this.camera = new THREE.PerspectiveCamera(
      42,
      Math.max(1, container.clientWidth) / Math.max(1, container.clientHeight),
      0.1,
      100,
    )
    this.camera.position.set(0, 0, 3.2)

    this.geometry = new THREE.SphereGeometry(1, 96, 64)
    this.material = new THREE.ShaderMaterial({
      vertexShader: VERTEX_SHADER,
      fragmentShader: FRAGMENT_SHADER,
      uniforms: {
        uState: { value: null },
        uGrid: { value: new THREE.Vector2(64, 32) },
        uTime: { value: 0 },
        uCellSnap: { value: 0 },
        uSelected: { value: new THREE.Vector2(-1, -1) },
      },
    })

    this.mesh = new THREE.Mesh(this.geometry, this.material)
    this.scene.add(this.mesh)

    // Labels live in the DOM rather than the scene: they stay crisp at any pixel ratio and cost no
    // draw calls. The layer is aria-hidden because the Place panel is the accessible route to this.
    this.labelLayer = document.createElement('div')
    this.labelLayer.className = 'globe-labels'
    this.labelLayer.setAttribute('aria-hidden', 'true')
    container.appendChild(this.labelLayer)

    this.renderer.domElement.addEventListener('pointerdown', this.onPointerDown)
    this.renderer.domElement.addEventListener('pointermove', this.onPointerMove)
    globalThis.addEventListener('pointerup', this.onPointerUp)
    this.renderer.domElement.addEventListener('wheel', this.onWheel, { passive: true })

    this.loop()
  }

  setReducedMotion(reducedMotion: boolean): void {
    this.reducedMotion = reducedMotion
  }

  setAutoRotate(autoRotate: boolean): void {
    this.autoRotate = autoRotate
  }

  /** Registers the handler called when a place is clicked. Pass null to stop picking. */
  setPickHandler(handler: ((cellIndex: number) => void) | null): void {
    this.pickHandler = handler
  }

  /** Outlines a cell on the planet, so the globe shows which place the panel is describing. */
  setSelectedCell(cellIndex: number | null): void {
    const selected = this.material.uniforms.uSelected!.value as THREE.Vector2
    if (cellIndex === null || cellIndex < 0 || cellIndex >= this.gridWidth * this.gridHeight) {
      selected.set(-1, -1)
      return
    }
    selected.set(cellIndex % this.gridWidth, Math.floor(cellIndex / this.gridWidth))
  }

  /**
   * Replaces the text pinned over the planet. Nodes are recreated only when the set of cells
   * changes; otherwise the existing nodes are rewritten, so a tick refreshes the words in place.
   */
  setLabels(labels: readonly CellLabel[]): void {
    if (this.disposed) {
      return
    }

    if (labels.length !== this.labelNodes.length) {
      this.labelLayer.replaceChildren()
      this.labelNodes = labels.map(() => {
        const node = document.createElement('div')
        node.className = 'globe-label'
        this.labelLayer.appendChild(node)
        return node
      })
    }

    this.labels = labels
    labels.forEach((label, index) => {
      const node = this.labelNodes[index]
      if (!node) {
        return
      }
      // textContent throughout: nothing the API sends is ever parsed as markup.
      node.replaceChildren(
        ...label.lines.map((line) => {
          const span = document.createElement('span')
          span.textContent = line
          return span
        }),
      )
    })
  }

  /** Moves the camera in or out by a step, clamped to the readable range. */
  setZoom(delta: number): void {
    this.targetDistance = Math.min(
      GlobeScene.MAX_DISTANCE,
      Math.max(GlobeScene.MIN_DISTANCE, this.targetDistance + delta),
    )
  }

  resize(): void {
    const width = Math.max(1, this.container.clientWidth)
    const height = Math.max(1, this.container.clientHeight)
    this.renderer.setSize(width, height, false)
    this.camera.aspect = width / height
    this.camera.updateProjectionMatrix()
  }

  /** Replaces the state texture. The previous texture is disposed before the new one is attached. */
  updateState(data: Uint8Array, width: number, height: number): void {
    if (this.disposed) {
      return
    }

    if (!this.texture || this.texture.image.width !== width || this.texture.image.height !== height) {
      this.texture?.dispose()
      this.texture = new THREE.DataTexture(data, width, height, THREE.RGBAFormat)
      this.texture.wrapS = THREE.RepeatWrapping
      this.texture.minFilter = THREE.LinearFilter
      this.texture.magFilter = THREE.LinearFilter
      this.material.uniforms.uState!.value = this.texture
      this.material.uniforms.uGrid!.value = new THREE.Vector2(width, height)
    } else {
      this.texture.image.data = data
    }

    this.gridWidth = width
    this.gridHeight = height
    this.texture.needsUpdate = true
  }

  /**
   * Rotates the planet toward a location. Passing `zoom` also moves the camera in, eased in the
   * render loop rather than snapped, so selecting a place does not jolt the view.
   */
  lookAtLocation(latitude: number, longitude: number, zoom = false): void {
    this.targetRotation = rotationForLongitude(longitude)
    this.targetTilt = tiltForLatitude(latitude)
    if (zoom) {
      this.targetDistance = Math.min(this.targetDistance, GlobeScene.FOCUS_DISTANCE)
    }

    if (this.reducedMotion) {
      this.rotation = this.targetRotation
      this.mesh.rotation.y = this.rotation
      this.mesh.rotation.x = this.targetTilt
      this.camera.position.z = this.targetDistance
    }
  }

  dispose(): void {
    this.disposed = true
    cancelAnimationFrame(this.frame)
    this.renderer.domElement.removeEventListener('pointerdown', this.onPointerDown)
    this.renderer.domElement.removeEventListener('pointermove', this.onPointerMove)
    this.renderer.domElement.removeEventListener('wheel', this.onWheel)
    globalThis.removeEventListener('pointerup', this.onPointerUp)
    this.texture?.dispose()
    this.geometry.dispose()
    this.material.dispose()
    this.renderer.dispose()
    this.renderer.domElement.remove()
    this.labelLayer.remove()
    this.labelNodes = []
  }

  private readonly onPointerDown = (event: PointerEvent): void => {
    this.pointerDown = true
    this.pointerX = event.clientX
    this.pointerStartX = event.clientX
    this.pointerStartY = event.clientY
    this.pointerDownAt = performance.now()
  }

  private readonly onPointerMove = (event: PointerEvent): void => {
    if (!this.pointerDown) {
      return
    }
    const delta = event.clientX - this.pointerX
    this.pointerX = event.clientX
    this.rotation += delta * 0.005
    this.targetRotation = this.rotation
  }

  private readonly onPointerUp = (event: PointerEvent): void => {
    const wasDown = this.pointerDown
    this.pointerDown = false
    if (!wasDown || !this.pickHandler) {
      return
    }

    // Dragging the planet around must never select a place, so only a short, still press counts.
    const moved = Math.hypot(event.clientX - this.pointerStartX, event.clientY - this.pointerStartY)
    const held = performance.now() - this.pointerDownAt
    if (moved >= GlobeScene.PICK_MOVE_LIMIT_PX || held >= GlobeScene.PICK_HOLD_LIMIT_MS) {
      return
    }

    const picked = this.pick(event.clientX, event.clientY)
    if (picked !== null) {
      this.pickHandler(picked)
    }
  }

  /** The cell under a client point, or null if the pointer missed the planet. */
  private pick(clientX: number, clientY: number): number | null {
    const bounds = this.renderer.domElement.getBoundingClientRect()
    if (bounds.width <= 0 || bounds.height <= 0) {
      return null
    }

    const ndc = new THREE.Vector2(
      ((clientX - bounds.left) / bounds.width) * 2 - 1,
      -((clientY - bounds.top) / bounds.height) * 2 + 1,
    )

    this.raycaster.setFromCamera(ndc, this.camera)
    const hit = this.raycaster.intersectObject(this.mesh, false)[0]
    if (!hit?.uv) {
      return null
    }

    return uvToCell(hit.uv.x, hit.uv.y, this.gridWidth, this.gridHeight)
  }

  private readonly onWheel = (event: WheelEvent): void => {
    this.setZoom(Math.sign(event.deltaY) * 0.15)
  }

  private readonly loop = (): void => {
    if (this.disposed) {
      return
    }

    const delta = this.clock.getDelta()
    if (!this.reducedMotion) {
      this.material.uniforms.uTime!.value += delta
      if (this.autoRotate && !this.pointerDown) {
        this.targetRotation += delta * 0.05
      }
      const ease = Math.min(1, delta * 3)
      this.rotation += (this.targetRotation - this.rotation) * ease
      this.mesh.rotation.x += (this.targetTilt - this.mesh.rotation.x) * ease
      this.camera.position.z += (this.targetDistance - this.camera.position.z) * ease
    } else {
      this.rotation = this.targetRotation
      this.mesh.rotation.x = this.targetTilt
      this.camera.position.z = this.targetDistance
    }

    this.mesh.rotation.y = this.rotation

    // Sharpen the cells as the camera closes in, and place the labels against the same matrices the
    // renderer is about to use, so text can never lag a frame behind the planet under it.
    const distance = this.camera.position.z
    this.material.uniforms.uCellSnap!.value = THREE.MathUtils.clamp(
      (GlobeScene.DETAIL_FAR - distance) / (GlobeScene.DETAIL_FAR - GlobeScene.DETAIL_NEAR),
      0,
      1,
    )
    this.mesh.updateMatrixWorld()
    this.camera.updateMatrixWorld()
    this.positionLabels(distance)

    this.renderer.render(this.scene, this.camera)
    this.frame = requestAnimationFrame(this.loop)
  }

  /**
   * Moves each label over its cell. A label hides when its cell has turned away from the camera,
   * when it falls outside the viewport, or when the camera is too far out for the text to mean
   * anything — so labels arrive as you zoom in and never crowd the whole planet.
   */
  private positionLabels(distance: number): void {
    if (this.labelNodes.length === 0) {
      return
    }

    const width = this.container.clientWidth
    const height = this.container.clientHeight
    const tooFar = distance > GlobeScene.LABEL_DISTANCE || width <= 0 || height <= 0

    for (let i = 0; i < this.labelNodes.length; i++) {
      const node = this.labelNodes[i]
      const label = this.labels[i]
      if (!node || !label) {
        continue
      }
      if (tooFar) {
        node.style.display = 'none'
        continue
      }

      const point = cellToPoint(label.cellIndex, this.gridWidth, this.gridHeight)
      this.scratchPoint.set(point.x, point.y, point.z).applyMatrix4(this.mesh.matrixWorld)

      // The mesh is a unit sphere at the origin, so the rotated point is also its own normal.
      this.scratchView.copy(this.camera.position).sub(this.scratchPoint).normalize()
      if (this.scratchPoint.dot(this.scratchView) <= 0.15) {
        node.style.display = 'none'
        continue
      }

      this.scratchPoint.project(this.camera)
      if (Math.abs(this.scratchPoint.x) > 1 || Math.abs(this.scratchPoint.y) > 1) {
        node.style.display = 'none'
        continue
      }

      const x = (this.scratchPoint.x * 0.5 + 0.5) * width
      const y = (-this.scratchPoint.y * 0.5 + 0.5) * height
      node.style.display = 'block'
      node.style.transform = `translate(-50%, -50%) translate(${x.toFixed(1)}px, ${y.toFixed(1)}px)`
    }
  }
}
