# World Generation Lab

## Purpose
Interactive sandbox for exploring procedural terrain generation with erosion,
rivers, biomes, and weather. No server dependency — pure local simulation.
Results inform the server-side RegionProfile generation pipeline.

## Architecture Alignment
- Server generates world data (ADR 0001 — server-authoritative)
- Client renders from received data (thin client)
- This lab prototypes the *algorithm* that will run on the server
- Once tuned, the algorithm moves to `NaturalResourceLoader`/`RegionProfileLoader`

## Implementation

### Core: Heightmap + Erosion (128x128 grid)

**Noise generation:**
- Multi-octave simplex noise for base continental shape
- Large scale: continents/oceans. Small scale: local hills.
- Sea level threshold → coastlines emerge naturally

**Hydraulic erosion (Sebastian Lague approach):**
- Drop virtual raindrop at random position
- Droplet flows downhill, picks up sediment based on speed × slope
- Deposits sediment when slowing (flat terrain, pooling)
- Evaporates gradually
- Parameters: erosion rate, deposition rate, evaporation, inertia, capacity
- Run N iterations (tunable via slider, 0 to 500K)

**Thermal erosion:**
- If slope between adjacent cells exceeds talus angle, material slumps downhill
- Softens ridges, creates scree slopes
- Parameter: talus angle (tunable)

### Rivers: Flow Accumulation

1. Fill depressions (remove local minima)
2. Calculate flow direction per cell (steepest descent to neighbor)
3. Accumulate upstream cell count
4. Threshold → river cells
5. Render as blue lines/mesh on terrain

### Biomes: Climate from Terrain

- **Moisture**: rainfall simulation from wind direction + orographic effect
  - Wind carries moisture from ocean
  - Moisture drops when hitting elevation (rain shadow)
- **Temperature**: base from altitude (higher = colder)
- **Biome selection**: temp × moisture matrix
  - Hot+wet = tropical forest
  - Hot+dry = desert
  - Cold+wet = boreal forest
  - Cold+dry = tundra
  - Very cold = snow/ice

### Visualization Modes (toggle in tuning panel)

1. **Height** — grayscale altitude
2. **Shaded** — terrain shader (grass/rock/snow by slope+altitude)
3. **Moisture** — blue gradient showing rainfall distribution
4. **Biome** — colored by biome type
5. **Flow** — river network overlay
6. **Erosion delta** — red/blue showing where material was removed/deposited

### Tuning Panel Sections

**Generation:**
- Noise octaves (1-8)
- Noise frequency (continent scale)
- Sea level (0-1)
- Volcanic hotspots (0-5, click to place)

**Erosion:**
- Iterations (0 to 500K, with "run 10K more" button)
- Erosion rate
- Deposition rate
- Evaporation rate
- Droplet inertia
- Sediment capacity

**Climate:**
- Wind direction (angle slider)
- Wind strength
- Base temperature (latitude proxy)
- Moisture evaporation from ocean

**Display:**
- Visualization mode (dropdown or toggle)
- Vertical exaggeration (how tall mountains appear)
- Water level opacity
- River threshold

**Time:**
- Season slider (0-1, spring→summer→fall→winter)
- Day/night slider

### Controls
- Same movement as atmosphere lab (WASD camera-relative, orbit, zoom)
- Can view terrain from above (top-down map view) or walk on it
- "Regenerate" button → new random seed
- "Erode More" button → add N erosion iterations to current terrain

## Build Order

1. **Heightmap + 3D mesh** — noise terrain on 128x128 grid, tunable octaves/frequency
2. **Hydraulic erosion** — droplet simulation with tunable params, see results in real-time
3. **Visualization modes** — height/shaded/moisture toggle
4. **River generation** — flow accumulation + threshold + blue overlay
5. **Biome coloring** — moisture × temperature → biome colors
6. **Season slider** — shifts vegetation colors, adds snow at altitude

## Performance Budget
- 128x128 grid = 16K cells
- Mesh generation: ~1ms
- 10K erosion droplets: ~50-100ms (can run async)
- River accumulation: ~5ms
- All runs on client CPU, no GPU compute needed at this scale
- Slider changes regenerate in real-time (except erosion which needs a button)
