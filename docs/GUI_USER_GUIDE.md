# VillageOS GUI — User Guide

VillageOS GUI is a web-based interface for the VillageOS temporal graph database. It lets you visually explore your model as an interactive graph, monitor the broker in real time, and perform every CLI operation through graphical forms — all from your browser.

> **Screenshots**: To re-capture screenshots, run `node docs/capture-screenshots.mjs` while the Broker and GUI dev server are running. See the script for details.

---

## 1. Getting Started

### 1.1 Prerequisites

- **VillageOS Broker** running (provides the REST API and SignalR hub)
- **Node.js 20+** installed (for the Vite dev server)
- A modern browser — Chrome, Firefox, or Safari (Safari has some WebGL limitations, see Section 8)

### 1.2 Launching the GUI

Open two terminals:

```bash
# Terminal 1: Start the Broker
dotnet run --project vos.Broker

# Terminal 2: Start the GUI dev server
cd vos.GUI
npm run dev
```

Open `http://localhost:5173` in your browser. You'll see a login form. Sign in with `admin` / `admin` (the default credentials, overridable via `VOS_ADMIN_PASSWORD` env var before first broker run). If multiple models exist, you'll be prompted to select one. Alternatively, set `VITE_API_KEY` in `.env.local` for auto-login during development.

If your account has been flagged for a password change (e.g., created by an admin with `MustChangePassword: true`), you'll see a password change form after login. Enter your current password and choose a new one — the app won't be accessible until the password is changed.

Your session stays alive automatically — the GUI silently refreshes your authentication token in the background before it expires, so you won't be logged out unexpectedly during normal use.

After login, the **Dashboard** is the default landing page. The sidebar on the left provides five navigation items:

| Icon | Page | Purpose |
|------|------|---------|
| Grid | **Dashboard** | Model statistics, service health, daemon status, and live activity feed |
| Network | **Graph** | Interactive graph visualization with search, clustering, 3D building view, and CRUD |
| Clock | **Temporal** | Time-range mutation explorer for viewing property change history |
| Boxes | **Things** | Dedicated search page — find things by name across the entire model |
| Search | **Properties** | Dedicated search page — find things and relationships by property name |

The sidebar can be collapsed to icon-only mode by clicking the chevron button at the top-right of the sidebar panel.

Both the Dashboard and Graph pages include **Logout** and **Switch Model** buttons in their headers, allowing you to sign out or change models without re-entering credentials.

### 1.2a Switching Seeds (Models)

Click the **Switch Model** button in the Dashboard or Graph header to open the seed library picker. This shows all seed files available in the broker's `seeds/library/` folder.

The seed picker provides:
- **Search bar** — type to filter seeds by name
- **Sortable columns** — click "Name" or "Size" headers to sort ascending/descending
- **Scrollable list** — handles large numbers of seeds with a fixed-height scrollable area
- **Result count** — shows "N of M seeds" when filtering

Click any seed to load it. The current model is replaced — the broker clears the existing data, loads the seed file, and the GUI automatically re-scopes your authentication to the new model. The graph page resets and renders the new model.

**Saving a seed:** At the bottom of the seed picker, type a name in the "Save current model" input and click **Save** to snapshot the current model to the library folder. It appears immediately in the seed list above.

### 1.3 Loading Your First Model

The GUI starts with an empty model. To load sample data, use one of these methods:

**Via CLI:**
```bash
cd vos.CLI
dotnet run -- deserialize ../vos.Broker/seeds/village.seed.json
```

**Via REST API:**
```bash
curl -X POST https://localhost:7243/api/model \
  -H "Content-Type: application/json" \
  -H "X-API-Key: <your-api-key>" \
  -d @vos.Broker/seeds/village.seed.json
```

**Via auto-load:** Place seed files in `vos.Broker/seeds/` — they are loaded automatically on broker startup.

After loading, click **Graph** in the sidebar to see your model rendered as an interactive graph.

### 1.4 Available Seed Models

| Seed File | Best For |
|-----------|----------|
| `village.seed.json` | Full-featured demo: type hierarchies, multi-domain relationships (energy, water, biodiversity, transport), IFC geometry for single-building 3D views |
| `warehouse.seed.json` | Type hierarchy exploration: zones, conveyors, AMRs, sensors, controllers with multi-level inheritance (e.g., `ConveyorPLC → Controller → SmartAppliance`) |
| IFC-imported seeds | Import IFC (BIM) files via `vos.Tools.IfcIngest` — see [IFC_IMPORT_GUIDE.md](IFC_IMPORT_GUIDE.md). |

The **village seed** is recommended for this guide because it demonstrates all features including per-building 3D views.

---

## 2. The Graph View

The Graph page is where you'll spend most of your time. It renders every thing in your model as a node and every relationship as a directed edge, using a WebGL-accelerated force-directed layout powered by Sigma.js and graphology.

### 2.1 Understanding What You See

After loading the village seed, you'll see the model rendered as nodes connected by directed edges. The force-directed layout automatically organizes nodes — things with many relationships gravitate toward the center, while leaf nodes drift to the periphery.

**Node size** reflects importance: nodes with more incoming relationships appear larger. The "PhysicalThing" type node, for example, is one of the largest because many things in the village have an "is PhysicalThing" relationship.

**Node colors** are derived from the data — no hardcoded color assignments:
- **Blue (large)** — Type definitions like "Home", "Sensor", "PhysicalThing" — anything that is a target of "is" relationships
- **Gold/amber (small)** — Predicate things like "is", "has", "feeds", "powers" — these define relationship types
- **Vibrant colors (medium)** — Instance nodes are colored by their type via "is" relationships. Every "Home" instance shares one color, every "SolarArray" shares another, every "GardenPlot" yet another. This happens automatically — adding new types gets them distinct colors without any configuration
- **Slate grey** — Instances with no "is" relationship (untyped things)

**Edge colors** are assigned per predicate type. Explicit color mappings can be configured via the `PredicateColors` property on the `GUI_Settings` Thing (a JSON object like `{"consumes":"#fb7185","produces":"#22d3ee"}`). Predicates without an explicit mapping get a deterministic color from an 8-color palette. This makes it easy to trace, say, all "consumes" relationships (one color) versus all "produces" relationships (another color) across the graph. The radial predicate menu uses the same colors as the edges.

**Logical vs. physical nodes**: In the village seed, nodes with IFC geometry (buildings, sensors mounted on buildings, etc.) are "physical" — they have a real-world shape. Nodes without geometry (types like "Home", predicates like "powers", abstract concepts) are "logical" — they exist only in the graph layout. This distinction drives the toolbar's Semantic Zoom feature, which auto-expands/collapses logical node groups based on zoom level.

### 2.2 Navigating the Graph

**Pan**: Click and drag anywhere on the background to move the view.

**Zoom**: Use the scroll wheel to zoom in and out. Zooming is centered on your cursor position, so point at the area you want to examine and scroll in.

**Fit to viewport**: If you get lost or want to see the full graph, click the **Fit** button (the square-with-arrows icon in the toolbar). This animates the camera to frame all nodes.

**Re-layout**: If the force layout has settled into an arrangement you don't like, click the **Re-layout** button (circular arrow icon). This perturbs all node positions slightly, causing the force simulation to re-settle into a new arrangement. Each click produces a different layout.

**Spread mode**: If nodes are too tightly clustered, click the **Spread** button (expand icon) to enter spread mode. This boosts the repulsion force so nodes push apart while maintaining their cluster structure. Click again to return to normal layout. The button is highlighted blue when active.

**Freeze/Resume**: Click the **Pause** button to freeze the force simulation. Nodes stop moving and you can examine the graph in its current state. Click **Play** to resume. This is useful when the layout is "good enough" and you want to stop the jittering.

### 2.3 The Toolbar

The toolbar sits at the bottom-left of the graph canvas. From left to right:

| Icon | Name | What it does |
|------|------|-------------|
| **+** (search bar) | Create Thing | Opens inline form to create a new thing by name |
| **+** | Zoom In | Animated zoom toward center |
| **−** | Zoom Out | Animated zoom away from center |
| ⛶ | Fit | Reset camera to show all nodes |
| ↻ | Re-layout | Perturb positions to re-settle force layout |
| ⤢ | Spread | Toggle spread mode — boosts repulsion to push nodes apart (blue when active) |
| ⏸/▶ | Freeze/Resume | Stop or start the force simulation |
| 🔍 | Semantic Zoom | Auto-expand/collapse logical nodes based on zoom level |
| ☰ | Layers | Open predicate selection menu (same as right-click) |

**Top-right controls** (next to model name):

| Icon | Name | What it does |
|------|------|-------------|
| ⇄ | Switch Model | Switch to a different seed/model |
| ⎋ | Log Out | End the current session |

When clustering is active, additional controls appear to the right: the active predicate name with edge count, and an X button to clear clustering.

---

## 3. Selecting and Inspecting Things

### 3.1 Selecting a Node

Click any node in the graph. A **detail panel** slides in from the right showing everything about that thing.

The detail panel has three tabs (four for things with geometry):

**Properties** — Shows the thing's own properties (name, value, type). Click the **pencil icon** to toggle inline editing mode: each property value becomes an editable input field with a delete button (trash icon). Edit a value and press **Enter** or click away to save; press **Escape** to cancel. A blue border indicates unsaved changes. The property type is automatically preserved (a number stays a number, a boolean stays a boolean). In edit mode, an **Add Property** row appears at the bottom with name, type dropdown, and value inputs — press Enter or click "+" to add a new property. Below the own properties, inherited properties are displayed with a clickable "← SourceName" link showing which type they come from. For example, clicking a "Serpentine-Home-4" node might show properties like `energy_rating: A+` inherited from the "Home" type. Clicking the source link navigates you to that type node. In edit mode, inherited property values are also editable (but cannot be deleted) — editing an inherited property creates an own property override that shadows the inherited value.

**Relationships** — Lists all incoming and outgoing relationships. Each row shows the other thing's name and the predicate. For example, "Serpentine-Home-4" might show:
- Outgoing: `→ is → Home` (this is a Home)
- Outgoing: `→ has → SerpentineRoofSolar-4` (has a solar panel)
- Incoming: `← feeds ← VillageMicrogrid` (fed by the microgrid)

Each thing name is a clickable link — clicking it navigates to that node, selecting it and scrolling the graph to center on it. Click the **chevron** next to a relationship to expand it and see its properties. Multiple relationships can be expanded simultaneously. Relationship properties also support inline editing (pencil toggle, same as own properties). Each relationship row also has an **edge-detail icon** — clicking it opens the **EdgeDetailPanel** for that relationship, where you can view and edit its properties and ranges without having to click the edge in the graph. In edit mode, an **Add Relationship** row appears at the bottom of each section (outgoing/incoming) with predicate and other-thing pickers — select both and click "+" to create a new relationship inline.

**Ranges** — Shows any active ranges defined on this thing with their current state. A windmill spinner appears while data loads. Own ranges, inherited ranges, current states, and relationship ranges are all fetched in a single composite API call for efficiency. The tab uses a temporal snapshot approach — data reflects a point-in-time view when the tab was opened, and is not disrupted by ongoing SignalR state-change events. Click the **refresh icon** in the tab bar to re-fetch the latest data without navigating away.

**3D** (conditional) — For things with IFC mesh geometry, a "3D" tab appears showing an interactive 3D view of the element. The model auto-rotates slowly. You can drag to orbit, scroll to zoom, and examine the element from any angle. For IFC container things (e.g., IfcBuilding, IfcBuildingStorey) that have no own geometry but contain child elements via `contains`/`aggregates` relationships, the 3D view renders all child meshes together, colored by IFC class. This tab is hidden on Safari due to WebGL context limits.

### 3.2 Selecting an Edge

Click any edge (relationship line) in the graph. The detail panel opens with two tabs:

**Properties** — Shows the relationship's Subject, Predicate, and Target — each as a clickable link to the respective thing. Any properties on the relationship are also displayed with inline editing. A "Delete Relationship" button at the bottom allows removal (with confirmation).

**Ranges** — Shows any ranges defined directly on this relationship, mirroring the Ranges tab on nodes. Active states appear as colored severity badges (green for nominal, yellow for warning, red for critical). Own ranges list their criteria expressions and current evaluation status. If bounds are defined, per-binding deviation details show how far actual values are from expected.

### 3.3 Right-Click Context Menu

**Right-click any node** to open a context menu with quick actions:

- **View Details** — Opens the detail panel for that node (same as clicking it)
- **Expand Relationships** — Reveals all of that node's edges in cluster mode
- **Toggle Logical Nodes** — Shows/hides logical children (only for geo nodes)
- **View in 3D** — Opens the detail panel on the 3D tab (only for nodes with geometry, not on Safari)
- **Copy ID** — Copies the node's GUID to your clipboard (shows a toast notification)
- **Delete** — Removes the thing after a confirmation dialog (shown in red, separated by a divider)

The context menu closes when you press Escape, click outside it, or select an action. Only one menu can be open at a time — right-clicking a node closes the radial predicate menu, and vice versa.

### 3.4 Deselecting

Click the empty background (not on any node or edge) to deselect everything. The detail panel closes and the graph returns to its default state.

---

## 4. Searching

The search bar at the top of the Graph page provides powerful filtering.

### 4.1 Basic Search

Start typing in the search bar. As you type, nodes whose names contain your text remain at full opacity, while non-matching nodes are dimmed (physical nodes) or hidden entirely (logical nodes). Edges are hidden by default — only edges where **both** endpoints match the search remain visible. The match count appears to the right of the search options (e.g., "5 found").

**Example**: Type `Home` in the village seed — you'll see "Home" (the type), "Home-1", "Home-2", etc. light up while everything else dims. Only edges connecting two matched nodes are visible.

### 4.2 Search Modes

Three toggle buttons next to the search bar modify matching behavior:

- **Aa** (Case-Sensitive): When active, "home" won't match "Home". Off by default.
- **=** (Exact Match): When active, only nodes whose name exactly equals your query match. "Home" matches the type but not "Home-1".
- **.\*** (Regex): When active, your query is treated as a regular expression. Type `^Solar` to find all things starting with "Solar", or `Sensor|Monitor` to find things containing either word.

### 4.3 Comma-Separated Search

Type multiple names separated by commas to find several things at once:

```
Home-1, SolarGreenhouse-1, VillageMicrogrid
```

This highlights all three nodes and their neighborhoods simultaneously — useful for comparing how different things relate to each other.

### 4.4 Clearing Search

Delete the text in the search bar (or select all and press Backspace) to return the graph to its unfiltered state. Nodes return to full visibility; edges return to the default hidden state (they appear when you select or hover a node, or activate predicate filters).

### 4.5 Thing Search Page

Click **Things** in the sidebar to open the dedicated thing search page (`/things`). Unlike the Graph search bar (which filters the live graph visualization), this page lets you find things by name without the graph rendering overhead — useful for large models.

**How to use it:**
1. Type any part of a thing's name in the search field. Results update automatically after 250 ms.
2. Each result card shows:
   - **Name** (blue link) — click to navigate directly to that thing in the Graph with its detail panel open
   - **Type badge** (green) — the type name from the thing's `is` relationship, if one exists
   - **Property preview** — up to four key=value pairs from the thing's own properties
   - **Props / Rels counters** — total own-property count and relationship count

**Result ranking:** Results are sorted by match quality before alphabetically within each tier:

| Tier | Description | Example (query: `"building"`) |
|------|-------------|-------------------------------|
| 0 — Exact | Name equals query (case-insensitive) | `building` |
| 1 — Prefix | Name starts with query | `Building-A`, `BuildingType` |
| 2 — Substring | Name contains query | `New-Building`, `OldBuilding-2` |
| 3 — ID | Thing's UUID contains query (ID search) | any thing whose ID includes `building` |

**Pagination:** The first 100 results are shown. Click **Show more** to load the next 100.

**Export:** Use **Copy as Markdown** or **Download as Markdown** to export the full result set as a markdown table (`| Name | Type | Properties | Relationships |`), regardless of how many results are paginated.

### 4.6 Property Search Page

Click **Properties** in the sidebar to open the property search page (`/properties`). This searches across all thing and relationship properties by **property name** — useful when you know a field exists but not which things have it.

**How to use it:**
1. Type any part of a property name (e.g. `quantity`, `temperature`, `pool`). Results update after 250 ms.
2. Results are grouped by property name. Each entry shows:
   - **T** badge (blue) for thing properties, **R** badge (purple) for relationship properties
   - The owner's name — click a thing name to navigate to it in the Graph
   - **via SourceName** in green if the property is inherited from a parent type
   - The current value

3. Hover any row and click **history** to view all past values for that property (thing properties only).

**Export:** Supports **Copy as Markdown** and **Download as Markdown** in the same table format as the Thing Search page.

**When to use Things vs. Properties search:**

| Use | Page |
|-----|------|
| "Find the thing called Building-A" | Things (`/things`) |
| "Find all things named something like 'Sensor'" | Things (`/things`) |
| "Find all things that have a `temperature` property" | Properties (`/properties`) |
| "Which things have a `quantity` below reorder point?" | Properties (`/properties`) — search `quantity` to surface all owners |

---

## 5. Predicate-Based Clustering

When you have a large graph with many relationship types, clustering helps you focus on specific patterns.

### 5.1 Opening the Predicate Menu

**Right-click** anywhere on the graph background (not on a node). A radial menu appears showing all predicates in your model, ordered by relationship count. Each predicate has a colored dot matching its edge color and a count showing how many relationships use it.

In the village seed you'll see predicates like **is** (type classification), **feeds** (energy/resource flow), **powers** (power supply), **serves** (service relationships), and **has** (containment/ownership).

Click a predicate to activate clustering on that relationship type. Click multiple predicates to cluster on several types simultaneously.

### 5.2 What Clustering Looks Like

When you activate "is" clustering, for example:
- Nodes connected by "is" relationships group together and appear at full opacity — you'll see "Home" surrounded by Home-1 through Home-N, "GardenPlot" surrounded by its instances, etc.
- Predicate nodes ("is", "has", "feeds") become tiny and dimmed — they're structural connectors, not interesting in this view
- Unclustered nodes (things not connected by "is") dim to dark gray and shrink
- "is" edges become visible (edges are hidden by default and only appear when triggered by selection, hover, or predicate filter)

The toolbar updates to show the active predicate with a colored dot and edge count. An **X** button clears all clustering and returns to normal view.

### 5.3 Collapsed Clusters

Large clusters (e.g., "Home" with many instances) initially collapse to save space. The representative node shows an enlarged badge like "Home (7)". Click the representative to expand the cluster, revealing all member nodes.

### 5.4 Expanded Node Exploration

**Double-click** any node in cluster mode to toggle its expanded state. When expanded, all of that node's edges (not just cluster edges) appear at reduced opacity, letting you see its full relationship neighborhood in context.

---

## 6. Single-Building 3D View

Things with IFC mesh geometry can be viewed in an interactive 3D panel.

### 6.1 Opening the 3D View

Select any thing with geometry (building, greenhouse, etc.) and click the **3D** tab in the detail panel. You can also right-click a geo node and choose **View in 3D**.

The 3D view shows an auto-rotating model of just that element with OrbitControls:

- **Zoom**: Scroll wheel to move closer or farther
- **Orbit**: Click and drag to rotate around the model
- **Pan**: Right-click and drag to shift the view

For IFC containers (IfcBuilding, IfcStorey) that have child elements via `contains`/`aggregates`, all child geometry is rendered together, colored by IFC class. Containers don't have their own mesh — the 3D view assembles children's geometry on demand.

> **Note**: The 3D tab is hidden on Safari because Safari's WebGL context limit is too low to run the 3D viewer alongside the graph canvases.

---

## 7. Dashboard

The Dashboard page provides real-time monitoring of the VillageOS Broker. It's divided into four sections:

### 7.1 Model Statistics

The top-left card shows at-a-glance counts for your model:
- **Things** — total count
- **Relationships** — total count
- **Predicates** — number of distinct predicate things
- **Properties** — total property count across all things
- **Handlers** — registered service handler count

Below the counts, a **Top Predicates** list shows the most-used relationship types ranked by count.

### 7.2 Registered Services

Shows health status for each registered service handler. Services can be in one of four states:
- **Healthy** (green badge) — responding normally
- **Unhealthy** (yellow badge) — degraded performance
- **Unreachable** (red badge) — not responding
- **Unknown** (gray badge) — health not yet determined

Each service has start/stop buttons and shows request statistics (total requests, failure count).

### 7.3 Daemons

Shows background processes managed by the broker. Each daemon shows its running status (green "Running" or red "Stopped"), process ID, and failure count.

### 7.4 Activity Feed

The right column shows a real-time log of all model mutations, streamed via SignalR WebSocket. Events include "ThingCreated", "RelationshipCreated", "PropertyChanged", etc. The feed keeps the most recent 200 events. Each event type has a distinct color (green for created, red for deleted, amber for property changes, purple/cyan for services).

**Pause/Resume** — Click the pause button to freeze the feed at its current snapshot. New events are buffered in the background and a badge shows how many are waiting. Click play to resume and see all buffered events.

**Category Filters** — Five filter chips at the top of the feed let you show/hide event categories: Model, Things, Rels, Props, Services. Click a chip to toggle it. Only events matching at least one enabled category are shown.

**Collapsible & Resizable** — The feed panel can be collapsed via the header button. When expanded, drag the top edge to resize the panel height. The height is persisted to localStorage.

The top-right controls include:
- **Broker** (green dot) — REST API connection active
- **Live** (green dot) — SignalR WebSocket connected and receiving events
- **Swagger** (document icon) — Opens the Broker API documentation (Swagger UI) in a new tab
- **Shutdown** (power icon) — Shuts down the broker (with confirmation dialog)
- **Switch Model** (arrows icon) — Switch to a different seed/model
- **Log Out** (exit icon) — End the current session

If either status indicator turns red, the broker may be down or unreachable.

---

## 8. Creating and Modifying Data

All CRUD operations are performed inline on the Graph page — no separate command page is needed.

### 8.1 Creating Things

Click the **+** button next to the search bar at the top of the Graph page. An inline form appears:

1. Type a name for the new thing
2. Press **Enter** or click the **+** submit button
3. The new thing appears immediately in the graph (via SignalR push)
4. Press **Escape** to cancel

### 8.2 Adding Properties

1. **Select a node** by clicking it — the detail panel opens
2. Click the **pencil icon** to enter edit mode
3. Scroll to the bottom of the property list — an **Add Property** row appears
4. Enter a **name**, select a **type** (string, number, boolean, datetime), and enter a **value**
5. Press **Enter** or click **+** to add the property
6. The input auto-focuses for rapid successive additions

### 8.3 Creating Relationships

1. **Select a node** and enter edit mode (pencil icon)
2. In the **Relationships** section, scroll to the bottom of either the outgoing or incoming list
3. An **Add Relationship** row appears with two pickers:
   - **Predicate**: Dropdown with known predicate things sorted to the top
   - **Other thing**: Dropdown showing non-predicate things
4. Select both and click **+** to create the relationship

### 8.4 Editing Properties

1. **Select a node or edge** and enter edit mode (pencil icon)
2. Click any property value to edit it inline — this works for both own and inherited properties
3. Press **Enter** or click away to save; **Escape** to revert
4. A blue border indicates unsaved changes
5. Click the **trash icon** to delete an own property (inherited properties cannot be deleted)
6. Editing an inherited property creates an own property override that shadows the inherited value

### 8.5 Deleting Things and Relationships

- **Delete a thing**: Right-click a node → **Delete** (confirmation dialog)
- **Delete a relationship**: Select an edge → **Delete Relationship** button in the detail panel (confirmation dialog)

### 8.6 Model Operations

Model-level operations (import/export/clear) are available via the CLI or REST API:
- **Export**: `serialize` CLI command or `GET /api/model`
- **Import**: `deserialize` CLI command or `POST /api/model`
- **Clear**: `clear model` CLI command or `DELETE /api/model`

---

## 9. Keyboard and Mouse Reference

| Input | Action |
|-------|--------|
| **Scroll wheel** | Zoom in/out (centered on cursor) |
| **Click + drag background** | Pan the view |
| **Click node** | Select node, open detail panel |
| **Click edge** | Select relationship, open detail panel |
| **Click background** | Deselect all, close detail panel and menus |
| **Right-click node** | Open node context menu (view details, expand, copy ID, delete) |
| **Right-click background** | Open radial predicate menu for clustering |
| **Double-click node** | Toggle expanded state (shows all edges in cluster mode) |
| **Hover over node** | Brightens edges connected to that node |
| **Escape** | Close any open context or radial menu |

---

## 10. Troubleshooting

### Layout won't settle / nodes keep moving
Click the **Freeze** (pause) button to stop the force simulation. You can then manually inspect the graph. Click **Play** to resume.

### Safari: blank canvas in the 3D tab
Safari limits the number of simultaneous WebGL contexts, and the graph already uses several. The 3D tab is hidden on Safari for this reason. If the main graph canvas goes blank:
1. Try resizing the browser window (triggers a refresh)
2. As a last resort, reload the page

### Real-time updates not appearing
Check the connection indicators on the Dashboard page. If "Live" shows red, the SignalR WebSocket connection has dropped. This usually recovers automatically within 30 seconds (exponential backoff). If "Broker" shows red, the broker process may have stopped.

### Search finds nothing
- Check if case-sensitive mode (**Aa**) is accidentally active
- Check if exact-match (**=**) is active — this requires the full name, not a substring
- Check if regex mode (**.\***) is active — special characters like `.` or `(` have regex meaning

---

## 11. Tips and Best Practices

**Start with the big picture, then drill down.** Load a seed, look at the full graph, then use clustering to focus on one relationship type at a time. "is" clustering shows the type hierarchy. "has" shows containment. "feeds" shows resource flow.

**Use predicate filtering for precise exploration.** Instead of seeing every relationship of a building, activate just "has" to see only what it contains, or "powers" to see only its power connections.

**Comma search for comparison.** Type `Home-1, Home-5` to highlight two homes and their neighborhoods simultaneously. This reveals shared connections (e.g., both fed by the same microgrid).

**Right-click for quick actions.** Right-click any node for a context menu with view details, expand relationships, copy ID, and delete. This is faster than opening the detail panel for common operations.

**Export before destructive changes.** Use the CLI `serialize` command or `GET /api/model` to export the model as JSON before clearing or deleting things. The exported file can be re-imported later.

**Tune the force layout.** The force-directed layout reads parameters from the "GUI_Settings" thing in your model. You can modify these properties via the CLI or REST API to control how tightly or loosely nodes arrange: `LayoutAttraction` (edge pull, default 0.0005), `LayoutRepulsion` (node push, default 0.1), `LayoutGravity` (center pull, default 0.0001), `LayoutInertia` (momentum, default 0.6), `LayoutMaxMove` (max pixels/tick, default 200), `ClusterRepulsion` (push when clustering, default 0.4).

**Temporal exploration.** After making changes over time, use the Temporal page to view the model at any past timestamp, or see the version history of a specific property.
