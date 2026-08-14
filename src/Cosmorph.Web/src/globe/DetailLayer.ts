import * as THREE from 'three'
import type { DetailBuffers } from '../render/detail'

/**
 * Everything standing on the planet, in one draw call: the critters of each species and the props for
 * the materials and constructions a cell holds. The positions come from {@link DetailBuffers}, which
 * is built by pure code with no WebGL in it; this file is only the drawing.
 *
 * The quads are billboarded in view space from a centre on the unit sphere, so nothing has to be
 * rotated on the CPU when the planet turns. A thing on the far side of the world collapses to a
 * point in the vertex shader, which is the cheapest back-face cull there is.
 */

const VERTEX_SHADER = `
attribute vec3 aCentre;
attribute float aKind;
attribute float aScale;
attribute float aPhase;
attribute vec3 aTint;
uniform float uTime;
uniform float uFade;
varying vec2 vShape;
varying float vKind;
varying vec3 vTint;
varying float vLight;

void main() {
  vKind = aKind;
  vTint = aTint;

  // The quad's own space, moved so the base of the silhouette sits at y = 0: a tree grows up out of
  // the ground it was placed on rather than being centred on it.
  vec2 corner = vec2(position.x, position.y + 0.5);
  vShape = corner;

  vec3 centre = normalize(aCentre);
  vec4 world = modelMatrix * vec4(centre * 1.004, 1.0);
  vec3 worldNormal = normalize(mat3(modelMatrix) * centre);

  // Anything that has turned away from the camera is scaled to nothing, so the far side of the
  // planet costs one vertex shader each and not a single fragment.
  float facing = step(0.12, dot(worldNormal, normalize(cameraPosition - world.xyz)));
  float size = aScale * uFade * facing;

  // Only the living things wander, and each one from its own phase, so a field of critters never
  // sways as one body. uTime stops advancing under reduced motion, which freezes this on its own.
  float wander = step(aKind, 2.5) * sin(uTime * 0.8 + aPhase * 6.2831) * 0.22;

  vec4 view = viewMatrix * world;
  view.x += (corner.x + wander) * size;
  view.y += corner.y * size;

  vLight = 0.62 + 0.38 * clamp(
    dot(normalize(normalMatrix * centre), normalize(vec3(0.6, 0.5, 0.8))), 0.0, 1.0);
  gl_Position = projectionMatrix * view;
}
`

// Silhouettes drawn from a handful of shapes and cut out with discard. The kinds are numbered by
// render/palette.ts, which is also where their colours live.
const FRAGMENT_SHADER = `
precision mediump float;
varying vec2 vShape;
varying float vKind;
varying vec3 vTint;
varying float vLight;

float ellipse(vec2 p, vec2 c, vec2 r) {
  vec2 d = (p - c) / r;
  return 1.0 - step(1.0, dot(d, d));
}

float box(vec2 p, vec2 c, vec2 h) {
  vec2 d = abs(p - c) - h;
  return 1.0 - step(0.0, max(d.x, d.y));
}

/** A triangle standing on its base: widest at yBase, a point at yTip. */
float cone(vec2 p, float yBase, float yTip, float halfWidth) {
  float t = clamp((p.y - yBase) / max(0.001, yTip - yBase), 0.0, 1.0);
  float band = step(yBase, p.y) * step(p.y, yTip);
  return band * (1.0 - step(halfWidth * (1.0 - t), abs(p.x)));
}

void main() {
  vec2 p = vShape;
  float inside;

  if (vKind < 0.5) {
    // Producer: a low cushion of moss with a couple of fronds standing out of it.
    inside = max(
      ellipse(p, vec2(0.0, 0.18), vec2(0.36, 0.20)),
      max(cone(p, 0.20, 0.62, 0.07), cone(p - vec2(0.18, 0.0), 0.16, 0.48, 0.06)));
  } else if (vKind < 1.5) {
    // Herbivore: a heavy body on four legs, head up and grazing forward.
    inside = max(
      max(ellipse(p, vec2(-0.04, 0.44), vec2(0.30, 0.18)), ellipse(p, vec2(0.26, 0.62), vec2(0.14, 0.13))),
      max(box(p, vec2(-0.18, 0.14), vec2(0.05, 0.15)), box(p, vec2(0.12, 0.14), vec2(0.05, 0.15))));
  } else if (vKind < 2.5) {
    // Predator: lower and longer, with a tail behind and ears on top.
    inside = max(
      max(ellipse(p, vec2(-0.02, 0.36), vec2(0.34, 0.14)), ellipse(p, vec2(0.28, 0.48), vec2(0.13, 0.11))),
      max(max(box(p, vec2(-0.20, 0.13), vec2(0.04, 0.14)), box(p, vec2(0.14, 0.13), vec2(0.04, 0.14))),
          max(cone(p - vec2(0.26, 0.0), 0.56, 0.72, 0.06), box(p, vec2(-0.36, 0.46), vec2(0.10, 0.03)))));
  } else if (vKind < 3.5) {
    // Timber: a conifer on a short trunk.
    inside = max(box(p, vec2(0.0, 0.10), vec2(0.05, 0.10)), cone(p, 0.14, 1.0, 0.34));
  } else if (vKind < 4.5) {
    // Stone: a boulder half-buried in the ground.
    inside = ellipse(p, vec2(0.0, 0.06), vec2(0.40, 0.34)) * step(0.02, p.y);
  } else if (vKind < 5.5) {
    // Fibre: a tuft of grass, three blades of uneven height.
    inside = max(
      cone(p, 0.0, 0.92, 0.06),
      max(cone(p - vec2(-0.17, 0.0), 0.0, 0.64, 0.05), cone(p - vec2(0.16, 0.0), 0.0, 0.74, 0.05)));
  } else {
    // Whatever a Warden has built here: walls and a roof.
    inside = max(box(p, vec2(0.0, 0.24), vec2(0.30, 0.24)), cone(p, 0.46, 0.88, 0.40));
  }

  if (inside < 0.5) {
    discard;
  }

  // Darker where it meets the ground, so each thing sits on the planet instead of floating over it.
  float grounded = mix(0.68, 1.0, clamp(p.y * 1.8, 0.0, 1.0));
  gl_FragColor = vec4(vTint * vLight * grounded, 1.0);
}
`

export class DetailLayer {
  readonly mesh: THREE.InstancedMesh
  /**
   * The buffers this layer draws. The attributes wrap the very same arrays, so refilling the buffers
   * and calling {@link update} uploads the new contents without copying anything. A layer therefore
   * belongs to one set of buffers for life: hand it a different set and build a new layer.
   */
  readonly buffers: DetailBuffers

  private readonly geometry: THREE.PlaneGeometry
  private readonly material: THREE.ShaderMaterial
  private readonly attributes: readonly THREE.InstancedBufferAttribute[]

  constructor(buffers: DetailBuffers) {
    this.buffers = buffers
    this.geometry = new THREE.PlaneGeometry(1, 1)
    this.material = new THREE.ShaderMaterial({
      vertexShader: VERTEX_SHADER,
      fragmentShader: FRAGMENT_SHADER,
      uniforms: {
        uTime: { value: 0 },
        uFade: { value: 0 },
      },
    })

    const centre = new THREE.InstancedBufferAttribute(buffers.centre, 3)
    const kind = new THREE.InstancedBufferAttribute(buffers.kind, 1)
    const scale = new THREE.InstancedBufferAttribute(buffers.scale, 1)
    const phase = new THREE.InstancedBufferAttribute(buffers.phase, 1)
    const tint = new THREE.InstancedBufferAttribute(buffers.tint, 3)
    this.attributes = [centre, kind, scale, phase, tint]

    this.geometry.setAttribute('aCentre', centre)
    this.geometry.setAttribute('aKind', kind)
    this.geometry.setAttribute('aScale', scale)
    this.geometry.setAttribute('aPhase', phase)
    this.geometry.setAttribute('aTint', tint)

    this.mesh = new THREE.InstancedMesh(this.geometry, this.material, Math.max(1, buffers.capacity))
    // Each instance is placed by the vertex shader, so the geometry's own bounds say nothing useful
    // about where this ends up on screen. Culling it against them would hide the whole layer.
    this.mesh.frustumCulled = false
    this.mesh.count = 0
    this.mesh.visible = false
  }

  /** Uploads whatever the buffers now hold and draws exactly as many things as they describe. */
  update(): void {
    for (const attribute of this.attributes) {
      attribute.needsUpdate = true
    }
    this.mesh.count = Math.min(this.buffers.capacity, Math.max(0, this.buffers.count))
    this.applyVisibility()
  }

  /** How much of the close-up is showing, from nothing at all to fully grown. */
  setFade(fade: number): void {
    this.material.uniforms.uFade!.value = Math.min(1, Math.max(0, fade))
    this.applyVisibility()
  }

  setTime(time: number): void {
    this.material.uniforms.uTime!.value = time
  }

  dispose(): void {
    this.mesh.removeFromParent()
    this.geometry.dispose()
    this.material.dispose()
    this.mesh.dispose()
  }

  /** A viewer who never zooms in should not pay for this layer at all, not even an empty draw. */
  private applyVisibility(): void {
    const fade = this.material.uniforms.uFade!.value as number
    this.mesh.visible = fade > 0.002 && this.mesh.count > 0
  }
}
