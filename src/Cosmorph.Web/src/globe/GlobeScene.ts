import * as THREE from 'three'
import { rotationForLongitude, tiltForLatitude, uvToCell } from '../render/projection'

/**
 * Owns every Three.js resource for the planet. It is deliberately outside React so the animation
 * loop never triggers a re-render, and every GPU resource is disposed explicitly.
 */
export interface GlobeOptions {
  readonly reducedMotion: boolean
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
  vec4 state = texture2D(uState, grid);
  float pattern = floor(state.a * 255.0 / 60.0 + 0.5);
  vec3 color = state.rgb;

  float shade = 0.55 + 0.45 * clamp(dot(vNormal, normalize(vec3(0.6, 0.5, 0.8))), 0.0, 1.0);
  color *= shade;

  float marks = hatch(grid, pattern);
  color = mix(color, vec3(0.08, 0.06, 0.05), marks * 0.45);

  // Chunky cartoon clouds drift slowly and never hide the underlying state.
  float clouds = smoothstep(0.72, 0.95, sin(vUv.x * 22.0 + uTime * 0.05) * cos(vUv.y * 14.0 - uTime * 0.03));
  color = mix(color, vec3(1.0), clouds * 0.18);

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
      },
    })

    this.mesh = new THREE.Mesh(this.geometry, this.material)
    this.scene.add(this.mesh)

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
    this.renderer.render(this.scene, this.camera)
    this.frame = requestAnimationFrame(this.loop)
  }
}
