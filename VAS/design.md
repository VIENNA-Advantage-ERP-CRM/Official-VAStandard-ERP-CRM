# Onfinity Dashboard Design System

This file is the design source of truth for AI agents and contributors working on the Onfinity dashboard UI. Use it before creating or changing any screen, widget, module dashboard, navigation pattern, or component.

The product is an enterprise CRM and partner operations dashboard. The interface should feel calm, structured, information-dense, and operational. It should not feel like a marketing website or a generic SaaS template.

## 1. Product Personality

Onfinity UI is:

- Clear: data, status, and next actions are always easier to scan than decoration.
- Modular: screens are made from widgets that follow predictable grid sizes and hierarchy.
- Lightweight: glass-like white surfaces sit over a soft blue-to-cream workspace background.
- Operational: every widget should help a user monitor, decide, or act.
- Consistent: module dashboards share the same title bars, breadcrumb behavior, 9-column grid (1x1 must be square), widgets, list rows, and bottom task bar.

Avoid:

- Purple default SaaS styling.
- Dark-mode-first layouts unless explicitly requested.
- Random card mosaics that do not align to the 9-column grid.
- Decorative gradients inside ordinary content areas.
- Different grid systems per module.
- Overlapping or scattered widgets.

## 2. Core Layout Model

### App Shell

- Root app fills the viewport: `height: 100dvh`.
- Body/root scrolling is disabled. Scroll only the active content/workspace area.
- Background is a soft diagonal gradient:
  - Start: `rgb(199, 232, 255)`
  - End: `rgb(255, 255, 196)`
  - Direction: approximately `129deg`
- Main app max width may be capped around `1920px`, but the dashboard canvas should stay visually centered and usable.

### Top Bar

- Height: `80px`.
- Contains menu button, breadcrumb, global action group, and avatar.
- Breadcrumb is always visible.
- Home screen breadcrumb: `Home`.
- When a module has been opened, breadcrumb remains `Home > CRM`.
- Clicking `Home` shows the home dashboard.
- Clicking `CRM` returns to the CRM module dashboard.
- Current breadcrumb item is black. Inactive breadcrumb item is black at about 40 percent opacity.

### Left Navigation

- Left navigation appears only inside module context, not on home.
- Collapsed width area begins at `12px` from the left.
- Icon button size: `48px`.
- Icon size: `24px`.
- Gap between icons: `12px`.
- Active icon uses a white-to-translucent surface and soft blue shadow.
- Default module dashboard should not select any left-nav item.
- Selecting a module item opens that module dashboard and highlights that item.

### Bottom Task Bar

- Height: `38px`.
- Background: `#002640`.
- The bar touches the bottom viewport edge with no visual gap.
- Home icon block uses blue `#2084C4` and white icon.
- Bottom input/helper area uses muted copy such as `Question? Ask Aura`.

## 3. Grid System

Onfinity has two major screen families: dashboard screens and window/detail screens. Use the correct layout model for the screen type.

### Screen Types

- Dashboard screens use the 9-column widget grid.
- Window/detail screens use a two-column master-detail layout.
- Screens must be classified by view variant before layout details are designed.
- Do not force the 9-column widget grid onto window/detail screens.
- Do not use the two-column master-detail layout for dashboards unless the screen is explicitly a record browser/detail view.

### Screen Variants

Every module screen should be treated as one of these view variants before any detailed layout is generated.

- `Only dashboard`
  - Uses the 9-column widget grid only.
  - Background should remain transparent so the app gradient stays visible.
  - No white full-surface work area should sit behind the dashboard unless explicitly required by the design.
- `Dashboard with window`
  - Supports both dashboard and window modes.
  - Dashboard mode follows the transparent 9-column widget layout.
  - Window mode should use a white or approved translucent white work surface depending on the module pattern.
  - Example reference: Opportunities.
- `Only window`
  - Uses a working surface instead of a dashboard widget canvas.
  - Background should normally be white or an approved white panel surface.
  - Use this for record browsers, operational lists, forms, communication review, and similar task-first screens.
  - Example reference: Inbox.

Additional support panels:

- Any screen variant may also include:
  - right panel
  - bottom panel
- These support panels do not change the parent screen variant.
- Example:
  - Inbox = `Only window` + `bottom panel`
  - Sales Proposal = `Only window` + `right panel`
  - Opportunities = `Dashboard with window`, and its window mode may include a `right panel`

Variant background rules:

- `Only dashboard`
  - transparent workspace
  - dashboard widgets float over the app gradient
- `Dashboard with window`
  - dashboard mode stays transparent
  - window mode uses a white or approved working surface
  - the white working surface must not appear in dashboard mode
- `Only window`
  - primary workspace should read as a white operational surface
  - avoid making it look like a dashboard widget canvas

Title bar background rule:

- The module title bar should keep the same semi-transparent shell treatment across dashboard and window screens.
- Do not turn the module title bar solid white just because the content area below is a window surface.
- White background belongs to the working surface below the title bar, not to the title bar itself.

Variant behavior rule:

- If a screen supports both dashboard and window:
  - dashboard mode keeps the transparent dashboard canvas
  - window mode gets the working-surface background
  - do not apply the window background styling to the dashboard view

### Shared Dashboard Grid

This is the default grid for all module dashboard/widget screens. Do not create a different widget grid per module.

- Columns: `repeat(9, minmax(0, 1fr))`.
- Gaps: `12px`.
- Use the same grid logic for Finance, CRM, Tasks, Calendar, Prospecting, and any future module dashboard.
- Dashboard area background should remain transparent so the app gradient workspace remains visible.
- Do not place a solid or semi-opaque panel behind the whole dashboard grid unless a special design explicitly requires it.

### Shared Widget Size System

All dashboard widgets should use the same size system so they remain visually compatible when mixed inside the same 9-column layout.

- Widget sizes are expressed as `column span x row span`.
- `1x1` widgets must be square.
- The row height unit must be derived from the same base block size used for the 9-column grid so that all widgets preserve their intended proportions.
- A widget should never invent a custom height that breaks the shared ratio system unless the product owner explicitly approves an exception.

Supported shared sizes:

- `1x1`: square quick action, compact stat, or single-focus action widget.
- `2x1`: compact horizontal summary/resource widget.
- `2x2`: stacked small list or dual-summary widget.
- `3x1`: wide KPI or compact comparison widget.
- `3x2`: medium analytics, calendar, inbox, queue, or list widget.
- `3x3`: tall support or detail-summary widget.
- `4x2`: medium-wide worklist or summary workspace.
- `5x2`: wide analysis or support workspace.
- `6x2`: primary wide dashboard workspace.
- `6x3`: large operational board or primary pipeline workspace.

### Shared Dashboard Layout Rules

- Preferred top-row pattern when a quick action exists:
  - `1 + 2 + 2 + 2 + 2 = 9`.
  - First widget is the quick action.
  - Next four widgets are KPI/summary widgets.
- Preferred main content pattern:
  - Primary workspace: `6` columns.
  - Right support area: `3` columns.
- Preferred lower content pattern:
  - `4 + 5` split or `3 + 3 + 3` split depending on density.
- Use only the supported widget sizes above to fill the dashboard.
- Keep all widgets snapped to the 9-column grid.
- Do not use free-positioned cards or arbitrary widths.
- Do not use 12-column grids in dashboard layouts unless a new system standard is explicitly approved.

### Window / Detail Screen Layout

Use this pattern for screens where the user selects a record from a list and reviews or edits detailed information on the right. Examples: Opportunities window view and Sales Proposal window view.

Window screens may support up to three content states:

- `grid view`
  - used for scanning many records in a dense list or table
  - example: Inbox communication list, customer grids, record browsers
- `card view`
  - used for scanning records in expanded card-like rows without opening the full detail workspace
  - example: Opportunities when the right detail pane is closed
- `detail view`
  - used for record review or editing with the right pane or form workspace open
  - example: Opportunities right-pane detail, customer detail form

These are content states inside a window workflow. They do not replace the higher-level screen variants such as `Only window` or `Dashboard with window`.

- Layout: two columns.
- Left column: record card list.
- Right column: detail workspace with header, stats, tabs/toggle sections, related rows, and activity/history.
- Left column width: approximately `320px` to `380px`.
- Right column: fills remaining width.
- Window/detail workspace background should use a semi-transparent white surface so the content reads as a working panel over the app gradient.
- The window container may use `rgba(255,255,255,0.7)` or similar approved translucent surface values from the shared system.
- Left column should scroll independently if the record list is long.
- Right detail area should scroll independently inside the available workspace.
- Do not use dashboard widget cards in the right detail area unless a stat or summary block clearly needs card treatment.
- Prefer simple rows, tabs, detail sections, and table/list layouts.

Left record list:

- Each record is a full-width clickable card/row.
- Selected record uses blue gradient, left inset rail, and stronger text color.
- Top-right area can show status chips and edit icon affordance.
- Cards should be dense enough for scanning: title, account/company, amount/value, owner/status.
- Avoid nested cards inside record list cards.
- If the window supports both `card view` and `detail view`, closing the right pane may expand the left list into a wider card-row layout.
- In that state, each row should still feel card-based rather than becoming a generic spreadsheet unless the design explicitly calls for a true table.
- A clear reopen action such as `See details` should return the user from `card view` to `detail view`.

Right detail panel:

- Top area shows record identity and shared context.
- Right detail panels should use a dedicated panel header bar whenever the pane has a close action.
- The close icon must live inside this header bar, not as a floating icon over the content area.
- The header bar may also show the pane title on the left.
- Put record-level details such as customer/client, contact person, ID, owner, status, key dates, probability, and quick stats near the top.
- Use tabs for sibling detail sections such as Lines, Activities, Notes, Related Records, and History.
- Use simple table/list rows for data fetched from database tables.
- Activities should be newest first and include timestamp, type, actor, and description.
- Related records should appear below the active record detail or inside a tab, depending on density.

### Detail View Screen

Use this as the standard pattern for window screens whose primary content is a form rather than a list/detail split. This is a `window` screen state, not a dashboard state.

Purpose:

- Support record creation, viewing, and editing in a structured form layout.
- Reuse the window shell pattern with title bar, compact window action bar, and white working surface.
- Allow optional support panels without changing the primary form layout.

Screen model:

- Parent variant:
  - `Only window`, or
  - the `window` state inside `Dashboard with window`
- The main work area is a form workspace.
- The form workspace may also include:
  - right panel
  - bottom panel
- These support panels are optional and sit alongside the form workspace when required.

Layout:

- Primary form layout uses a `4-column` grid.
- Use consistent horizontal spacing between columns.
- Use consistent vertical spacing between field rows.
- The form sits directly on the white working surface.
- Do not wrap the whole top form workspace in an extra rounded card unless a design explicitly calls for a framed subsection.

Column behavior:

- Each form field spans one or more columns depending on content length and importance.
- Standard fields often span `1` column.
- Longer descriptive or lookup-style fields may span `2` columns.
- Special sections may span all `4` columns.
- Keep field alignment strict across rows so labels and underline baselines feel orderly.

Field arrangement:

- Use the shared `Form Field` component pattern for all inputs in this layout.
- Fields may appear:
  - with left icon
  - without left icon
  - with right utility icons
- Arrange fields in clear horizontal rows rather than stacked cards.
- The layout should feel like a structured business form, not a dashboard or settings card wall.

Visual rules:

- White operational surface for the form area.
- Underline-style field treatment, not boxed card inputs.
- Strong horizontal rhythm and alignment between fields.
- Use whitespace and grid alignment to create structure instead of heavy borders.
- If a bottom panel is present, keep a clearer visual break between the form area and the lower contextual section.
- A larger vertical gap before the bottom panel is preferred over tightly stacking the two regions.

Behavior:

- Detail view is a distinct state from grid view.
- If the screen supports both states:
  - `grid view` is used for scanning/selecting records
  - `detail view` is used for record form editing/review
- Support panels may remain available in detail view if the module needs contextual guidance, notes, or related data.

Good uses:

- master record forms
- transaction detail forms
- customer/account detail entry
- sales/order/purchase detail views
- operational record maintenance screens

Do:

- Treat detail view as a structured form workspace.
- Keep the 4-column field grid consistent.
- Use the shared form-field styling and spacing rules.
- Allow right or bottom panel support when the workflow needs contextual help.

Do not:

- Convert detail view into a dashboard-like card mosaic.
- Mix unrelated field widths without grid logic.
- Wrap each row of fields in separate decorative cards unless explicitly required.

### Window Header Panel

Use this pattern for window screens that need a fixed contextual band between the compact window action/search bar and the main scrolling content. Example reference: `Customers` detail view.

Purpose:

- Provide a stable area for record-level actions or compact contextual information.
- Keep important actions visible while the form, grid, or bottom panel scrolls.
- Extend the window shell without introducing another heavy card or dashboard block.

Placement:

- Position: directly below the compact `Window Action Bar`.
- Use only on `window` screens or the `window` mode of a `Dashboard with window` screen.
- The header panel belongs to the fixed shell area.
- It must not scroll with the main content region.

Behavior:

- `title bar`: fixed
- `window action bar`: fixed
- `header panel`: fixed
- `main content below`: scrolls
- If a screen also has a right action panel, the header panel should stop before that rail and reserve the same shell clearance.

Recommended use:

- primary save actions
- record-level action buttons
- short status or context line when explicitly needed
- not for dense stats or dashboard-style KPI blocks

Preferred visual style:

- sleek and quiet
- soft white or near-white surface
- optional very light gradient
- minimal or no inner border
- subtle shadow only
- should feel integrated with the window shell, not like a separate card dropped into the page

Content rules:

- Prefer action-first content.
- If actions are the main purpose, show buttons only.
- Avoid large stat cards in this area.
- Avoid chip clutter unless the design explicitly calls for compact state markers.
- Keep copy short and operational.

Action pattern:

- Primary action may use filled blue treatment.
- Secondary actions use white surface with blue border.
- Disabled actions should remain visible but clearly inactive.
- In detail forms, `Save` actions may be conditionally enabled only when required fields are filled.

Layout:

- Full shell width, minus any right action rail clearance.
- Outer padding should align with the rest of the window shell.
- Inner padding should be slightly tighter than the main form area.
- Keep vertical height compact.
- Do not let this panel become visually taller than necessary.

Do:

- Keep it fixed with the shell.
- Use it for high-value actions that should remain visible.
- Keep it visually lighter than the main content area.

Do not:

- Let it scroll with the form/grid content.
- Turn it into a mini dashboard.
- Add heavy borders or boxed stat tiles by default.
- Duplicate the same record actions in both the action/search bar and the header panel.

### Right Detail Panel Header Bar

Use this pattern at the top of the right-side detail pane in any window/detail screen when the pane needs a clear title row or close control. This prevents floating icons from overlapping content and gives the detail area a predictable structure for AI-generated layouts.

Purpose:

- Provide a consistent top row for the right detail workspace.
- Hold the panel title and optional close icon.
- Reserve space above the scrollable detail content so actions never overlap data.

Variants:

- `with close`: title on the left and close icon on the right.
- `without close`: title only, with the same spacing and height so layouts remain aligned.

When to use:

- Use `with close` when the right detail pane can be dismissed directly from inside the pane.
- Use `without close` when the pane is persistent or the close action already exists elsewhere.
- Use this header for Opportunities, Sales Proposals, and future record-detail windows whenever the right pane starts to feel crowded.
- If a close action exists in the right pane, this header is required.

Layout:

- Position: top of the right detail pane.
- Width: full width of the right pane.
- Height: `56px`.
- Horizontal padding: `18px` to `20px`.
- Layout: horizontal flex row with `justify-content: space-between` and `align-items: center`.
- Background: `#FFFFFF` or the pane's approved white surface.
- Bottom border: `1px solid #E4EDF4`.
- Border radius should follow the parent right pane. Do not create a separate floating card look.
- The header itself should not scroll away if the design expects a fixed pane header. In that case, the content area below it should handle scrolling.
- Preferred structure:
  - left: pane title
  - right: optional context meta and close icon

Title:

- Align left.
- Font: `Roboto Medium` or `Roboto Bold` depending on emphasis.
- Size: `16px`.
- Color: `#141414` or the primary body text color.
- Keep the title vertically centered within the bar.
- Use concise record-space labels such as `Opportunity Details`, `Sales Proposal`, or the active module-specific detail title.
- A short title such as `Opportunity Overview`, `Sales Proposal`, or `Record Details` is preferred.

Optional right-side meta:

- The area before the close icon may show lightweight context such as company name, record count, or related-record count.
- Keep this text small and secondary.
- Font: `Roboto Regular`.
- Size: `12px` to `13px`.
- Color: `#717182` or the shared secondary text color.
- Do not place buttons or status chips in this zone by default.

Close Icon:

- Only present in the `with close` variant.
- Position: far right inside the header bar.
- Hit target: `32px x 32px`.
- Icon size: `20px`.
- Stroke color: `#141414`.
- Stroke width: about `1.8`.
- Use a simple cross icon, not a filled button.
- Keep it vertically centered.

Content relationship:

- The detail content must begin below this header bar.
- Do not place absolute-positioned close icons directly over the content area.
- If the pane body scrolls, keep header and body as separate regions:
  - header: `shrink-0`
  - content: scrollable
- If the pane has a top summary row directly under the header, maintain a clear gap of `16px` to `20px`.
- The first summary block under the header should not repeat the same close affordance.

Behavior rules:

- The `with close` and `without close` variants must share the same height, padding, and title alignment.
- Do not add unrelated action buttons into this bar unless the design explicitly defines a header action area.
- Do not replace this with a floating icon in the corner of the pane.
- Keep this header simpler than the module top title bar. It is a pane header, not a global view-mode switcher.

Do:

- Use this header whenever the right panel needs a top title and/or close action.
- Keep the close icon inside the header, not over the data content.
- Keep the header visually quiet and aligned to the right pane.

Do not:

- Add pills, stats, tabs, or filters directly into this header by default.
- Use a separate raised card for the header.
- Change height or padding screen by screen.

### Bottom Panel Layout

Use this pattern for module screens that need a secondary contextual work area at the bottom of the page instead of a right-side detail pane. This is a master-support layout, not a dashboard widget layout.

Purpose:

- Keep the main working surface visible in the top section.
- Provide a persistent contextual panel for insights, actions, notes, summaries, or related records.
- Reuse the same disciplined panel behavior as the right detail panel without forcing a side-by-side layout.

Layout:

- Overall structure: vertical split.
- Top workspace: `70%` to `80%` of available height.
- Bottom panel: `20%` to `30%` of available height.
- Default recommendation: `72% / 28%` split unless a module needs a different balance.
- The screen should remain one unified module view, not two disconnected cards.
- Top and bottom sections should scroll independently when needed.
- Bottom panel should span the full content width of the module workspace.
- Do not rely on one common page scroll for both regions.
- When the design uses a tinted lower section, that tinted treatment may run edge-to-edge across the full width of the content area.
- In that pattern, the lower panel header and body should read as one continuous band.

When to use:

- Use this pattern when the primary user task is list review, form entry, or grid work in the top section, and supporting intelligence or actions belong below.
- Use it for Inbox, communication review, AI analysis, quick CRM actions, task recommendations, logs, or contextual history.
- Do not use it for standard dashboard screens.

Top workspace:

- Can be a table, list, form, grid view, or communication browser.
- Must remain the primary working area.
- Should not visually collapse into a small strip when the bottom panel is present.
- Keep top content operational and scannable.
- For window screens, the primary top workspace should usually sit directly on the white working surface.
- Do not add an extra rounded border wrapper around the main top workspace unless the design explicitly needs a separate framed sub-panel.

Bottom panel:

- Use a white or approved translucent white surface, consistent with right detail panels.
- Must include a dedicated panel header bar when the bottom area has a title, close action, or collapse action.
- The content below the header is free-form but should still follow shared panel spacing, type, and list conventions.
- Prefer simple sections, rows, grouped actions, compact summaries, and operational recommendations.
- A subtle tinted background may be applied across the entire lower section to distinguish it from the white form/work area above.
- That tint can extend across both the bottom panel header and body when a continuous-band treatment is preferred.

Behavior:

- The bottom panel may be:
  - persistent
  - closable
  - collapsible
- If closable, closing the bottom panel should allow the top workspace to expand vertically.
- If reopened, the layout should restore the intended split.
- The panel should update contextually based on the selected item in the top workspace when the screen design calls for linked behavior.

Variants:

- `with close`: title at left, close icon at right.
- `without close`: title only.
- `collapsed`: header remains visible, body is hidden.

Scroll rules:

- Top workspace scrolls independently inside the top region.
- Bottom panel body scrolls independently inside the bottom region when its content exceeds available height.
- Do not use one shared scroll container for both the top workspace and bottom panel body.
- Header stays fixed relative to the bottom panel body.
- Do not let the full browser page become the scrolling container if the module shell already owns the viewport.

Adaptive height rule:

- Bottom panels do not always need a fixed height split.
- When the workflow benefits from showing more bottom-panel content without compressing the top workspace, let the form/work area and bottom panel stack naturally inside one module scroll container.
- In this mode:
  - top content keeps its natural height
  - bottom panel can grow vertically
  - the overall module content area scrolls
  - avoid internal bottom-panel scrolling until it becomes necessary
- Use this especially for detail-view forms where the bottom panel contains timeline, notes, or action context that should be visible without squeezing the form above.
- This same stacked-scroll mode may also be used for `Only window` list screens when the lower panel is important enough that it should grow naturally instead of being trapped inside a short fixed-height area.
- Example references:
  - Customers detail view
  - Inbox with analysis bottom panel
- In stacked-scroll mode:
  - the module content area becomes the single scroll owner
  - the top workspace should not keep a second internal scroll unless the dataset or design explicitly requires virtualization or a fixed header region
  - the bottom panel should remain free of internal scrolling until its content becomes unusually long
- Choose one bottom-panel scroll model per screen and keep it consistent:
  - `split-scroll`: top and bottom regions scroll independently
  - `stacked-scroll`: the whole module content scrolls as one page-like workspace

Separation rule:

- For detail-view forms with a bottom panel, use a clearer gap between the top form area and the lower panel.
- Prefer approximately `24px` to `32px` of visual separation before the lower panel begins when the layout allows.
- This separation should make the form feel primary while still keeping the lower panel visually connected.

Visual rules:

- Use the same spacing rhythm as right detail panels.
- Keep the panel header visually calm and utility-focused.
- When a screen also has a right `Window Action Panel`, keep the outer tinted lower band full-width.
- Do not shrink the entire bottom band with a right margin or global right padding.
- Instead, apply the action-rail clearance to the inner bottom-panel header/body content only.
- The lower panel should still feel edge-to-edge, while the content inside keeps breathing room from the rail.
- Use symmetric-looking internal spacing:
  - left side follows normal panel padding
  - right side should reserve rail width plus extra breathing room when needed
- In practice, the right inset should be slightly larger than the raw rail width so the content does not appear to touch the action strip.
- Avoid a dashboard-card mosaic inside the bottom panel unless explicitly requested.
- Prefer rows, chips, short summaries, and grouped action buttons.

### Bottom Panel Header Bar

Use the same mental model as the right detail panel header bar, adapted for horizontal placement at the bottom of the screen.

Layout:

- Position: top edge of the bottom panel.
- Height: `56px`.
- Horizontal padding: `18px` to `20px`.
- Background: `#FFFFFF` or approved panel surface.
- Bottom border: `1px solid #E4EDF4`.
- Layout: horizontal flex row with `justify-content: space-between` and `align-items: center`.

Title:

- Font: `Roboto Medium` or `Roboto Bold`.
- Size: `16px`.
- Color: `#141414`.
- Keep the title concise and operational.

Right side:

- May contain lightweight context meta and optional close/collapse icon.
- Meta text should use `Roboto Regular`, `12px` to `13px`, color `#717182`.
- Close or collapse icon should use a `32px x 32px` hit target and `20px` icon size.

Rules:

- If the bottom panel has a close action, it must be inside this header.
- Do not float close or collapse icons over the bottom panel body.
- Keep the body content clearly separated below the header.

### Inbox Bottom Panel Pattern

Use Inbox as the default reference implementation for the bottom-panel screen family.

Screen structure:

- Screen variant: `Only window`.
- Primary workspace background: white operational surface.
- Top section: communication list.
- Bottom section: Inbox analysis and CRM quick actions.

Top section guidance:

- Show communications in a list or row-based table.
- Each row should support selection.
- Rows should feel like an operational queue, not large email cards.
- The top section should sit on a white work surface, not a transparent dashboard surface.
- Suggested fields:
  - channel
  - subject or preview
  - contact
  - company
  - owner
  - received time
  - priority
  - status

Bottom section guidance:

- Title examples:
  - `Inbox Analysis`
  - `CRM Quick Actions`
  - `Communication Summary`
- The bottom panel content should update from the selected communication when applicable.
- If no communication is selected, show queue-level analysis and suggested actions.

Recommended content blocks:

- `Message Summary`
  - short summary of the selected message
  - customer intent
  - urgency
  - sentiment or response risk
- `Detected CRM Context`
  - contact name
  - company
  - linked customer or prospect
  - possible opportunity or proposal connection
- `Suggested Actions`
  - create lead
  - create task
  - draft reply
  - schedule follow-up
  - attach to opportunity
  - link to customer/contact
- `Warnings / Highlights`
  - no owner assigned
  - high-priority message pending
  - opportunity-linked message needs response
  - unresolved customer concern

Layout guidance inside the bottom panel:

- Prefer `2` columns when the panel is wide enough:
  - left: message/context summary
  - right: recommended actions and CRM next steps
- On narrower layouts, stack sections vertically.
- Use simple section titles, compact rows, chips, and capsule action buttons.

Do:

- Treat the bottom panel as contextual intelligence, not as a second inbox.
- Keep actions operational and directly useful.
- Keep the communication list dominant in the top section.

Do not:

- Turn the bottom panel into another dashboard widget grid.
- Fill it with decorative charts unless analytics is the main requirement.
- Duplicate the entire selected message body unless the design explicitly calls for a reading pane.

## 4. Color Palette

### Brand and Navigation

- Primary blue: `#0083DA`
- Action blue: `#1F83FF`
- Deep navy: `#002640`
- Home icon block: `#2084C4`
- Link blue: `#106AB0`
- Light active blue surface: `#EAF8FF`
- Active gradient end: `#CAEDFF`
- Pale blue border: `#BFE4FF`

### Backgrounds and Surfaces

- App background start: `#C7E8FF`
- App background end: `#FFFFC4`
- Widget surface gradient start: `rgba(255,255,255,0.70)` or `rgba(255,255,255,0.82)`
- Widget surface gradient end: `rgba(255,255,255,0.49)` or `rgba(255,255,255,0.58)`
- Solid panel surface: `#FFFFFF`
- Secondary panel surface: `#FBFDFF`
- Faint blue panel surface: `#EEF6FF`
- Muted row divider: `#EDF2F6`
- Light border: `#E4EDF4`
- Figma/design-doc info frame border: `rgba(0,0,0,0.10)`

### Text

- Primary text: `#000000` for Figma-exported foundation blocks, otherwise prefer `#102C3F` or `#111827` for app UI.
- Secondary text: `#5F7283`
- Muted text: `#748494`
- Disabled/placeholder text: `#9F9F9F`
- Breadcrumb inactive text: `rgba(0,0,0,0.4)`

### Semantic Colors

- Success: `#019D89`, `#0B6B45`, `#20A464`
- Danger/Lost/Blocked: `#ED1C24`, `#D14545`, `#A33F3F`
- Warning/Negotiation: `#D78B10`, `#9A6500`
- Proposal/secondary accent: `#8B7CFF`, `#5F4AA6`
- Info pill background: `#DFF1FF`, `#EEF8FF`

### Gradients

- App: `linear-gradient(129deg, rgb(199,232,255), rgb(255,255,196))`
- Standard glass widget: `linear-gradient(180deg, rgba(255,255,255,0.7), rgba(255,255,255,0.49))`
- Module panel: `linear-gradient(180deg, rgba(255,255,255,0.82), rgba(255,255,255,0.58))`
- Selected list row: `linear-gradient(109deg, #EAF8FF 0%, #CAEDFF 100%)`
- Module action bar: `linear-gradient(180deg, rgba(230,243,252,0.65), rgba(245,250,253,0.72))`

## 5. Figma Foundation Variables

These variables were read from the Figma Onfinity Design System component foundation, especially the Tab component node `2425:2268`. Prefer these token names when creating reusable CSS variables or component APIs.

### Color Variables

- `color/text/default`: `#141414`
- `color/text/inverse`: `#FFFFFF`
- `color/surface/page`: `#FFFFFF`
- `Color/Primary`: `#0083DA`
- `color/primary/hover`: `#0069AE`
- `color/primary/pressed`: `#004F83`
- `color/secondary/enabled`: `#E5F3FB`
- `color/secondary/hover`: `#CCE6F8`
- `color/disabled`: `#616161`
- `Color/On surface`: `#080808`
- `Color/On surface disabled`: `#474747`

### Typography Variables

- `Text/Styles/Body`: `Roboto`
- `text/height/sm`: `14`
- `text/height/md`: `16`
- `scale/maximal/4`: `400`
- `Onfinity DS/Paragraph/P2`: Roboto Regular, `14px`, weight `400`, line height `100%`, letter spacing `0.25px`
- `Onfinity DS/Paragraph/P1`: Roboto Regular, `16px`, weight `400`, line height `100%`, letter spacing `0.5px`

### Spacing and Shape Variables

- `space/2xs`: `2`
- `space/md`: `8`
- `space/xl`: `12`
- `space/2xl`: `16`
- `border/stroke/normal`: `2`
- `border/radius/full`: `999`
- `scale/11`: `20`
- `scale/14`: `28`

### Variable Usage Rules

- Use Figma variable names as the semantic source when possible, then map them to project CSS/Tailwind values.
- Keep `Roboto` as the only text family unless the whole typography system is intentionally revised.
- Use `color/text/default` for tab labels and simple component text.
- Use `Color/Primary`, `color/primary/hover`, and `color/primary/pressed` for interactive blue states.
- Use `color/secondary/enabled` and `color/secondary/hover` for hover/pressed variant backgrounds.
- Use `border/radius/full` for badges and capsule controls.
- Use `space/2xl` and `space/xl` for tab padding: `16px` horizontal and `12px` vertical.

## 6. Typography

### Product UI Font

- Primary app font: `Roboto`.
- Available weights: `300`, `400`, `700`.
- Use `fontVariationSettings: "'wdth' 100"` where existing components use it.

### Design System Documentation Font

- Use `Roboto` for design-system documentation/reference frames as well.
- Documentation headings should use Roboto Bold, `40px`, black.
- Documentation descriptions should use Roboto Bold, `16px`, black.
- Do not introduce secondary fonts unless the full product typography system is intentionally revised.

### Type Scale

- Module title bar label: `16px`, regular, black.
- Breadcrumb: `18px`, bold.
- Widget title: `22px` to `24px`, bold or regular depending on legacy pattern.
- KPI value: `30px`, `34px`, `40px`, `52px`, or `70px` depending on widget size.
- Table/list header: `13px`, regular, muted.
- Row title: `14px` to `16px`, bold.
- Row metadata: `12px` to `14px`, regular, muted.
- Chip/pill text: `11px` to `13px`, bold for counts/status, regular for simple labels.

### Typography Rules

- Use bold for record names, totals, active values, and key statuses.
- Use regular for labels, descriptions, metadata, and explanatory copy.
- Keep utility copy short and operational.
- Avoid marketing copy in module dashboards.
- Prefer sentence case for labels and headings unless the source data is a proper noun.

### Responsive Unit Rules

Use relative units for content sizing so widgets and modules scale more gracefully across different screen sizes.

- Prefer `em` for:
  - font sizes
  - line heights
  - icon sizes inside content areas
  - content padding
  - content gaps
  - badges, pills, and compact control sizing inside widgets
- Prefer component-local sizing:
  - set a base font size on the widget, panel, or module content area
  - derive inner spacing and typography from that base using `em`
- Keep shell-defining measurements explicit when needed:
  - viewport shell heights
  - top bar height
  - bottom task bar height
  - left nav width
  - exact dashboard grid math
  - title bar height
- For dashboard widgets:
  - the grid block system may use explicit or computed layout units
  - the content inside each widget should still use `em` sizing
- Do not mix arbitrary pixel-based typography across widgets.
- If a widget needs to scale down for smaller screens, reduce the base font size of that widget or its container instead of rewriting each child size independently.

### CSS Variable Scope Rules

Keep widget-level CSS variables from clashing with app-shell or parent-page variables.

- Do not define generic widget-local tokens such as `--text`, `--radius`, `--accent`, `--muted`, `--size`, or `--pad-x` when the widget may be embedded inside a larger screen.
- Namespace widget-local variables with a clear component prefix.
- Good examples:
  - `--work-schedule-title`
  - `--resource-widget-accent`
  - `--lead-sources-line`
- Avoid generic examples:
  - `--title`
  - `--line`
  - `--warning`
- Shared app-shell variables may stay generic only if they are part of the system-level token layer defined intentionally for the whole screen.
- Standalone demo widgets, reusable widget HTML files, and embedded widget snippets should always prefer namespaced local variables.
- If a widget consumes shared design-system variables from the parent, map them into local namespaced aliases instead of mixing shared and generic local token names in the same component.

## 7. Spacing, Radius, Borders, Shadows

### Spacing

- Base grid gap: `12px`.
- Module content padding: `18px`.
- Large detail panel padding: `22px` to `24px`.
- Card/widget inner padding: `16px` to `18px`.
- Compact row padding: `10px` to `14px`.
- Icon/text gap: `8px` to `12px`.
- Section vertical gap: `12px` to `18px`.

### Radius

- Small controls: `8px`.
- Standard widgets: `12px` or `14px`.
- Large panels/detail sections: `16px` to `18px`.
- Pills/buttons: `999px`.
- Quick-action icon well: `14px`.

### Borders

- Widget border: `2px solid #FFFFFF`.
- List row divider: `1px solid #EBEBEB` or `#EDF2F6`.
- Module title bar bottom border: `1px solid #1F83FF`.
- Standard content border: `1px solid #E4EDF4`.
- Figma/design-doc info frame: `1px solid rgba(0,0,0,0.1)`.

### Shadows

- Widget shadow: `0 10px 24px rgba(15,61,97,0.06)`.
- Active list row shadow: `inset 4px 0 0 #0083DA, 0 12px 24px rgba(31,131,255,0.10)`.
- Quick action icon shadow: `0 10px 24px rgba(31,131,255,0.22)`.
- Search pill shadow: `0 6px 14px rgba(16,47,74,0.06)`.

Use shadows sparingly. Depth should clarify selection and hierarchy, not decorate every element.

## 8. Component Patterns

### Glass Widget

Use for all dashboard widgets across all modules.

- Surface: white translucent gradient.
- Border: `2px solid white`.
- Radius: `12px` to `14px`.
- Padding: `16px` to `18px`.
- Shadow: subtle blue-gray shadow.
- Content aligns to the 9-column grid.
- Use this as the standard default widget surface unless a widget pattern explicitly defines a tinted variant.
- Do not redesign widget chrome per module.

### Widget Edge Spacing

Use this rule for all dashboard widgets regardless of module.

- Every widget must preserve consistent breathing room from all four inner edges.
- Default widget inner padding:
  - top: `16px`
  - left: `16px`
  - right: `16px`
  - bottom: `18px`
- Larger widgets may increase internal spacing slightly, but should not reduce below this baseline.
- Do not let text, pills, charts, lists, or subpanels visually touch the widget edge.
- If a widget contains nested panels, lists, or split areas, the outer widget padding still applies before inner content begins.
- If content is dense, make the inner content area scroll or compress the inner layout before removing edge spacing.
- Bottom spacing must remain visibly present even in tall or content-heavy widgets.

### Widget Footer Pager

Use one consistent bottom pager pattern for widgets that page through records, people, or states.

- Footer layout: three columns
  - left pager button
  - centered status or summary label
  - right pager button
- Footer should sit at the bottom of the widget content area.
- Pager button size: about `2.4em`.
- Pager button shape: circular.
- Pager button fill should come from the widget or system `primary` color.
- Do not hard-code pager fill to a single orange value.

### Form Field

Use this as the default Onfinity form-field pattern for input-like controls in forms, filters, and structured data-entry screens. Reference source: Onfinity Mobile UI Figma node `2865:2734`.

Field structure:

- Surface: white.
- Default border treatment: bottom border only.
- Default bottom border color: `#D7D7D7`.
- Layout: horizontal row with optional left icon block, text content area, and optional right utility icons.
- Typical field padding: `12px` horizontal and `8px` vertical.
- Keep the field visually clean and flat, not card-like.

Content model:

- Label appears above value/input text inside the field.
- Label style:
  - `Roboto Regular`
  - `12px`
  - black or near-black
- Value / entered text style:
  - `Roboto Regular`
  - `16px`
  - black
- Vertical gap between label and value: about `8px`.

Left icon variant:

- Fields may appear `with left icon` or `without left icon`.
- If present, the left icon sits inside its own light framed block.
- Left icon block:
  - fixed square size: `34px x 34px`
  - subtle border: `1px solid #F0F0F0`
  - centered icon
- The icon block must remain square, not rectangular.
- The icon block should align vertically to the middle of the field content.
- Use this variant for date, organization, person, or other clearly identifiable field types.

Right utility icons:

- Fields may include utility icons on the right when the field type requires them.
- Typical examples:
  - dropdown arrow
  - calendar trigger
  - more menu with three vertical dots
- Right-side icons should use the primary color.
- Preferred primary icon color: `#0083DA`.
- Keep right icons compact and aligned vertically with the field content.
- If both a field-type icon and overflow icon are present, group them cleanly on the right with even spacing.

Interaction:

- On hover, the bottom border line should change from grey to the primary blue color.
- This hover behavior should be consistent whether or not the field has left or right icons.
- If the field supports focus, follow the same underline emphasis direction as hover unless a stronger focus treatment is defined later.

Variants:

- `with left icon`
- `without left icon`
- `with right utility icons`
- combinations of the above are allowed when appropriate

Do:

- Keep form fields flat, structured, and operational.
- Use underline emphasis instead of full outlined input boxes when following this pattern.
- Keep labels small and values readable.
- Use primary blue for interactive utility icons and hover underline.

Do not:

- Turn these fields into rounded cards.
- Use a full heavy border when the design calls for underline-only treatment.
- Mix unrelated decorative icons into the field chrome.
- If a widget needs a warm/orange pager, set that widget's `primary` token accordingly.
- Pager icon color: white.
- Pager icon size: about `1.4em` to `1.55em`.
- Pager shadow: subtle shadow derived from the same `primary` color family.
- Footer center label:
  - use centered alignment
  - use widget title/text color
  - size should stay modest so it does not compete with primary content
- Use the same pager visual treatment across all widgets. Do not create a different arrow button style per widget.

### Quick Action Widget

Use as the first widget in module top rows.

- Grid span: `col-span-1`.
- Border: `2px dashed #9ED1FF`.
- Surface: pale blue-to-white gradient.
- Radius: `14px`.
- Padding: `16px`.
- Min height: about `140px`.
- Icon well: `40px`, blue background `#1F83FF`, white icon.
- Title: `22px`, bold, `#102C3F`.
- Copy: `12px`, regular, muted.
- Examples: `New Meeting`, `New Task`, `New Prospect`.

### KPI/Summary Widget

- Grid span in top row: `col-span-2` when quick action exists.
- For rows without quick action, first KPI may span 3 columns and the rest 2 columns.
- Label: `13px`, muted.
- Value: `34px`, bold.
- Detail pill: small white translucent surface with `11px` uppercase caption and `13px` bold detail.
- Use soft tinted background based on metric type:
  - Info: pale blue.
  - Success: pale green.
  - Warning: pale amber.
  - Secondary/probability: pale violet.

### Data Table / Worklist

Use simple row layout, not nested cards.

- Header row: muted `13px`, bottom border.
- Body row: `14px` to `15px`, border bottom `#EDF2F6`.
- Title cell: bold `15px`, `#102C3F`.
- Metadata: `12px`, muted.
- Use CSS grid columns for alignment.
- Keep rows compact and easy to scan.

### Left Record List Card

Used in Sales Proposal and Opportunity window views.

- Width: approximately `380px`.
- Full-row clickable button.
- Padding: `18px`.
- Border bottom: `#EBEBEB`.
- Selected state: blue gradient and inset blue left rail.
- Top-right edit icon:
  - Size: `28px`.
  - Shape: circle.
  - Border: `#E1E8EF` default, `#9ED1FF` when selected.
  - Icon: lucide `Pencil`, `14px`, stroke about `1.9`.
- Do not nest a button inside the clickable card. Use a non-button icon span unless a separate edit action is implemented with proper event handling.

### Status Chip

- Shape: pill, radius `999px`.
- Padding: `10px 5px` or `12px 6px` depending on density.
- Text: `12px` to `13px`.
- Accepted: green background `#CCEFDD`, text `#0C5D38`.
- Rejected: red background `#FAD7D7`, text `#8F2D2D`.
- Draft: gray background `#E1E1E1`, text `#505050`.
- Sent: blue background `#D9ECFF`, text `#0E5DA8`.

### Buttons

- Primary button:
  - Blue background `#1F83FF` or `#0083DA`.
  - White text.
  - Capsule shape.
  - No heavy shadow except for quick action icon wells.
- Secondary button:
  - White or transparent background.
  - Blue border.
  - Blue text.
  - Capsule shape.
- Icon action button:
  - Square or circular touch target around `32px`.
  - Icon size `18px` to `20px`.
  - Neutral gray icon by default.

### Module Title Bar

- Height: `64px`.
- Background: translucent white.
- Bottom border: `#1F83FF`.
- Left label: `16px`, regular, black.
- Right side should usually contain only close icon unless a module specifically needs dashboard/window toggles.
- Sales Proposal and Tasks use close-only title bars.
- Opportunities may use dashboard/window toggle icons in title bar.

### Action Bar

Used under Sales Proposal title bar.

- Background: pale blue vertical gradient.
- Padding: `10px 16px`.
- Left side: action icons for home, back, undo, new record, delete, save, save-plus.
- Right side: search pill, filter icon, overflow icon.
- Keep icons aligned and compact.

### Toggle Group

- Use when there are only two options, such as `Lines` and `Activities`.
- Capsule buttons.
- Active option: blue filled or pale blue selected state.
- Secondary option: blue border and blue text.
- Place contextual edit action beside the toggle when it affects only the active content area.

### Tab Component

Use tabs to organize and navigate between groups of related content at the same hierarchy level. If there are only two options and the interaction is closer to a switch, prefer the Toggle Group pattern. Use the Tab component when the content behaves like sibling sections within a screen or detail panel.

Source reference: Figma Onfinity Design System, node `2425:2268`.

Variants:

- Form: `Horizontal`, `Vertical`.
- Content options: label only, leading icon + label, label + badge, icon + label + badge.
- States: `selected`, `enabled`, `hovered`, `pressed`, `disabled`, `hover variant`, `pressed variant`.

Base tab anatomy:

- Container.
- Optional leading icon.
- Label.
- Optional badge.
- Selected indicator is a bottom border.

Base sizing:

- Container padding: `16px` left/right and `12px` top/bottom.
- Horizontal container gap: `8px` between tab info and badge.
- Vertical container gap: `0px`; icon and label stack inside the tab info group.
- Tab info gap: `2px`.
- Icon size: `20px`.
- Text size: `14px`.
- Text letter spacing: `0.25px`.
- Badge height: `20px`.
- Badge min width: `28px`.
- Badge horizontal padding: `8px`.
- Badge radius: `999px`.

Base colors:

- Surface: `#FFFFFF`.
- Text default: `#141414`.
- Primary: `#0083DA`.
- Primary hover: `#0069AE`.
- Primary pressed: `#004F83`.
- Secondary enabled: `#E5F3FB`.
- Secondary hover: `#CCE6F8`.
- Disabled: `#616161`.
- Text inverse: `#FFFFFF`.

Horizontal tab behavior:

- Selected: white surface, default text, optional primary badge, bottom border `2px solid #141414`.
- Enabled: white surface, default text, optional primary badge, no bottom border.
- Hovered: white surface, hover-blue text/icon, optional primary badge.
- Hover variant: secondary enabled background `#E5F3FB`, hover-blue text/icon, optional primary badge.
- Pressed: white surface, pressed-blue text/icon, optional primary badge.
- Pressed variant: secondary hover background `#CCE6F8`, pressed-blue text/icon, optional primary badge.
- Disabled: white surface, disabled text/icon, disabled badge background `#616161`.

Vertical tab behavior:

- Icon and label stack vertically.
- Selected: white surface, default text/icon, bottom border `2px solid #141414`.
- Enabled: white surface, default text/icon.
- Hovered and hover variant: hover-blue text/icon; hover variant uses `#E5F3FB` background.
- Pressed and pressed variant: pressed-blue text/icon; pressed variant uses `#CCE6F8` background.

Implementation rules:

- Use `Roboto` for labels and badges.
- Use regular weight for tab labels unless the product context needs stronger hierarchy.
- Use button semantics for interactive tabs.
- Mark selected tab with `aria-selected`.
- Keep tab lists keyboard navigable when implemented as real tab panels.
- Do not use heavy cards around tabs; tabs should sit directly on the relevant content surface.
- Do not use tabs where a simple two-option toggle is clearer.

### Activity Timeline

- Sort newest first.
- Include activity type, icon, title, timestamp, actor when available.
- Activity types: call, email, meeting, revision, viewed, created.
- Use simple rows with dividers instead of heavy cards.
- Revision activity can include a link/button to open a compact changes popup.

### Opportunity Stage Snapshot

- Show opportunity-level notes and recent activity inside the opportunity detail, not globally in the header.
- Notes and recent activity can be two columns on wider layouts.
- Probability should be colored by percentage:
  - Low: red/amber.
  - Medium: amber/violet.
  - High: green/blue.
- If multiple opportunities belong to the same customer, show the current opportunity detail first and related opportunities below.

### Calendar Widget

- Top row uses `New Meeting` quick action plus four KPI widgets.
- Main schedule uses a 6-column span and day columns inside it.
- Today agenda uses the 3-column support panel.
- Follow-up queue should use simple rows, not nested cards.

### Task Widget

- Top row uses `New Task` quick action plus four KPI widgets.
- Main queue uses row/table layout.
- Focus board and workload spread use simple rows with dividers, not mini cards.

### Prospect Widget

- Top row uses `New Prospect` quick action plus four KPI widgets.
- Prospect grid shows company, contact, source, score, stage, value, response time.
- Conversion funnel and score distribution are support widgets.

### Design-System Info Frame

Based on the Figma design-system reference node.

- Surface: white.
- Border: `1px solid rgba(0,0,0,0.10)`.
- Padding: `17px`.
- Gap: `16px`.
- Heading: Roboto Bold, `40px`, black.
- Body: Roboto Bold, `16px`, black.
- Use this style for documentation/reference panels, not necessarily for operational CRM screens.

## 9. Icons

- Preferred app icon library: `lucide-react`.
- Existing Figma-imported SVG icon components are acceptable when already present.
- Standard icon sizes:
  - Nav icons: `24px`.
  - Title/action bar icons: `18px` to `20px`.
  - Section header icons: `22px`.
  - Row/action icons: `14px` to `16px`.
- Icon color:
  - Primary action: `#1F83FF` or `#0083DA`.
  - Neutral action: `#586575`.
  - Disabled/secondary: `#7A8A98`.
- Avoid adding new icon libraries unless already installed and approved.

## 10. Interaction Rules

- Whole left-list cards are clickable for selection.
- Edit icons shown inside list cards are visual affordances unless explicit separate edit behavior is requested.
- Close icon in module title bars returns to the CRM module dashboard.
- Breadcrumb navigation:
  - `Home` always returns to home dashboard.
  - Module name such as `CRM` returns to that module dashboard.
- Module dashboard should clear left-nav selection by default.
- Selecting a module left-nav item highlights exactly one item.
- Keep hover states subtle: pale blue/white surface changes, not strong color shifts.

## 11. Responsive Behavior

- Preserve the 9-column grid concept across screen sizes.
- Scale the grid block size, gaps, and font sizes together when responsive tuning is required.
- Do not change widget semantic sizes randomly. A `3x2` widget remains a `3x2` widget even if the block size changes.
- Only the widget/content workspace should scroll. Avoid double scrollbars.
- Ensure bottom task bar remains flush with viewport bottom.
- On smaller laptop screens, prioritize preserving column visibility and square proportions over increasing content density.

## 12. Implementation Rules

- Stack: React, TypeScript, Vite, Tailwind CSS utilities.
- Main UI implementation currently lives in `src/imports/WidgetOnWindowHome/WidgetOnWindowHome.tsx`.
- Global styles live in `src/styles`.
- Use Tailwind utilities and existing arbitrary values consistently.
- Prefer local component patterns already in the file before introducing new abstractions.
- Use `apply_patch` for manual file edits.
- Keep CSS values explicit when matching Figma or existing widgets.
- Do not introduce unrelated framework changes.
- Do not replace the app-wide visual language when adding a single widget.

## 13. Accessibility and Readability

- Keep text contrast strong on translucent surfaces.
- Interactive elements must be `button` when they trigger navigation or actions.
- Avoid nested buttons.
- Keep touch targets around `32px` minimum for icon actions and `48px` for primary nav icons.
- Use clear labels, not only icons, for important actions when space allows.
- Avoid dense paragraphs inside widgets. Prefer rows, labels, values, and short support text.

## 14. Do and Do Not

Do:

- Use the 9-column grid for every dashboard.
- Use quick-action + four KPI top rows for module dashboards when an action exists.
- Use simple row lists for queues, workload, focus boards, and follow-up queues.
- Keep module title bars consistent.
- Keep Home and CRM breadcrumb navigation available once CRM is opened.
- Use blue as the primary action color.
- Use soft tinted panels only to support meaning.

Do not:

- Use 12-column dashboard grids.
- Add purple default SaaS styling.
- Add heavy card nesting inside widgets.
- Place action buttons in title bars unless the title bar pattern calls for them.
- Let home dashboard show left nav.
- Let multiple left-nav items appear selected.
- Add separate scrollbars to both page and widget area.
- Introduce a new visual system for a single module.

## 15. Module Top Title Bar With Dashboard / Window Selector

Use this title bar pattern for module screens that support two view modes: `dashboard` and `window`. Example reference: `Opportunities`.

Purpose:
- Show the module title.
- Let the user switch between dashboard view and window/detail view.
- Provide a close action at the far right.

Layout:
- Full width horizontal bar.
- Height: `56px`.
- Horizontal padding: `20px`.
- Background: translucent white over the app background.
- Bottom border: `1px solid #1F83FF`.
- Left section: module title + view selector.
- Right section: close icon only.

Title:
- Text: module name, for example `Opportunities`.
- Font: `Roboto Regular`.
- Size: `16px`.
- Color: `#000000`.
- Keep title vertically centered.

View selector:
- Position: immediately to the right of the title.
- Gap between title and selector: `18px`.
- Selector width: `96px`.
- Internal layout:
  - dashboard icon button
  - vertical divider
  - window icon button
- Icon button size: `24px`.
- Divider:
  - visual line length: `17px`
  - color: `#D9D9D9`
- Horizontal gap inside selector: `12px`.

Selector active state:
- Active icon gets a soft blue selected surface behind it.
- Active background: `#EAF8FF`.
- Active border: `1px solid #BFE4FF`.
- Active radius: `8px`.
- Active background inset around icon: about `6px` beyond icon bounds.

Bottom indicator:
- Show a small blue triangle aligned under the active icon.
- Color: `#1F83FF`.
- Shape:
  - left border: `8px solid transparent`
  - right border: `8px solid transparent`
  - bottom: `10px solid #1F83FF`
- Position:
  - sits below the selector and visually touches the title bar bottom border
  - align to the center of the active icon
- Transition:
  - move horizontally when switching between dashboard and window
  - keep movement subtle and precise

Icon behavior:
- `dashboard` icon represents the widget/grid dashboard view.
- `window` icon represents the 2-column master-detail window view.
- Only one mode is active at a time.

Close action:
- Position: far right of the title bar.
- Touch target: `32px x 32px`.
- Icon size: `20px`.
- Icon color: `#141414`.
- Stroke width: `1.8`.
- Function:
  - close the current module screen
  - return user to the module dashboard or previous parent context, depending on screen behavior

Behavior rules:
- Use this title bar only on screens that truly support both dashboard and window modes.
- If the screen is dashboard-only or window-only, remove the selector and keep title + close only.
- Do not place extra action buttons in this title bar.
- Keep the selector centered vertically with the title text.
- Keep the close icon aligned with the title bar centerline.
- Keep spacing and indicator alignment identical across modules.

Do:
- Reuse the exact spacing, border, active state, and indicator pattern for every module.
- Keep icon alignment pixel-consistent.
- Keep the active indicator centered under the chosen icon.

Do not:
- Add pills, search, filters, or overflow actions directly in this title bar.
- Change the selector width per module.
- Use a different active color or indicator style on different screens.

## 16. Window Action Bar

Use this bar directly below the module title bar on `Only window` screens and on the `window` mode of `Dashboard with window` screens when the design requires record actions, search, filters, or overflow tools.

Purpose:
- Hold operational actions for the active window screen.
- Provide search and lightweight utilities without crowding the title bar.
- Separate shell navigation from screen-level actions.

Layout:
- Full width horizontal bar below the title bar.
- Use the approved soft blue-tinted panel treatment:
  - `linear-gradient(180deg, rgba(230,243,252,0.65), rgba(245,250,253,0.72))`
- Horizontal padding: `14px` to `16px`.
- Vertical padding: `7px` to `10px`.
- Layout: left actions cluster and right utilities cluster.
- The bar should feel compact and secondary relative to the title bar.

Left actions cluster:
- Use compact icon buttons in a tight row.
- Standard icon size: `16px` to `18px`.
- Typical actions:
  - home
  - back
  - undo
  - new record / new message
  - delete
  - send / save depending on module
- Keep button rhythm tight and consistent.

Right utilities cluster:
- Search pill may appear on the right.
- Search pill should be compact rather than oversized.
- Typical search field width: `280px` to `320px`.
- Search pill leading icon block may be narrower than the legacy Sales Proposal pattern when a more compact layout is needed.
- Search text should generally be around `14px`.
- Optional filter and overflow icons may appear to the right of search.
- Utility icons should usually be `16px` to `18px`.

Behavior rules:
- Use this action bar for window screens, not for pure dashboard screens.
- Do not move these actions into the module title bar.
- Keep the bar visually lighter than the content area below.
- Match the compact treatment across modules unless a screen has a clear reason to differ.

Do:
- Keep the action bar compact.
- Keep search, filter, and overflow actions grouped on the right.
- Keep action icons operational and familiar.

Do not:
- Turn the action bar into a large toolbar ribbon.
- Use oversized search pills or excessive vertical padding.
- Add decorative content that competes with the working surface.

## 17. Window Action Panel

Use this as the standard right-side action rail for `window` screens when the user needs quick record actions that may open a popup, helper flow, or lightweight side interaction for the current record.

Purpose:
- Provide a dedicated vertical action area for window-only operations.
- Keep secondary record actions out of the main content area.
- Reuse one consistent right-side rail pattern across list, card, and detail window states.

When to use:
- Use on `Only window` screens or on the `window` mode of `Dashboard with window` screens.
- Use when a screen needs many contextual actions and the horizontal action bar alone would become crowded.
- Good first examples:
  - Inbox
  - future record browsers with popup-driven actions
- Do not use on pure dashboard screens.

Relationship to the window shell:
- The action panel is part of the window shell.
- It sits on the far right side of the white working surface.
- It starts directly below the module title bar bottom border.
- It continues alongside the window action bar and the window content area below.
- It is not part of the global bottom task bar.
- It must not create a fake extra strip below the window or push the global bottom bar out of alignment.

Layout:
- Position: right edge of the window workspace.
- Width: approximately `56px`.
- Background: `#FFFFFF`.
- Left divider: `1px solid #E6EDF3`.
- Internal layout: vertical icon stack aligned to the center of the rail.
- Top padding: about `10px`.
- Keep the rail visually narrow and quiet.

Icon rules:
- Standard icon size: `18px`.
- Recommended tap target: `34px x 34px`.
- Default icon color: primary blue `#1F83FF` or approved shared primary blue.
- Hover state: soft blue background such as `#EEF7FF`.
- Icons should usually be neutral action triggers, not persistent selected navigation states.
- Avoid always-on highlighted icons unless the design explicitly calls for one temporary active action.

Behavior:
- Icons may open:
  - popup dialogs
  - quick action forms
  - helper panels
  - record-level tools
- Treat these as contextual actions for the current record or current window selection.
- The panel should be configurable per screen; do not hardcode the same icon set for every module.

Scroll behavior:
- The action panel itself should remain fixed while the main window content scrolls.
- The module title bar remains fixed.
- The horizontal window action/search bar remains fixed.
- Only the window content area below those shell bars should scroll.
- Do not place the window action bar inside the scrolling content region when an action panel is present.

Spacing rules:
- The main scrolling content should reserve horizontal space so content does not slide under the action panel.
- Lower sections such as bottom panels must keep normal left padding and visually balanced right spacing.
- Compensate for the rail inside the lower content layout rather than removing left padding from the whole section.
- Keep the outer window edges visually aligned even when the rail is present.

Do:
- Keep the rail fixed on the right.
- Keep it visually part of the window shell.
- Use it for contextual record actions.
- Align it from just below the title bar through the rest of the window.

Do not:
- Put the rail on dashboard screens.
- Let it push the global bottom bar or global logo area out of place.
- Let it become a second navigation menu.
- Scroll the window action/search bar together with the main content.

## 16. Global Top Menu Bar

Use this as the standard top application bar for the Onfinity dashboard shell. This bar appears above both the home dashboard and module dashboards.

Purpose:
- Provide entry into the active module context.
- Show breadcrumb navigation.
- Show persistent global action icons.
- Show the current user avatar.

Layout:
- Full width horizontal bar.
- Height: `80px`.
- Horizontal padding: `12px`.
- Content aligned center vertically.
- Left cluster: menu button + breadcrumb.
- Right cluster: global icons group + avatar.
- Background stays transparent over the app gradient workspace.

Structure:
- Root layout: `display: flex`.
- Main alignment: `align-items: center`, `justify-content: space-between`.
- Left cluster gap: `20px`.
- Right cluster should sit flush toward the right with a small right margin if needed.

### Menu Button

Purpose:
- Opens or switches into the active module context.
- Current implementation enters the `CRM` module dashboard.

Specs:
- Outer shape: rounded rectangle.
- Size: auto based on icon padding; visual box is about `48px`.
- Padding: `8px`.
- Radius: `8px`.
- Background:
  - `linear-gradient(180deg, rgba(255,255,255,0.7), rgba(255,255,255,0.49))`
- Border:
  - `2px solid #FFFFFF`
- Icon:
  - hamburger/menu icon
  - visual icon size around `32px`
  - color: `#0083DA`

Behavior:
- On click, open the current module dashboard.
- Do not use this button for arbitrary page actions.
- Keep the visual style identical across screens.

### Breadcrumb

Purpose:
- Show current navigation context.
- Allow switching between home dashboard and active module dashboard.

Placement:
- Immediately to the right of the menu button.

Specs:
- Layout: horizontal row.
- Gap between breadcrumb items: `12px`.
- Chevron separator:
  - icon: right chevron
  - size: `14px`
  - stroke width: `2.2`
  - color: black
- Text size: `18px`
- Font: `Roboto Bold`

States:
- Active item color: `#000000`
- Inactive item color: `rgba(0,0,0,0.4)`

Behavior:
- `Home` is always shown.
- When a module has been opened, show `Home > CRM` or `Home > <Module Name>`.
- Clicking `Home` returns to the home dashboard.
- Clicking module name returns to that module dashboard.
- On the home dashboard:
  - `Home` is active
  - module name, if shown, is inactive
- On the module dashboard:
  - module name is active
  - `Home` is inactive

Rules:
- Breadcrumb must always remain visible once the top bar is present.
- Do not replace breadcrumb with page titles.
- Do not add extra controls inside breadcrumb.

### Right Action Area

Purpose:
- Show global pinned/favorite/workspace actions and current user identity.

Placement:
- Right side of the top bar.
- Use `margin-left: auto` on the right cluster if needed.

### Global Icons Group

Specs:
- Visual container height: about `48px`
- Rounded rectangle surface
- Background:
  - `linear-gradient(180deg, rgba(255,255,255,0.7), rgba(255,255,255,0.49))`
- Border:
  - `2px solid #FFFFFF`
- Radius: `8px`
- Internal layout: horizontal row
- Divider between icons:
  - `1px`
  - color: `#E1E1E1`

Icons:
- Count: 2 in current design
- Example actions: workspace/edit and favorite/star
- Icon color: `#0083DA`
- Icon area height: `24px`
- Total visual group width: approximately `72px`
- Padding should keep icons centered and balanced

Behavior:
- Icons are global utility actions, not page-local actions.
- Do not place search, filters, or module-specific controls here.
- Keep icons consistent across home and module screens unless product requirements change globally.

### Top Bar Avatar

Use this avatar pattern consistently in the global top menu bar. Do not redesign it per screen.

Purpose:
- Show the signed-in user identity.
- Balance the right-side utility icon group visually.
- Provide a stable, recognizable anchor in the top shell.

Placement:
- Far right of the top menu bar.
- Sits immediately to the right of the global icons group.
- Keep a small but clear horizontal gap between the icon group and avatar.

Size:
- Visual avatar size: `50px x 50px`.
- Treat this as fixed for the top bar unless the whole shell is intentionally redesigned.

Shape:
- Perfect circle.
- Image is clipped fully to the circular frame.

Frame and surface:
- Use a soft white outer ring / container feel.
- Keep a subtle white edge around the image.
- Allow a very light shadow so the avatar remains visible over the gradient background.
- The avatar should feel like it sits on top of the workspace, not flat inside it.

Image treatment:
- Real user image or profile photo.
- Cover fill.
- Center crop.
- Face should remain centered and readable.
- Do not distort or stretch the image.

Visual style:
- Clean, polished, minimal.
- No square corners.
- No colored border unless a product state explicitly requires one.
- No oversized glow, badge, or notification dot by default.

Alignment:
- Vertically centered with the icon group and breadcrumb row.
- The avatar should visually align with the `48px` utility group even though it is slightly larger.
- Do not let the avatar sit lower or higher than the top bar centerline.

Behavior:
- Avatar is persistent across home and module screens.
- Keep the same size and style everywhere in the app shell.
- Do not replace avatar with initials unless no image is available.
- If initials fallback is required:
  - keep the same `50px` circular container
  - use centered initials
  - use `Roboto Bold`
  - keep the same white framed surface treatment

Do:
- Keep avatar size fixed and consistent.
- Keep image crop clean and centered.
- Maintain the white framed circular look.

Do not:
- Change avatar size screen by screen.
- Use a square or rounded-rectangle avatar in the top bar.
- Add status dots, role badges, or dropdown labels unless explicitly designed.
- Use a darker border that competes with the icon group.

Reference values:
- Avatar size: `50px`
- Shape: `circle`
- Surface feel: white framed / soft white edge
- Placement: right of the global icons group


### Reference Values

- Top bar height: `80px`
- Outer horizontal padding: `12px`
- Left cluster gap: `20px`
- Menu button radius: `8px`
- Menu/global icon surface border: `2px solid #FFFFFF`
- Primary blue: `#0083DA`
- Glass background:
  - `linear-gradient(180deg, rgba(255,255,255,0.7), rgba(255,255,255,0.49))`
- Breadcrumb text: `18px`, `Roboto Bold`
- Breadcrumb chevron: `14px`
- Right utility group divider: `#E1E1E1`




## Reference Sources

- Google Stitch DESIGN.md concept: https://stitch.withgoogle.com/docs/design-md/overview
- Figma design-system reference: Onfinity Design system, node `2425:4585`
- Local product implementation: `src/imports/WidgetOnWindowHome/WidgetOnWindowHome.tsx`
- Local tokens/styles: `src/styles/theme.css`, `src/styles/fonts.css`, `src/styles/tailwind.css`
