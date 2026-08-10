import * as THREE from 'three'

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
  vec4 state = texture2D(uState, vUv);
  float pattern = floor(state.a * 255.0 / 60.0 + 0.5);
  vec3 color = state.rgb;

  float shade = 0.55 + 0.45 * clamp(dot(vNormal, normalize(vec3(0.6, 0.5, 0.8))), 0.0, 1.0);
  color *= shade;

  float marks = hatch(vUv, pattern);
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
  private autoRotate = true
  private reducedMotion: boolean
  private pointerDown = false
  private pointerX = 0
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

    this.texture.needsUpdate = true
  }

  /** Rotates the planet toward a location without changing the zoom level. */
  lookAtLocation(latitude: number, longitude: number): void {
    this.targetRotation = -(longitude * Math.PI) / 180
    const tilt = Math.max(-0.6, Math.min(0.6, (latitude * Math.PI) / 360))
    this.mesh.rotation.x = this.reducedMotion ? tilt : this.mesh.rotation.x
    if (this.reducedMotion) {
      this.rotation = this.targetRotation
      this.mesh.rotation.y = this.rotation
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

  private readonly onPointerUp = (): void => {
    this.pointerDown = false
  }

  private readonly onWheel = (event: WheelEvent): void => {
    const next = this.camera.position.z + Math.sign(event.deltaY) * 0.15
    this.camera.position.z = Math.min(6, Math.max(1.35, next))
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
      this.rotation += (this.targetRotation - this.rotation) * Math.min(1, delta * 3)
    } else {
      this.rotation = this.targetRotation
    }

    this.mesh.rotation.y = this.rotation
    this.renderer.render(this.scene, this.camera)
    this.frame = requestAnimationFrame(this.loop)
  }
}
