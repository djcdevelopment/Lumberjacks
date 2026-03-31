# Tree Felling Physics Lab

## Purpose

Interactive sandbox for exploring realistic tree felling physics — axe swing mechanics, notch/back-cut geometry, hinge analysis, fall dynamics, and barber chair detection. No server dependency — pure local simulation. Results inform the server-side tree felling pipeline and validate that detailed physics can be compressed to 24-byte network payloads.

## Architecture Alignment
- Server owns tree state (ADR 0001 — server-authoritative)
- Client renders from received data (thin client)
- This lab prototypes the *physics algorithm* that will run on the server
- The detailed polar-slice model (lab-only) collapses to `CompactTreeState` (6 floats, 24 bytes) for ADR 0012 compliance
- The cruise → inspect → plan → fell gameplay loop maps directly to AoI tiers (ADR 0015)

## Reference Material

Three source documents informed the physics:

1. **USFS "An Ax to Grind" Manual** — Chopping technique (forehand/backhand at 45°), felling procedure (front notch 1/3-1/2 diameter, back cut 2" higher, leaving hinge wood), limbing, bucking, splitting
2. **Wisconsin Tree Felling Techniques Manual** — Open-face notch (70°+), bore cutting, hinge dimensions (length ~80% DBH, width ~10% DBH), barber chair risk, side scarring, felling against natural lean
3. **Rod Cross, "Physics of swinging a striking implement"** — Driven circular arc model (not pendulum). Gravity is negligible. Head velocity V = ω × R where ω = π/(2T). Centripetal force FC = mv²/R dominates.

Additionally, the Pluta & Hryniewicz papers on cutting tool dynamics informed the penetration model (three forces: gravity, inertia, material reaction).

## Implementation

### TreeFellingSim.cs — Pure C# Physics Engine (no Godot dependency)

**Core model: Polar cross-section slices**

The trunk is a vertical stack of `TrunkSlice` arrays. Each slice is `float[36]` — 36 sectors at 10° each representing remaining wood radius fraction (1.0 = intact, 0.0 = fully cut). Cuts zero out sectors at given depths. The hinge is the uncut bridge between notch and back cut.

**Swing physics (Cross model):**
```
swingTime = 0.35s / swingEffort
ω = π / (2 × swingTime)           — angular velocity at impact
V_head = ω × SwingRadius           — head velocity (8-20 m/s typical)
KE = ½ × HeadMass × V²            — kinetic energy (50-300J)
FC = HeadMass × V² / SwingRadius   — centripetal force (dominant, >>gravity)
```

**Penetration model:**
```
penetration = KE × wedgeFactor / (JankaN × bladeWidth × moistureFactor)
```
Where Janka hardness: Oak ~6000N, Pine ~3300N. Oak takes ~2× more energy to cut than Pine.

**Cut types:**
- `ApplyNotchCut` — open-face (70°) or conventional (45°) front notch
- `ApplyBackCut` — from opposite side, height offset above notch floor
- `ApplyBoreCut` — plunge cut for large trees / felling against lean
- `ApplyAxeStrike` — individual chop removing a wedge of material
- `ApplyPhysicsStrike` — physics-based swing using Cross model → delegates to ApplyAxeStrike

**Hinge analysis:**
- `ComputeHinge()` — scans remaining material to find hinge width/depth
- Hinge strength: σ_yield × (w × t² / 6) (rectangular section modulus)
- Progressive fiber stress during fall

**Fall dynamics (5 phases):**
1. Standing — cuts accumulate
2. HingeBending — tipping moment > resistance, slow rotation around hinge
3. FreeFall — hinge failed, gravity-driven rotation
4. Ground — impact
5. BarberChair — trunk splits vertically (narrow hinge + low back cut + prone species)

**Network projection:**
- `GetCompactState()` → `CompactTreeState` (6 floats, 24 bytes)
- Proves detailed model collapses to entity_update datagram budget

### TreeFellingLab.cs — Godot Visualization

**Scene structure (all programmatic in _Ready):**
- Ground plane (flat or sloped)
- Tree root with trunk ArrayMesh (built from sim slices), canopy sphere, stump
- Player orb (green sphere with axe mesh) — WASD movement, auto-faces tree
- Wind/lean direction arrows
- Orbit camera (RMB drag, scroll zoom)
- HUD showing phase, hinge dimensions, torque values, Cross physics values, wire cost
- ASCII cross-section display
- TuningPanel with 8 sections

**Tuning panel sections:**
- Tree Properties (species, DBH, height, lean, crown, age, green/dry)
- Notch Control (type, depth, height, face angle)
- Back Cut (height offset, hinge target)
- Axe Strikes (manual angle/height/power)
- Player Axe (mode, strike height, axe mass, wedge angle, swing radius)
- Environment (slope, wind)
- Simulation (time scale, viz mode, start/reset/step)
- Presets (Textbook Fell, Barber Chair, Hillside, Against Lean, Big Oak)

**Visualization modes:** Shaded, Cross-section, Force diagram, Stress map, Fall trajectory, Side profile

### Units & Terminology

All physics internally in SI (meters, kg, seconds, newtons, joules). Display in forestry-standard imperial where appropriate (DBH in inches, height in feet). Key terminology:

| Term | Meaning |
|------|---------|
| `NaturalLeanDeg` | Tree's growth lean from vertical (not strike accumulation) |
| `NaturalLeanBearing` | Compass direction of natural lean |
| `FallTiltDeg` | Current rotation during active fall |
| `FallBearing` | Compass direction of fall |
| `removeFraction` | Fraction of trunk radius removed per strike |
| `SwingRadiusM` | Shoulder to axe head distance (Cross's R) |
| `SwingResult` | Physics output from a strike (velocity, KE, FC, penetration) |

## Known Technical Debt

- **Axe swing animation:** Tween-based rotation doesn't correctly model pivot-at-grip mechanics. The axe node's origin needs to be at the hand, with handle+blade extending outward. Current implementation rotates the wrong axis. Needs a proper Node3D hierarchy: GripPivot → HandleMesh + BladeMesh, with the tween rotating the pivot.
- **No chip formation model:** Multiple strikes at similar angles should accumulate into a notch with chip-popping behavior (USFS "top-bottom-middle" technique). Currently each strike is independent.
- **Penetration scaling constant (3.5):** Empirically tuned, not derived from first principles.
- **No tree-on-tree collision:** Multi-tree interaction (hung-up trees, domino) not implemented.
- **No limbing/bucking:** Post-fell processing not implemented.

## Verification

1. Open Godot editor, load `TreeFellingLab.tscn`, run scene (F6)
2. Tune tree properties → trunk mesh rebuilds
3. Cut notch → material visibly removed, hinge highlighted orange
4. Make back cut → HUD shows hinge dimensions, barber chair warning if applicable
5. Press Space → tree falls toward notch face
6. HUD shows: head velocity 8-20 m/s, KE 50-300J, FC 50-500N (Cross range)
7. "Wire: 24 bytes" confirms compact state fits ADR 0012 budget
8. Try presets: each produces distinct, physically plausible behavior
